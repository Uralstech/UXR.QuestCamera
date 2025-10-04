cmake_minimum_required(VERSION 3.4.1)

add_library(
        NativeTextureHelper
        SHARED

        JNIExtensions.h
        JNIExtensions.cpp
        Renderer.h
        Renderer.cpp
        STCaptureSessionHelper.cpp
)

target_include_directories(
        NativeTextureHelper
        PRIVATE
        UnityInterfaces
)

target_link_libraries(NativeTextureHelper android GLESv3 log)