//
// Created by celeste on 03/10/25.
//

#ifndef UCAMERA_JNIEXTENSIONS_H
#define UCAMERA_JNIEXTENSIONS_H

#include <jni.h>

bool HasJNIException(JNIEnv* env);

JNIEnv* AttachEnv(JavaVM* javaVm, bool* shouldDetach);
void DetachJNIEnv(JavaVM* javaVm);

#endif //UCAMERA_JNIEXTENSIONS_H
