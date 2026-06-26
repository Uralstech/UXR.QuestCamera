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
#include <unordered_map>
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
    jobject srcTextureJava = nullptr;
    ASurfaceTexture* srcTextureNative = nullptr;

    unique_ptr<GLES_YUVConverter> converter;
    bool awaitingDispose = false;
};

static unordered_map<GLuint, unique_ptr<RenderJob>> g_renderJobs;
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
    auto it = g_renderJobs.find(jobTexId);

    if (it == g_renderJobs.end()) {
        LOGE("Unknown job ID provided.");
        return false;
    }

    RenderJob* job = it->second.get();
    if (job->srcTextureJava != nullptr || job->srcTextureNative != nullptr) {
        LOGE("Cannot bind to job with already bound surfaceTexture.");
        return false;
    }

    if (job->awaitingDispose) {
        LOGE("Cannot bind to disposing job.");
        return false;
    }

    jobject globalRef = env->NewGlobalRef(surfaceTexture);
    if (globalRef == nullptr) {
        LOGE("Could not create global reference for surfaceTexture.");
        return false;
    }

    job->srcTextureJava = globalRef;
    job->srcTextureNative = ASurfaceTexture_fromSurfaceTexture(env, globalRef);

    if (job->srcTextureNative == nullptr) {
        LOGE("Could not create native surfaceTexture from java object!");
        job->srcTextureJava = nullptr;
        env->DeleteGlobalRef(globalRef);
        return false;
    }

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
    auto it = g_renderJobs.find(jobTexId);

    if (it == g_renderJobs.end()) {
        LOGE("Unknown job ID provided.");
        return;
    }

    RenderJob* job = it->second.get();
    if (job->srcTextureNative != nullptr) {
        ASurfaceTexture_release(job->srcTextureNative);
        job->srcTextureNative = nullptr;
    }

    if (job->srcTextureJava != nullptr) {
        env->DeleteGlobalRef(job->srcTextureJava);
        job->srcTextureJava = nullptr;
    }

    job->awaitingDispose = true;
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
    GLuint nativeTexture = 0;

    {
        lock_guard<mutex> lock(g_renderJobsMutex);
        if (g_renderJobs.find(renderTexture) != g_renderJobs.end()) {
            LOGE("Tried to register texture to multiple jobs!");
            goto setupJobEnd;
        }

        auto converter = make_unique<GLES_YUVConverter>(
                renderTexture,
                setupData->width,
                setupData->height
        );

        if (!converter->initialize(&nativeTexture)) {
            LOGE("Could not initialize converter.");
            converter->dispose();
            goto setupJobEnd;
        }

        g_renderJobs[renderTexture] = make_unique<RenderJob>(RenderJob{
                .converter = std::move(converter),
        });

        LOGI("Converter initialized.");
    }

setupJobEnd:
    setupData->onDone(nativeTexture, renderTexture);
}

static void runJob(void* data) {
    auto renderData = reinterpret_cast<JobRunData*>(data);
    GLuint renderTexture = renderData->renderTexture;
    int64_t timestamp = -1;

    {
        lock_guard<mutex> lock(g_renderJobsMutex);
        auto it = g_renderJobs.find(renderTexture);

        if (it == g_renderJobs.end()) {
            LOGE("Unknown job ID provided.");
            goto runJobEnd;
        }

        RenderJob* job = it->second.get();
        if (job->awaitingDispose) {
            LOGE("Cannot run disposing job.");
            goto runJobEnd;
        }

        if (job->srcTextureNative == nullptr) {
            LOGE("Job does not have valid srcTexture.");
            goto runJobEnd;
        }

        if (job->converter == nullptr) {
            LOGE("Job does not have valid converter.");
            goto runJobEnd;
        }

        bool result = job->converter->render(job->srcTextureNative);
        if (result) {
            timestamp = ASurfaceTexture_getTimestamp(job->srcTextureNative);
        }
    }

runJobEnd:
    renderData->onDone(timestamp, renderTexture);
}

static void disposeJob(void* data) {
    auto disposeData = reinterpret_cast<JobDisposeData*>(data);
    GLuint renderTexture = disposeData->renderTexture;
    bool result = false;

    {
        lock_guard<mutex> lock(g_renderJobsMutex);
        auto it = g_renderJobs.find(renderTexture);

        if (it == g_renderJobs.end()) {
            LOGE("Unknown job ID provided.");
            goto disposeJobEnd;
        }

        RenderJob* job = it->second.get();
        if (!job->awaitingDispose) {
            LOGE("Cannot dispose job with active source texture.");
            goto disposeJobEnd;
        }

        if (job->converter != nullptr) {
            job->converter->dispose();
        }

        g_renderJobs.erase(it);
        result = true;

        LOGI("Job successfully disposed.");
    }

disposeJobEnd:
    disposeData->onDone(result, renderTexture);
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
