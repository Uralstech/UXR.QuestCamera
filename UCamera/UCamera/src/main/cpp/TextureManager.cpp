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

#include "ShaderManager.h"
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include <android/log.h>
#include <android/surface_texture.h>
#include <android/surface_texture_jni.h>
#include <GLES3/gl32.h>
#include <GLES2/gl2ext.h>
#include <mutex>
#include <map>
#include "jni.h"

#define LOG_TAG "UCameraNativeGraphics"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,     LOG_TAG, __VA_ARGS__)
#define LOGW(...) __android_log_print(ANDROID_LOG_WARN,     LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR,    LOG_TAG, __VA_ARGS__)

#define CREATE_GL_TEXTURE_EVENT         1
#define DESTROY_GL_TEXTURE_EVENT        2
#define UPDATE_SURFACE_TEXTURE_EVENT    3

static JavaVM* g_javaVm = nullptr;
static jmethodID g_startCaptureSessionMethodId = nullptr;

static std::map<jlong, jobject> g_uninitializedSTCaptureSessionMap;
static std::mutex g_uninitializedSTCaptureSessionMapMutex;

struct NativeAndJavaSurfaceTexture
{
    ASurfaceTexture* nativeSurfaceTexture;
    jobject jniSurfaceTexture;

    GLuint cameraFrameBuffer;
    GLuint unityFrameBuffer;
};

static std::map<jint, NativeAndJavaSurfaceTexture> g_registeredSurfaceTextureMap;
static std::mutex g_registeredSurfaceTextureMapMutex;

bool CheckAndLogJNIException(JNIEnv* env);

JNIEXPORT jint JNI_OnLoad(JavaVM* vm, void* /* reserved */) {
    LOGI("JNI_OnLoad called.");
    g_javaVm = vm;

    JNIEnv* env;
    if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) != JNI_OK) {
        LOGE("Could not assign g_startCaptureSessionMethodId as JNIEnv could not be retrieved.");
        return JNI_ERR;
    }

    jclass surfaceTextureCaptureSession = env->FindClass("com/uralstech/ucamera/SurfaceTextureCaptureSession");
    if (CheckAndLogJNIException(env) || surfaceTextureCaptureSession == nullptr) {
        LOGE("Could not assign g_startCaptureSessionMethodId due to error while finding its class.");
        return JNI_ERR;
    }

    g_startCaptureSessionMethodId = env->GetMethodID(surfaceTextureCaptureSession, "startCaptureSession", "(I)V");
    if (CheckAndLogJNIException(env) || g_startCaptureSessionMethodId == nullptr) {
        LOGE("Could not assign g_startCaptureSessionMethodId due to error while finding its methodId.");
        env->DeleteLocalRef(surfaceTextureCaptureSession);
        return JNI_ERR;
    }

    env->DeleteLocalRef(surfaceTextureCaptureSession);
    LOGI("Successfully initialized g_startCaptureSessionMethodId.");

    return JNI_VERSION_1_6;
}

JNIEXPORT void JNI_OnUnload(JavaVM* vm, void* /* reserved */) {
    LOGI("JNI_OnUnload called.");

    g_startCaptureSessionMethodId = nullptr;
    g_javaVm = nullptr;

    JNIEnv* env;
    if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) != JNI_OK) {
        LOGE("Could not properly dispose g_uninitializedSTCaptureSessionMap as JNIEnv could not be retrieved.");
        return;
    }

    std::lock_guard<std::mutex> lock1(g_uninitializedSTCaptureSessionMapMutex);
    for (auto const& [key, val] : g_uninitializedSTCaptureSessionMap) {
        env->DeleteGlobalRef(val);
    }

    g_uninitializedSTCaptureSessionMap.clear();
    LOGI("Successfully disposed g_uninitializedSTCaptureSessionMap.");

    std::lock_guard<std::mutex> lock2(g_registeredSurfaceTextureMapMutex);
    for (auto const& [key, val] : g_registeredSurfaceTextureMap) {
        ASurfaceTexture_release(val.nativeSurfaceTexture);
        env->DeleteGlobalRef(val.jniSurfaceTexture);

        LOGW("Could not dispose FrameBuffers with IDs \"%i\" and \"%i\" due to being on the JNI thread.", val.cameraFrameBuffer, val.unityFrameBuffer);
    }

    g_registeredSurfaceTextureMap.clear();
    LOGI("Successfully disposed g_registeredSurfaceTextureMap.");
}

