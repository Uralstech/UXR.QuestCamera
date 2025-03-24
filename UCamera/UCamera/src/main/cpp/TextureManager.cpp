#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include <android/log.h>
#include <GLES3/gl3.h>
#include <mutex>
#include <map>
#include "jni.h"

#define LOG_TAG "UCameraNativeGraphics"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,    LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR,   LOG_TAG, __VA_ARGS__)

#define CREATE_GL_TEXTURE_EVENT 1
#define DESTROY_GL_TEXTURE_EVENT 2

static JavaVM* g_javaVm = nullptr;
static jmethodID g_startCaptureSessionMethodId = nullptr;

static std::map<jlong, jobject> g_uninitializedSurfaceTextureMap;
static std::mutex g_mapMutex;

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
        LOGE("Could not properly dispose g_uninitializedSurfaceTextureMap as JNIEnv could not be retrieved.");
        return;
    }

    std::lock_guard<std::mutex> lock(g_mapMutex);
    for (auto const& [key, val] : g_uninitializedSurfaceTextureMap) {
        env->DeleteGlobalRef(val);
    }

    g_uninitializedSurfaceTextureMap.clear();
    LOGI("Successfully disposed g_uninitializedSurfaceTextureMap.");
}

JNIEnv* AttachEnv() {
    JavaVM* javaVm = g_javaVm;
    if (javaVm == nullptr) {
        LOGE("Failed to get JNIEnv as javaVM is a nullptr!");
        return nullptr;
    }

    JNIEnv* env;
    jint result = javaVm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6);

    if (result == JNI_EDETACHED) {
        LOGI("Attaching to JNI thread.");

        result = javaVm->AttachCurrentThread(&env, nullptr);
        if (result != JNI_OK) {
            LOGE("Failed to attach to JNI thread, result: %i", result);
            return nullptr;
        }
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
    std::lock_guard<std::mutex> lock(g_mapMutex);

    jobject globalRef = env->NewGlobalRef(current);
    if (globalRef == nullptr) {
        LOGE("Could not create global reference for STCaptureSession.");
        return;
    }

    auto it = g_uninitializedSurfaceTextureMap.find(timeStamp);
    if (it != g_uninitializedSurfaceTextureMap.end()) {
        env->DeleteGlobalRef(it->second);
        it->second = globalRef;

        LOGI("Replaced existing STCaptureSession in map with new GlobalRef (timeStamp: %li).", timeStamp);
    } else {
        g_uninitializedSurfaceTextureMap[timeStamp] = globalRef;
        LOGI("Added new STCaptureSession GlobalRef to map, with timeStamp: %li.", timeStamp);
    }
}

void SendTextureIdToCaptureSession(GLuint textureId, jlong timeStamp)
{
    LOGI("Sending initialization signal to STCaptureSession associated with timeStamp: %li", timeStamp);
    if (g_startCaptureSessionMethodId == nullptr) {
        LOGE("Could not initialize STCaptureSession due to missing methodId.");
        return;
    }

    std::lock_guard<std::mutex> lock(g_mapMutex);

    auto it = g_uninitializedSurfaceTextureMap.find(timeStamp);
    if (it == g_uninitializedSurfaceTextureMap.end()) {
        LOGE("Could not find any uninitialized STCaptureSessions for the given timeStamp: %li", timeStamp);
        return;
    }

    JNIEnv* jniEnv = AttachEnv();
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
        g_uninitializedSurfaceTextureMap.erase(it);
    }

    DetachJNIEnv();
}

static void UNITY_INTERFACE_API OnRenderEvent(int eventId, void* data) {
    switch (eventId) {
        case CREATE_GL_TEXTURE_EVENT:
            LOGI("Creating new OpenGL texture.");

            if (data == nullptr) {
                LOGE("Could not create OpenGL texture as data is a nullptr.");
                break;
            }

            GLuint textureIds[1];
            glGenTextures(1, textureIds);
            LOGI("Created new OpenGL texture with ID: %u", textureIds[0]);

            SendTextureIdToCaptureSession(textureIds[0], *reinterpret_cast<jlong*>(data));
            break;

        case DESTROY_GL_TEXTURE_EVENT:
            LOGI("Destroying OpenGL texture.");

            GLuint* textureToDelete;
            textureToDelete = reinterpret_cast<GLuint *>(data);

            if (data == nullptr || (*textureToDelete) == 0) {
                LOGE("Could not destroy OpenGL texture as textureToDelete is a nullptr or is zero.");
                break;
            }

            glDeleteTextures(1, textureToDelete);
            LOGI("OpenGL texture with ID \"%u\" deleted", *textureToDelete);
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