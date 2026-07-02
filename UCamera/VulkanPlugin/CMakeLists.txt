# For more information about using CMake with Android Studio, read the
# documentation: https://d.android.com/studio/projects/add-native-code.html.
# For more examples on how to use CMake, see https://github.com/android/ndk-samples.

# Sets the minimum CMake version required for this project.
cmake_minimum_required(VERSION 3.22.1)

# Declares the project name. The project name can be accessed via ${ PROJECT_NAME},
# Since this is the top level CMakeLists.txt, the project name is also accessible
# with ${CMAKE_PROJECT_NAME} (both CMake variables are in-sync within the top level
# build script scope).
project("GfxPlugin_UXRQC_VulkanPlugin")

# Compile shaders
find_program(GLSLC
    NAMES glslc
    PATHS
        "$ENV{ANDROID_NDK}/shader-tools"
        "${ANDROID_NDK}/shader-tools"
    PATH_SUFFIXES
        darwin-x86_64
        windows-x86_64
    REQUIRED
)

set(SHADERS
        Shaders/Render.vert
        Shaders/Render.frag
)

set(GENERATED_SOURCES)
foreach(SHADER ${SHADERS})

    get_filename_component(FILE_NAME ${SHADER} NAME)
    string(REPLACE "." "_" SYMBOL ${FILE_NAME})
    string(TOLOWER ${SYMBOL} SYMBOL)

    set(TXT ${CMAKE_CURRENT_SOURCE_DIR}/CompiledShaders/${SYMBOL}.txt)
    set(CPP ${CMAKE_CURRENT_SOURCE_DIR}/CompiledShaders/${SYMBOL}.cpp)
    set(HPP ${CMAKE_CURRENT_SOURCE_DIR}/CompiledShaders/${SYMBOL}.h)

    add_custom_command(
        OUTPUT ${TXT}
        COMMAND ${GLSLC}
            --target-env=vulkan1.1
            -mfmt=c
            -o ${TXT}
            ${CMAKE_CURRENT_SOURCE_DIR}/${SHADER}
        DEPENDS ${SHADER}
    )

    file(GENERATE
        OUTPUT ${HPP}
        CONTENT
            "#pragma once

#include <cstddef>
#include <cstdint>

extern const uint32_t ${SYMBOL}[];
extern const size_t ${SYMBOL}_size;
")

    file(GENERATE
        OUTPUT ${CPP}
        CONTENT
            "#include \"${SYMBOL}.h\"

const uint32_t ${SYMBOL}[] =
#include \"${SYMBOL}.txt\"
;

const size_t ${SYMBOL}_size = sizeof(${SYMBOL});
")

    set_source_files_properties(${CPP}
            PROPERTIES
            GENERATED TRUE
            OBJECT_DEPENDS ${TXT}
    )

    set_source_files_properties(${HPP}
            PROPERTIES
            GENERATED TRUE
    )

    list(APPEND GENERATED_SOURCES ${CPP})
endforeach()

# Creates and names a library, sets it as either STATIC
# or SHARED, and provides the relative paths to its source code.
# You can define multiple libraries, and CMake builds them for you.
# Gradle automatically packages shared libraries with your APK.
#
# In this top level CMakeLists.txt, ${CMAKE_PROJECT_NAME} is used to define
# the target library name; in the sub-module's CMakeLists.txt, ${PROJECT_NAME}
# is preferred for the same purpose.
#
# In order to load a library into your app from Java/Kotlin, you must call
# System.loadLibrary() and pass the name of the library defined here;
# for GameActivity/NativeActivity derived applications, the same library name must be
# used in the AndroidManifest.xml file.
add_library(${CMAKE_PROJECT_NAME} SHARED
    # List C/C++ source files with relative paths to this CMakeLists.txt.
    VkRenderer/VkRenderer.h
    VkRenderer/VkRenderer.cpp
    VkRenderer/VkRendererStatic.cpp

    VkUnityInterface.cpp
    ${GENERATED_SOURCES})

target_include_directories(
        ${CMAKE_PROJECT_NAME} PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../UnityInterface
)

target_compile_definitions(${CMAKE_PROJECT_NAME} PRIVATE VK_NO_PROTOTYPES)

# Specifies libraries CMake should link to your target library. You
# can link libraries from various origins, such as libraries defined in this
# build script, prebuilt third-party libraries, or Android system libraries.
target_link_libraries(${CMAKE_PROJECT_NAME}
    # List libraries link to the target library
    android
    vulkan
    log)

