cmake_minimum_required(VERSION 3.4.1)

add_library(
        NativeTextureHelper
        SHARED
        TextureManager.cpp
        ShaderManager.h
        ShaderManager.cpp
)

target_include_directories(
        NativeTextureHelper
        PRIVATE
        UnityInterfaces
)

target_link_libraries(NativeTextureHelper android GLESv3 log)