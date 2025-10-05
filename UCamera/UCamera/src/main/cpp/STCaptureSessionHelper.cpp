// Copyright 2025 URAV ADVANCED LEARNING SYSTEMS PRIVATE LIMITED
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
#include <jni.h>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "JNIExtensions.h"
#include "Renderer.h"

#define TAG "STCaptureSessionHelper"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)
#define LOGW(...) __android_log_print(ANDROID_LOG_WARN, TAG, __VA_ARGS__)

using namespace std;

static JavaVM* g_javaVm = nullptr;
static jmethodID g_startCaptureSessionMtd = nullptr;

static map<jlong, jobject> g_registeredSessions;
static mutex g_registeredSessionsMtx;

static map<GLuint, Renderer*> g_renderers;
static mutex g_renderersMtx;

struct SurfaceTexture {
    jobject java;
    ASurfaceTexture* native;
};

static map<GLuint, SurfaceTexture> g_surfaceTextures;
static mutex g_surfaceTexturesMtx;

JNIEXPORT jint JNI_OnLoad(JavaVM* vm, void*) {
    g_javaVm = vm;

    JNIEnv* env;
    if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) != JNI_OK) {
        LOGE("JNIEnv could not be retrieved for setup.");
        return JNI_ERR;
    }

    jclass stCaptureSessionWrapperCls = env->FindClass("com/uralstech/ucamera/STCaptureSessionWrapper");
    if (HasJNIException(env) || stCaptureSessionWrapperCls == nullptr) {
        LOGE("Could not find STCaptureSessionWrapper class.");
        return JNI_ERR;
    }

    g_startCaptureSessionMtd = env->GetMethodID(stCaptureSessionWrapperCls, "startCaptureSession","(I)Z");
    if (HasJNIException(env) || g_startCaptureSessionMtd == nullptr) {
        LOGE("Could not find startCaptureSession method.");
        env->DeleteLocalRef(stCaptureSessionWrapperCls);
        return JNI_ERR;
    }

    env->DeleteLocalRef(stCaptureSessionWrapperCls);
    LOGI("STCaptureSessionHelper initialized");
    return JNI_VERSION_1_6;
}

JNIEXPORT void JNI_OnUnload(JavaVM* vm, void*) {
    g_startCaptureSessionMtd = nullptr;
    g_javaVm = nullptr;

    JNIEnv* env;
    if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) != JNI_OK) {
        LOGE("JNIEnv could not be retrieved for deinitialization.");
        return;
    }

    scoped_lock lock(g_registeredSessionsMtx, g_surfaceTexturesMtx, g_renderersMtx);
    for (auto const& [key, val] : g_registeredSessions) {
        env->DeleteGlobalRef(val);
    }

    g_registeredSessions.clear();
    LOGI("Registered sessions disposed.");

    for (auto const& [key, val] : g_surfaceTextures) {
        LOGW("Disposing surface textures on JNI unload.");
        ASurfaceTexture_release(val.native);
        env->DeleteGlobalRef(val.java);
    }

    g_surfaceTextures.clear();

    for (auto const& [key, val] : g_renderers) {
        LOGW("Disposing renderers on JNI unload.");
        delete val;
    }

    g_renderers.clear();
}

extern "C" JNIEXPORT jboolean JNICALL
    Java_com_uralstech_ucamera_STCaptureSessionWrapper_registerCaptureSessionNative(JNIEnv *env, jobject current, jlong timestamp) {
    LOGI("Registering capture session.");

    lock_guard<mutex> lock(g_registeredSessionsMtx);
    if (g_registeredSessions.find(timestamp) != g_registeredSessions.end()) {
        LOGE("Tried to register capture session twice!");
        return false;
    }

    jobject globalRef = env->NewGlobalRef(current);
    if (globalRef == nullptr) {
        LOGE("Could not register capture session as global reference could not be created.");
        return false;
    }

    g_registeredSessions[timestamp] = globalRef;
    return true;
}

extern "C" JNIEXPORT void JNICALL
    Java_com_uralstech_ucamera_STCaptureSessionWrapper_tryDeregisterCaptureSessionNative(JNIEnv *env, jobject, jlong timestamp) {
    LOGI("Trying to deregister capture session.");

    lock_guard<mutex> lock(g_registeredSessionsMtx);
    if (g_registeredSessions.find(timestamp) != g_registeredSessions.end()) {
        env->DeleteGlobalRef(g_registeredSessions[timestamp]);
        g_registeredSessions.erase(timestamp);
    }
}

extern "C" JNIEXPORT jboolean JNICALL
    Java_com_uralstech_ucamera_STCaptureSessionWrapper_registerSurfaceTextureForUpdates(JNIEnv *env, jobject, jobject texture, jint textureId) {
    LOGI("Registering surface texture.");

    lock_guard<mutex> lock(g_surfaceTexturesMtx);
    if (g_surfaceTextures.find(textureId) != g_surfaceTextures.end()) {
        LOGE("Tried to register capture session twice!");
        return false;
    }

    jobject globalRef = env->NewGlobalRef(texture);
    if (globalRef == nullptr) {
        LOGE("Could not create global reference for surface texture.");
        return false;
    }

    g_surfaceTextures[textureId] = { globalRef, ASurfaceTexture_fromSurfaceTexture(env, texture) };
    return true;
}
extern "C" JNIEXPORT void JNICALL
    Java_com_uralstech_ucamera_STCaptureSessionWrapper_deregisterSurfaceTextureForUpdates(JNIEnv *env, jobject, jint textureId) {
    LOGI("Deregistering surface texture.");

    lock_guard<mutex> lock(g_surfaceTexturesMtx);
    if (g_surfaceTextures.find(textureId) != g_surfaceTextures.end()) {
        SurfaceTexture surfaceTexture = g_surfaceTextures[textureId];
        ASurfaceTexture_release(surfaceTexture.native);
        env->DeleteGlobalRef(surfaceTexture.java);

        g_surfaceTextures.erase(textureId);
    }
}