JNIEnv* AttachEnv(bool* shouldDetach) {
    JavaVM* javaVm = g_javaVm;
    if (javaVm == nullptr) {
        LOGE("Failed to get JNIEnv as javaVM is a nullptr!");
        return nullptr;
    }

    *shouldDetach = false;

    JNIEnv* env;
    jint result = javaVm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6);

    if (result == JNI_EDETACHED) {
        LOGI("Attaching to JNI thread.");

        result = javaVm->AttachCurrentThread(&env, nullptr);
        if (result != JNI_OK) {
            LOGE("Failed to attach to JNI thread, result: %i", result);
            return nullptr;
        }

        *shouldDetach = true;
    } else if (result != JNI_OK) {
        LOGE("Failed to get JNIEnv, result: %i", result);
        return nullptr;
    }

    LOGI("Got JNIEnv.");
    return env;
}

void DetachJNIEnv() {
    JavaVM* javaVm = g_javaVm;
    if (javaVm == nullptr) {
        LOGE("Failed to detach from JNI thread as javaVM is a nullptr!");
        return;
    }

    jint result = javaVm->DetachCurrentThread();
    if (result != JNI_OK) {
        LOGE("Failed to detach from JNI thread, maybe it was never attached? Result: %i", result);
    }
}

bool CheckAndLogJNIException(JNIEnv* env) {
    if (env->ExceptionCheck()) {
        env->ExceptionDescribe();
        env->ExceptionClear();

        return true;
    }

    return false;
}

extern "C" JNIEXPORT void JNICALL
    Java_com_uralstech_ucamera_SurfaceTextureCaptureSession_queueSurfaceTextureCaptureSession(JNIEnv* env, jobject current, jlong timeStamp) {
    std::lock_guard<std::mutex> lock(g_uninitializedSTCaptureSessionMapMutex);

    jobject globalRef = env->NewGlobalRef(current);
    if (globalRef == nullptr) {
        LOGE("Could not create global reference for STCaptureSession.");
        return;
    }

    auto it = g_uninitializedSTCaptureSessionMap.find(timeStamp);
    if (it != g_uninitializedSTCaptureSessionMap.end()) {
        env->DeleteGlobalRef(it->second);
        it->second = globalRef;

        LOGI("Replaced existing STCaptureSession in map with new GlobalRef (timeStamp: %li).", timeStamp);
    } else {
        g_uninitializedSTCaptureSessionMap[timeStamp] = globalRef;
        LOGI("Added new STCaptureSession GlobalRef to map, with timeStamp: %li.", timeStamp);
    }
}

extern "C" JNIEXPORT void JNICALL
    Java_com_uralstech_ucamera_SurfaceTextureCaptureSession_registerSurfaceTextureForUpdates(JNIEnv* env, jobject /* current */, jobject surfaceTexture, jint textureId) {
    LOGI("Registering SurfaceTexture for updates.");

    std::lock_guard<std::mutex> lock(g_registeredSurfaceTextureMapMutex);

    jobject globalRef = env->NewGlobalRef(surfaceTexture);
    if (globalRef == nullptr) {
        LOGE("Could not create global reference for SurfaceTexture.");
        return;
    }

    auto it = g_registeredSurfaceTextureMap.find(textureId);
    if (it != g_registeredSurfaceTextureMap.end()) {
        ASurfaceTexture_release(it->second.nativeSurfaceTexture);
        env->DeleteGlobalRef(it->second.jniSurfaceTexture);

        it->second.jniSurfaceTexture = globalRef;
        it->second.nativeSurfaceTexture = ASurfaceTexture_fromSurfaceTexture(env, globalRef);

        LOGI("Replaced existing SurfaceTexture in map with new GlobalRef (textureId: %i).", textureId);
    } else {
        NativeAndJavaSurfaceTexture data = { ASurfaceTexture_fromSurfaceTexture(env, globalRef), globalRef };
        g_registeredSurfaceTextureMap[textureId] = data;

        LOGI("Added new SurfaceTexture GlobalRef to map, with textureId: %i.", textureId);
    }
}

