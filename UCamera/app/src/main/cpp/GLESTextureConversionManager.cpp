// Copyright 2026 URAV ADVANCED LEARNING SYSTEMS PRIVATE LIMITED
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#include <android/log.h>
#include <mutex>
#include <map>
#include <GLES3/gl3.h>
#include <android/surface_texture_jni.h>

#include "GLES_YUVConverter.h"
#include "IUnityInterface.h"
#include "IUnityGraphics.h"

#define TAG "UXRQC.GLTexConvMgr"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

using namespace std;

struct RenderJob {
    jobject srcTextureJava;
    ASurfaceTexture* srcTextureNative;

    GLES_YUVConverter* converter;
    bool awaitingDispose;
};

static map<GLuint, RenderJob> g_renderJobs;
static mutex g_renderJobsMutex;

//region Kotlin interface

extern "C"
JNIEXPORT jboolean JNICALL
Java_com_uralstech_uxr_questcamera_GLESCaptureSessionManager_bindJob(JNIEnv *env,
                                                                     jobject,
                                                                     jint jobTexId,
                                                                     jobject surfaceTexture) {

    LOGI("Binding surfaceTexture to job.");

    lock_guard<mutex> lock(g_renderJobsMutex);
    if (g_renderJobs.find(jobTexId) == g_renderJobs.end()) {
        LOGE("Unknown job ID provided.");
        return false;
    }

    RenderJob& job = g_renderJobs[jobTexId];
    if (job.srcTextureJava != nullptr || job.srcTextureNative != nullptr) {
        LOGE("Cannot bind to job with already bound surfaceTexture.");
        return false;
    }

    if (job.awaitingDispose) {
        LOGE("Cannot bind to disposing job.");
        return false;
    }

    jobject globalRef = env->NewGlobalRef(surfaceTexture);
    if (globalRef == nullptr) {
        LOGE("Could not create global reference for surfaceTexture.");
        return false;
    }

    job.srcTextureJava = globalRef;
    job.srcTextureNative = ASurfaceTexture_fromSurfaceTexture(env, surfaceTexture);

    LOGI("Surface texture bound.");
    return true;
}


extern "C"
JNIEXPORT void JNICALL
Java_com_uralstech_uxr_questcamera_GLESCaptureSessionManager_unbindJob(JNIEnv *env,
                                                                      jobject,
                                                                      jint jobTexId) {

    LOGI("Unbinding surfaceTexture from job.");

    lock_guard<mutex> lock(g_renderJobsMutex);
    if (g_renderJobs.find(jobTexId) == g_renderJobs.end()) {
        LOGE("Unknown job ID provided.");
        return;
    }

    RenderJob& job = g_renderJobs[jobTexId];

    if (job.srcTextureNative != nullptr) {
        ASurfaceTexture_release(job.srcTextureNative);
        job.srcTextureNative = nullptr;
    }

    if (job.srcTextureJava != nullptr) {
        env->DeleteGlobalRef(job.srcTextureJava);
        job.srcTextureJava = nullptr;
    }

    job.awaitingDispose = true;
    LOGI("SurfaceTexture unbound, awaiting dispose.");
}

//endregion

//region Unity interface

#define EVENTID_SETUP_JOB    1
#define EVENTID_DISPOSE_JOB  2
#define EVENTID_RUN_JOB      3

struct JobSetupData {
    GLuint renderTexture;
    GLint width; GLint height;

    void (*onDone)(GLuint nativeTexture, GLuint renderTexture);
};

struct JobRunData {
    GLuint renderTexture;
    void (*onDone)(int64_t timestamp, GLuint renderTexture);
};

struct JobDisposeData {
    GLuint renderTexture;
    void (*onDone)(bool result, GLuint renderTexture);
};

static void setupJob(void* data) {
    auto setupData = reinterpret_cast<JobSetupData*>(data);
    GLuint renderTexture = setupData->renderTexture;

    lock_guard<mutex> lock(g_renderJobsMutex);
    if (g_renderJobs.find(renderTexture) != g_renderJobs.end()) {
        LOGE("Tried to register texture to multiple jobs!");
        setupData->onDone(0, renderTexture);
        return;
    }

    auto converter = new GLES_YUVConverter(
            renderTexture,
            setupData->width,
            setupData->height
    );

    GLuint newTexture;
    if (!converter->initialize(&newTexture)) {
        LOGE("Could not initialize converter.");
        converter->dispose();
        delete converter;

        setupData->onDone(0, renderTexture);
        return;
    }

    g_renderJobs[renderTexture] = {
            nullptr,
            nullptr,
            converter,
            false
    };

    LOGI("Converter initialized.");
    setupData->onDone(newTexture, renderTexture);
}

static void runJob(void* data) {
    auto renderData = reinterpret_cast<JobRunData*>(data);
    GLuint renderTexture = renderData->renderTexture;

    GLES_YUVConverter* converter;
    ASurfaceTexture* srcTexture;
    bool awaitingDispose;

    {
        lock_guard<mutex> lock(g_renderJobsMutex);
        if (g_renderJobs.find(renderTexture) == g_renderJobs.end()) {
            LOGE("Unknown job ID provided.");
            renderData->onDone(-1, renderTexture);
            return;
        }

        const RenderJob& job = g_renderJobs[renderTexture];
        converter = job.converter;
        srcTexture = job.srcTextureNative;
        awaitingDispose = job.awaitingDispose;
    }

    if (awaitingDispose) {
        LOGE("Cannot run disposing job.");
        renderData->onDone(-1, renderTexture);
        return;
    }

    if (srcTexture == nullptr) {
        LOGE("Job does not have valid source srcTexture.");
        renderData->onDone(-1, renderTexture);
        return;
    }

    if (converter == nullptr) {
        LOGE("Job does not have valid converter.");
        renderData->onDone(-1, renderTexture);
        return;
    }

    bool result = converter->render(srcTexture);
    if (!result) {
        renderData->onDone(-1, renderTexture);
        return;
    }

    int64_t timestamp = ASurfaceTexture_getTimestamp(srcTexture);
    renderData->onDone(timestamp, renderTexture);
}

static void disposeJob(void* data) {
    auto disposeData = reinterpret_cast<JobDisposeData*>(data);
    GLuint renderTexture = disposeData->renderTexture;

    lock_guard<mutex> lock(g_renderJobsMutex);
    if (g_renderJobs.find(renderTexture) == g_renderJobs.end()) {
        LOGE("Unknown job ID provided.");
        disposeData->onDone(false, renderTexture);
        return;
    }

    RenderJob& job = g_renderJobs[renderTexture];
    if (!job.awaitingDispose) {
        LOGE("Cannot dispose job with active source texture.");
        disposeData->onDone(false, renderTexture);
        return;
    }

    if (job.converter != nullptr) {
        job.converter->dispose();
        delete job.converter;
    }

    g_renderJobs.erase(renderTexture);
    LOGI("Job successfully disposed.");

    disposeData->onDone(true, renderTexture);
}

static void UNITY_INTERFACE_API manageConverterJob(int eventId, void* data) {
    if (data == nullptr) {
        LOGE("nullptr passed to manageConverterJob.");
        return;
    }

    switch (eventId) {
        case EVENTID_SETUP_JOB:
            setupJob(data);
            break;

        case EVENTID_RUN_JOB:
            runJob(data);
            break;

        case EVENTID_DISPOSE_JOB:
            disposeJob(data);
            break;

        default:
            LOGE("Unknown event '%i'", eventId);
            break;
    }
}

extern "C" UnityRenderingEventAndData UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
getGLESManageConverterJobEvent() {
    return manageConverterJob;
}

//endregion
