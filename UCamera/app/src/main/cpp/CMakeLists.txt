# For more information about using CMake with Android Studio, read the
# documentation: https://d.android.com/studio/projects/add-native-code.html.
# For more examples on how to use CMake, see https://github.com/android/ndk-samples.

# Sets the minimum CMake version required for this project.
cmake_minimum_required(VERSION 3.22.1)

# Declares the project name. The project name can be accessed via ${ PROJECT_NAME},
# Since this is the top level CMakeLists.txt, the project name is also accessible
# with ${CMAKE_PROJECT_NAME} (both CMake variables are in-sync within the top level
# build script scope).
project("UXRQC_Native")

set(GLES_PLUGIN_NAME UXRQC_GLESPlugin)

# Creates and names a library, sets it as either STATIC
# or SHARED, and provides the relative paths to its source code.
# You can define multiple libraries, and CMake builds them for you.
# Gradle automatically packages shared libraries with your APK.
#
# In order to load a library into your app from Java/Kotlin, you must call
# System.loadLibrary() and pass the name of the library defined here;
# for GameActivity/NativeActivity derived applications, the same library name must be
# used in the AndroidManifest.xml file.
add_library(${GLES_PLUGIN_NAME} SHARED
    # List C/C++ source files with relative paths to this CMakeLists.txt.
    GLES_YUVConverter.h
    GLES_YUVConverter.cpp
    GLESTextureConversionManager.cpp)

target_include_directories(
        ${GLES_PLUGIN_NAME} PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../../../../UnityInterface
)

# Specifies libraries CMake should link to your target library. You
# can link libraries from various origins, such as libraries defined in this
# build script, prebuilt third-party libraries, or Android system libraries.
target_link_libraries(${GLES_PLUGIN_NAME}
    # List libraries link to the target library
    android
    GLESv3
    log)

set(VKGLUE_PLUGIN_NAME UXRQC_VulkanGluePlugin)

add_library(${VKGLUE_PLUGIN_NAME} SHARED
        VulkanGlue.cpp)

target_include_directories(
        ${VKGLUE_PLUGIN_NAME} PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../../../../UnityInterface
)

target_link_libraries(${VKGLUE_PLUGIN_NAME}
    android
    log)