extern "C" JNIEXPORT void JNICALL
    Java_com_uralstech_ucamera_SurfaceTextureCaptureSession_deregisterSurfaceTextureForUpdates(JNIEnv* env, jobject /* current */, jint textureId) {
    LOGI("Unregistering SurfaceTexture from updates.");

    std::lock_guard<std::mutex> lock(g_registeredSurfaceTextureMapMutex);

    auto it = g_registeredSurfaceTextureMap.find(textureId);
    if (it == g_registeredSurfaceTextureMap.end()) {
        LOGE("Can't deregister a SurfaceTexture that was never registered in the first place!");
        return;
    }

    ASurfaceTexture_release(it->second.nativeSurfaceTexture);
    env->DeleteGlobalRef(it->second.jniSurfaceTexture);

    it->second.nativeSurfaceTexture = nullptr;
    it->second.jniSurfaceTexture = nullptr;

    LOGI("Deregistered SurfaceTexture successfully.");
}

struct TextureSetupData {
    GLuint unityTextureId;

    jlong timeStamp;
    void (*onDoneCallback)(GLuint);
};

struct TextureUpdateData {
    GLuint unityTextureId;
    jint cameraTextureId;
    GLint width;
    GLint height;

    void (*onDoneCallback)(GLuint);
};

struct TextureDeletionData {
    GLuint textureId;
    void (*onDoneCallback)(GLuint);
};

static ShaderManager::RenderInfo g_renderInfo;

void updateSurfaceTextureNative(TextureUpdateData data) {
    LOGI("Updating SurfaceTexture from native code. (camTex: %i, unityTex: %i)", data.cameraTextureId, data.unityTextureId);
    std::lock_guard<std::mutex> lock(g_registeredSurfaceTextureMapMutex);

    auto it = g_registeredSurfaceTextureMap.find(data.cameraTextureId);
    if (it == g_registeredSurfaceTextureMap.end()) {
        LOGE("Could not find any registered SurfaceTextures for textureId: %i", data.cameraTextureId);
        return;
    }

    ASurfaceTexture_updateTexImage(it->second.nativeSurfaceTexture);
    LOGI("Successfully updated SurfaceTexture from native code.");

    ShaderManager::renderFrame(&g_renderInfo, data.cameraTextureId, data.unityTextureId, data.width, data.height);
    data.onDoneCallback(data.unityTextureId);
}

void deleteTextureNative(TextureDeletionData data) {
    ShaderManager::cleanupGraphics(&g_renderInfo);

    glDeleteTextures(1, &data.textureId);
    LOGI("Rendering data released.");

    std::lock_guard<std::mutex> lock(g_registeredSurfaceTextureMapMutex);

    auto it = g_registeredSurfaceTextureMap.find((jint)data.textureId);
    if (it != g_registeredSurfaceTextureMap.end()) {
        if (it->second.cameraFrameBuffer > 0) {
            glDeleteFramebuffers(1, &it->second.cameraFrameBuffer);
            it->second.cameraFrameBuffer = 0;
            LOGI("cameraFrameBuffer deleted.");
        }

        if (it->second.unityFrameBuffer > 0) {
            glDeleteFramebuffers(1, &it->second.unityFrameBuffer);
            it->second.unityFrameBuffer = 0;
            LOGI("unityFrameBuffer deleted.");
        }

        if (it->second.nativeSurfaceTexture != nullptr) {
            ASurfaceTexture_release(it->second.nativeSurfaceTexture);
            it->second.nativeSurfaceTexture = nullptr;
            LOGI("ASurfaceTexture* deleted.");
        }

        if (it->second.jniSurfaceTexture != nullptr) {

            bool shouldDetach;
            JNIEnv* env = AttachEnv(&shouldDetach);
            if (env == nullptr) {
                LOGE("Could not delete global reference to SurfaceTexture due to not being able to attach to a JNIEnv.");
                return;
            }

            env->DeleteGlobalRef(it->second.jniSurfaceTexture);
            it->second.jniSurfaceTexture = nullptr;
            LOGI("SurfaceTexture Global Reference deleted.");

            if (shouldDetach) {
                DetachJNIEnv();
            }
        }

        g_registeredSurfaceTextureMap.erase(it);
    }

    data.onDoneCallback(data.textureId);
}

