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
#include "JNIExtensions.h"

#define LOG_TAG "UCameraJNIExt"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,     LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR,    LOG_TAG, __VA_ARGS__)

bool HasJNIException(JNIEnv* env) {
    if (env->ExceptionCheck()) {
        env->ExceptionDescribe();
        env->ExceptionClear();
        return true;
    }

    return false;
}

JNIEnv* AttachEnv(JavaVM* javaVm, bool* shouldDetach) {
    if (javaVm == nullptr) {
        LOGE("javaVM is a nullptr, can't get JNIEnv.");
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


void DetachJNIEnv(JavaVM* javaVm) {
    if (javaVm == nullptr) {
        LOGE("javaVM is a nullptr, can't detach JNI thread.");
        return;
    }

    jint result = javaVm->DetachCurrentThread();
    if (result != JNI_OK) {
        LOGE("Failed to detach from JNI thread, result: %i", result);
    }
}