struct NativeSetupData
{
    GLuint unityTexture;
    GLint width; GLint height;

    int64_t timestamp;
    void (*onDoneCallback)(uint8_t glIsClean, uint8_t sessionCallSent, GLuint unityTexture, GLuint nativeTexture, uint8_t idIsValid);
};

void setupNativeTextures(void* data) {
    if (data == nullptr) {
        LOGE("Required data was not passed to setupNativeTextures.");
        return;
    }

    auto setupData = reinterpret_cast<NativeSetupData*>(data);
    GLuint unityTexture = setupData->unityTexture;

    scoped_lock lock(g_registeredSessionsMtx, g_renderersMtx);
    if (g_registeredSessions.find(setupData->timestamp) == g_registeredSessions.end()) {
        LOGE("No registered session found for timestamp.");
        setupData->onDoneCallback(true, false, unityTexture, 0, false);
        return;
    }

    auto renderer = new Renderer(unityTexture, setupData->width, setupData->height);

    GLuint newTexture;
    if (!renderer->initialize(&newTexture)) {
        LOGE("Could not initialize renderer");
        delete renderer;

        setupData->onDoneCallback(true, false, unityTexture, 0, false);
        return;
    }

    if (g_renderers.find(newTexture) != g_renderers.end()) {
        LOGE("Tried to register renderer twice!");
        renderer->dispose();
        delete renderer;

        setupData->onDoneCallback(true, false, unityTexture, 0, false);
        return;
    }

    g_renderers[newTexture] = renderer;
    jobject registeredSession = g_registeredSessions[setupData->timestamp];

    bool shouldDetachJNI;
    JNIEnv* env = AttachEnv(g_javaVm, &shouldDetachJNI);
    if (env == nullptr) {
        LOGE("A reference to the JNI could not be retrieved.");
        g_renderers.erase(newTexture);
        renderer->dispose();
        delete renderer;

        setupData->onDoneCallback(true, false, unityTexture, 0, false);
        return;
    }

    jboolean result = env->CallBooleanMethod(registeredSession, g_startCaptureSessionMtd, (jint)newTexture);
    g_registeredSessions.erase(setupData->timestamp);
    env->DeleteGlobalRef(registeredSession);

    if (HasJNIException(env) || !result) {
        if (shouldDetachJNI) {
            DetachJNIEnv(g_javaVm);
        }

        LOGE("A JNI/script exception occurred.");
        setupData->onDoneCallback(false, false, unityTexture, newTexture, true);
        return;
    }

    if (shouldDetachJNI) {
        DetachJNIEnv(g_javaVm);
    }

    LOGI("Renderer is ready for capture session.");
    setupData->onDoneCallback(true, true, unityTexture, newTexture, true);
}

struct NativeUpdateData {
    GLuint nativeTexture;
    void (*onDoneCallback)(GLuint textureId, uint8_t success);
};

void renderNativeTextures(void* data) {
    if (data == nullptr) {
        LOGE("Required data was not passed to renderNativeTextures.");
        return;
    }

    auto renderData = reinterpret_cast<NativeUpdateData*>(data);
    GLuint texture = renderData->nativeTexture;

    scoped_lock lock(g_surfaceTexturesMtx, g_renderersMtx);
    if (g_surfaceTextures.find(texture) == g_surfaceTextures.end()
        || g_renderers.find(texture) == g_renderers.end()) {
        LOGE("Cannot render as registered SurfaceTexture or Renderer was not found.");
        renderData->onDoneCallback(texture, false);
        return;
    }

    Renderer* renderer = g_renderers[texture];
    ASurfaceTexture* surfaceTexture = g_surfaceTextures[texture].native;
    if (renderer == nullptr || surfaceTexture == nullptr) {
        LOGE("Tried to render with nonexistent renderer or SurfaceTexture.");
        renderData->onDoneCallback(texture, false);
        return;
    }

    uint8_t result = renderer->render(surfaceTexture);
    renderData->onDoneCallback(texture, result);
}

void cleanupNativeData(void* data) {
    if (data == nullptr) {
        LOGE("Required data was not passed to cleanupNativeData.");
        return;
    }

    auto renderData = reinterpret_cast<NativeUpdateData*>(data);
    GLuint texture = renderData->nativeTexture;

    lock_guard<mutex> lock(g_renderersMtx);
    if (g_renderers.find(texture) == g_renderers.end()) {
        LOGE("Tried to cleanup unregistered renderer.");
        renderData->onDoneCallback(texture, false);
        return;
    }

    Renderer* renderer = g_renderers[texture];
    g_renderers.erase(texture);
    renderer->dispose();
    delete renderer;

    LOGI("Renderer cleaned up.");
    renderData->onDoneCallback(texture, true);
}

#define SETUP_NATIVE_TEXTURE_EVENT      1
#define CLEANUP_NATIVE_TEXTURE_EVENT    2
#define RENDER_TEXTURES_EVENT           3

static void UNITY_INTERFACE_API OnRenderEvent(int eventId, void* data) {
    switch (eventId) {
        case SETUP_NATIVE_TEXTURE_EVENT:
            setupNativeTextures(data); break;

        case RENDER_TEXTURES_EVENT:
            renderNativeTextures(data); break;

        case CLEANUP_NATIVE_TEXTURE_EVENT:
            cleanupNativeData(data); break;

        default:
            LOGE("Encountered unrecognized render event with ID: %i", eventId);
            break;
    }
}

extern "C" UnityRenderingEventAndData UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
    GetRenderEventFunction() {
    return OnRenderEvent;
}