void setupTextureNative(TextureSetupData data) {
    if (g_startCaptureSessionMethodId == nullptr) {
        LOGE("Could not initialize STCaptureSession due to missing methodId.");
        return;
    }

    GLuint textureId;
    glGenTextures(1, &textureId);
    LOGI("Created new OpenGL texture with ID: %u, sending to STCaptureSession with timeStamp: %li", textureId, data.timeStamp);

    bool result = ShaderManager::setupGraphics(&g_renderInfo);
    if (!result) {
        LOGE("Could not setup rendering!");
        return;
    }

    std::lock_guard<std::mutex> lock(g_uninitializedSTCaptureSessionMapMutex);

    auto it = g_uninitializedSTCaptureSessionMap.find(data.timeStamp);
    if (it == g_uninitializedSTCaptureSessionMap.end()) {
        LOGE("Could not find any uninitialized STCaptureSessions for the given timeStamp: %li", data.timeStamp);
        return;
    }

    bool shouldDetach;
    JNIEnv* jniEnv = AttachEnv(&shouldDetach);
    if (jniEnv == nullptr) {
        LOGE("Could not initialize STCaptureSession due to JNIEnv being null.");
        return;
    }

    jniEnv->CallVoidMethod(it->second, g_startCaptureSessionMethodId, (jint)textureId);
    if (CheckAndLogJNIException(jniEnv)) {
        LOGE("Could not initialize STCaptureSession due to error.");
    } else {
        LOGI("Successfully called STCaptureSession initialization method.");
        jniEnv->DeleteGlobalRef(it->second);
        g_uninitializedSTCaptureSessionMap.erase(it);
    }

    if (shouldDetach) {
        DetachJNIEnv();
        LOGI("JNIEnv detached.");
    }

    data.onDoneCallback(data.unityTextureId);
}

static void UNITY_INTERFACE_API OnRenderEvent(int eventId, void* data) {
    if (data == nullptr) {
        LOGE("OnRenderEvent got nullptr as data.");
        return;
    }

    switch (eventId) {
        case CREATE_GL_TEXTURE_EVENT:
            LOGI("Creating new OpenGL texture.");

            TextureSetupData* setupData;
            setupData = reinterpret_cast<TextureSetupData*>(data);

            setupTextureNative(*setupData);
            break;

        case DESTROY_GL_TEXTURE_EVENT:
            LOGI("Destroying OpenGL texture.");

            TextureDeletionData* deletionData;
            deletionData = reinterpret_cast<TextureDeletionData*>(data);

            deleteTextureNative(*deletionData);
            break;

        case UPDATE_SURFACE_TEXTURE_EVENT:
            LOGI("Updating SurfaceTexture.");

            TextureUpdateData* updateData;
            updateData = reinterpret_cast<TextureUpdateData*>(data);

            updateSurfaceTextureNative(*updateData);
            break;

        default:
            LOGE("Unknown eventId for OnRenderEvent: %i", eventId);
            break;
    }
}

extern "C" UnityRenderingEventAndData UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
    GetRenderEventFunction() {
    return OnRenderEvent;
}