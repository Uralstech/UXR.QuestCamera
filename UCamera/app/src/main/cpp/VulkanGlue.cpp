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
#include <android/hardware_buffer_jni.h>
#include <dlfcn.h>
#include "IUnityInterface.h"

#define TAG "UXRQC.VkGlue"
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

using GetIdFn = int(*)(AHardwareBuffer*, uint64_t*);

static GetIdFn getIdFn = []() -> GetIdFn {
    return reinterpret_cast<GetIdFn>(
            dlsym(RTLD_DEFAULT, "AHardwareBuffer_getId"));
}();

extern "C"
JNIEXPORT jlong JNICALL
Java_com_uralstech_uxr_questcamera_sessions_vulkan_VkContinuousCaptureSessionManager_acquireHardwareBuffer(
        JNIEnv *env, jobject, jobject buffer) {

    AHardwareBuffer* nativeBuffer = AHardwareBuffer_fromHardwareBuffer(env, buffer);
    if (nativeBuffer == nullptr) {
        LOGE("Could not create native hardware buffer from Kotlin.");
        return 0;
    }

    AHardwareBuffer_acquire(nativeBuffer);
    return reinterpret_cast<jlong>(nativeBuffer);
}

extern "C"
JNIEXPORT jlong JNICALL
Java_com_uralstech_uxr_questcamera_sessions_vulkan_VkContinuousCaptureSessionManager_getHardwareBufferId(
        JNIEnv*, jobject, jlong acquiredBufferPtr) {

    auto hardwareBuffer = reinterpret_cast<AHardwareBuffer*>(acquiredBufferPtr);
    if (hardwareBuffer == nullptr) {
        LOGE("Cannot get ID of nullptr native hardware buffer.");
        return 0;
    }

    if (!getIdFn) {
        LOGE("Cannot get ID of native hardware buffer on Android <31.");
        return 0;
    }

    uint64_t bufferId;
    auto result = getIdFn(hardwareBuffer, &bufferId);
    if (result != 0) {
        LOGE("Could not get ID of native hardware buffer due to error, code: %d", result);
        return 0;
    }

    return static_cast<jlong>(bufferId);
}

extern "C"
JNIEXPORT void JNICALL
Java_com_uralstech_uxr_questcamera_sessions_vulkan_VkContinuousCaptureSessionManager_releaseHardwareBuffer(
        JNIEnv*, jobject, jlong acquiredBufferPtr) {

    auto hardwareBuffer = reinterpret_cast<AHardwareBuffer*>(acquiredBufferPtr);
    if (hardwareBuffer == nullptr) {
        LOGE("Cannot release nullptr native hardware buffer.");
        return;
    }

    AHardwareBuffer_release(hardwareBuffer);
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
    releaseHardwareBuffer(AHardwareBuffer* acquiredBufferPtr) {

    if (acquiredBufferPtr != nullptr) {
        AHardwareBuffer_release(acquiredBufferPtr);
    }
}