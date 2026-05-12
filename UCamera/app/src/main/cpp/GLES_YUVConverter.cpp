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

#include "GLES_YUVConverter.h"
#include <android/log.h>
#include <GLES2/gl2ext.h>
#include <malloc.h>

#define TAG "UXRQC.GLYUVConverter"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

//region Shader sources

const char* VERTEX_SHADER_SOURCE = R"glsl(
#version 300 es

// Input vertex data
layout(location = 0) in vec4 aPosition;
layout(location = 1) in vec2 aTexCoord;

// The matrix from SurfaceTexture
uniform mat4 uTransformMatrix;

// Pass the transformed texture coordinate to the fragment shader
out vec2 vTexCoord;

void main() {
    gl_Position = aPosition;
    vTexCoord = (uTransformMatrix * vec4(aTexCoord, 0.0, 1.0)).xy;
}
)glsl";

const char* FRAGMENT_SHADER_SOURCE = R"glsl(
#version 300 es
#extension GL_EXT_YUV_target : require
precision mediump float;

in vec2 vTexCoord;

uniform __samplerExternal2DY2YEXT sYUVTexture;
out vec4 outColor;

void main() {
    vec3 yuv = texture(sYUVTexture, vTexCoord).xyz;
    vec3 rgb = yuv_2_rgb(yuv, itu_601_full_range);
    outColor = vec4(rgb, 1.0);
}
)glsl";

//endregion

//region Static members

uint8_t GLES_YUVConverter::s_staticReferenceHolders      = 0;

GLuint GLES_YUVConverter::s_shaderProgram                = 0;
GLint GLES_YUVConverter::s_shaderTransformMatrixHandle   = 0;
GLint GLES_YUVConverter::s_shaderTextureSamplerHandle    = 0;

GLuint GLES_YUVConverter::s_vertexBufferObj              = 0;
GLuint GLES_YUVConverter::s_vertexArrayObj               = 0;

static bool hasErrors(const char *methodName) {
    bool hasErrors = false;

    GLenum error;
    while ((error = glGetError()) != GL_NO_ERROR) {
        LOGE("Encountered GL error %u at %s", error, methodName);
        hasErrors = true;
    }

    return hasErrors;
}

static bool compileShader(GLenum type, const char *source, GLuint *shader) {

    *shader = glCreateShader(type);
    if (hasErrors("glCreateShader") || *shader == 0) {
        LOGE("Failed at creating shader of type %u", type);
        return false;
    }

    glShaderSource(*shader, 1, &source, nullptr);
    glCompileShader(*shader);

    GLint status;
    glGetShaderiv(*shader, GL_COMPILE_STATUS, &status);

    if (!status) {
        GLint logsLength;
        glGetShaderiv(*shader, GL_INFO_LOG_LENGTH, &logsLength);

        if (logsLength > 0) {
            char* logs = (char*)malloc(sizeof(char) * logsLength);
            glGetShaderInfoLog(*shader, logsLength, nullptr, logs);

            LOGE("Shader compilation failed (type: %u), logs:\n%s", type, logs);
            free(logs);
        } else {
            LOGE("Shader compilation failed (type: %u).", type);
        }

        glDeleteShader(*shader);
        *shader = 0;
        return false;
    }

    LOGI("Shader compiled (type: %u)", type);
    return true;
}

static bool linkShaders(GLuint vertexShader, GLuint fragmentShader, GLuint* shaderProgram) {

    *shaderProgram = glCreateProgram();
    if (hasErrors("glCreateProgram") || *shaderProgram == 0) {
        LOGE("Could not create shader program.");
        return false;
    }

    glAttachShader(*shaderProgram, vertexShader);
    if (hasErrors("glAttachShader(vertex)")) {
        return false;
    }

    glAttachShader(*shaderProgram, fragmentShader);
    if (hasErrors("glAttachShader(fragment)")) {
        return false;
    }

    glLinkProgram(*shaderProgram);
    if (hasErrors("glLinkProgram")) {
        return false;
    }

    GLint status;
    glGetProgramiv(*shaderProgram, GL_LINK_STATUS, &status);

    if (!status) {
        GLint logsLength;
        glGetProgramiv(*shaderProgram, GL_INFO_LOG_LENGTH, &logsLength);

        if (logsLength > 0) {
            char* logs = (char*)malloc(sizeof(char) * logsLength);
            glGetProgramInfoLog(*shaderProgram, logsLength, nullptr, logs);

            LOGE("Shader linking failed, logs:\n%s", logs);
            free(logs);
        } else {
            LOGE("Shader linking failed.");
        }

        glDeleteProgram(*shaderProgram);
        *shaderProgram = 0;
        return false;
    }

    LOGI("Shader linked.");
    return true;
}

static bool setupShaderProgram(GLuint* shaderProgram, GLint* shaderTransformMatrixHandle, GLint* shaderTextureSamplerHandle) {

    GLuint vertexShader, fragmentShader;
    if (!compileShader(GL_VERTEX_SHADER, VERTEX_SHADER_SOURCE, &vertexShader)) {
        return false;
    }

    if (!compileShader(GL_FRAGMENT_SHADER, FRAGMENT_SHADER_SOURCE, &fragmentShader)) {
        glDeleteShader(vertexShader);
        return false;
    }

    bool result = linkShaders(vertexShader, fragmentShader, shaderProgram);
    glDeleteShader(vertexShader);
    glDeleteShader(fragmentShader);

    if (result) {

        *shaderTransformMatrixHandle = glGetUniformLocation(*shaderProgram, "uTransformMatrix");
        *shaderTextureSamplerHandle = glGetUniformLocation(*shaderProgram, "sYUVTexture");

        if (*shaderTransformMatrixHandle == -1 || *shaderTextureSamplerHandle == -1) {
            LOGE("Could not locate shader parameter handles (transformMatrix: %i, sampler: %i)", *shaderTransformMatrixHandle, *shaderTextureSamplerHandle);

            glDeleteProgram(*shaderProgram);
            *shaderProgram = 0;
            return false;
        }
    }

    return result;
}

static bool setupGeometry(GLuint* vertexArrayObj, GLuint* vertexBufferObj) {
    const GLfloat quadVertices[] = {
            // positions                     // texture Coords
            -1.0f, 1.0f, 0.0f,      0.0f, 1.0f,
            -1.0f,-1.0f, 0.0f,      0.0f, 0.0f,
            1.0f, 1.0f, 0.0f,   1.0f, 1.0f,
            1.0f,-1.0f, 0.0f,   1.0f, 0.0f,
    };

    glGenVertexArrays(1, vertexArrayObj);
    glGenBuffers(1, vertexBufferObj);

    glBindVertexArray(*vertexArrayObj);
    glBindBuffer(GL_ARRAY_BUFFER, *vertexBufferObj);

    glBufferData(GL_ARRAY_BUFFER, sizeof(quadVertices), quadVertices, GL_STATIC_DRAW);
    if (hasErrors("glBufferData")) {
        glBindVertexArray(0);
        glBindBuffer(GL_ARRAY_BUFFER, 0);

        glDeleteVertexArrays(1, vertexArrayObj);
        glDeleteBuffers(1, vertexBufferObj);
        *vertexArrayObj = *vertexBufferObj = 0;
        return false;
    }

    glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, 5 * sizeof(GLfloat), nullptr);
    glEnableVertexAttribArray(0);

    glVertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, 5 * sizeof(GLfloat), (void*)(3 * sizeof(GLfloat)));
    glEnableVertexAttribArray(1);

    glBindVertexArray(0);
    glBindBuffer(GL_ARRAY_BUFFER, 0);

    LOGI("Geometry data setup.");
    return true;
}

bool GLES_YUVConverter::registerStaticResourceRef() {
    if (s_staticReferenceHolders > 0) {
        return true;
    }

    if (!setupShaderProgram(&s_shaderProgram, &s_shaderTransformMatrixHandle, &s_shaderTextureSamplerHandle)) {
        return false;
    }

    if (!setupGeometry(&s_vertexArrayObj, &s_vertexBufferObj)) {
        glDeleteProgram(s_shaderProgram);
        s_shaderProgram = 0;
        return false;
    }

    s_staticReferenceHolders++;
    return true;
}

void GLES_YUVConverter::deregisterStaticResourceRef() {
    if (s_staticReferenceHolders == 0) {
        return;
    }

    s_staticReferenceHolders--;
    if (s_staticReferenceHolders > 0) {
        return;
    }

    if (s_vertexArrayObj) {
        glDeleteVertexArrays(1, &s_vertexArrayObj);
        s_vertexArrayObj = 0;
    }

    if (s_vertexBufferObj) {
        glDeleteBuffers(1, &s_vertexBufferObj);
        s_vertexBufferObj = 0;
    }

    if (s_shaderProgram) {
        glDeleteProgram(s_shaderProgram);
        s_shaderProgram = 0;
    }

    LOGI("Static resources disposed.");
}

//endregion

GLES_YUVConverter::GLES_YUVConverter(GLuint renderTexture, GLint width, GLint height) {
    _renderTexture = renderTexture;
    _width = width; _height = height;

    _sourceTexture = 0;
    _frameBufferObj = 0;
    _disposed = false;
}

bool GLES_YUVConverter::initialize(GLuint *createdSourceTexture) {
    if (!registerStaticResourceRef()) {
        return false;
    }

    glGenFramebuffers(1, &_frameBufferObj);
    glGenTextures(1, &_sourceTexture);
    if (hasErrors("glGenTextures")) {
        return false;
    }

    glBindTexture(GL_TEXTURE_EXTERNAL_OES, _sourceTexture);
    if (hasErrors("glBindTexture")) {
        return false;
    }

    glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

    glBindTexture(GL_TEXTURE_EXTERNAL_OES, 0);

    *createdSourceTexture = _sourceTexture;
    LOGI("Renderer setup.");
    return true;
}

bool GLES_YUVConverter::render(ASurfaceTexture *surfaceTexture) const {

    bool result = false;
    int updateResult;

    // REQUIRED to make this work well in Unity with sRGB
    bool srgbEnabled = glIsEnabled(GL_FRAMEBUFFER_SRGB_EXT);
    glDisable(GL_FRAMEBUFFER_SRGB_EXT);

    glBindFramebuffer(GL_FRAMEBUFFER, _frameBufferObj);
    glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _renderTexture, 0);

    if (hasErrors("glFramebufferTexture2D") || glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE) {
        LOGE("Could not bind frameBuffer to texture.");
        goto draw_cleanup;
    }

    glViewport(0, 0, _width, _height);
    if (hasErrors("glViewport")) {
        goto draw_cleanup;
    }

    glUseProgram(s_shaderProgram);
    if (hasErrors("glUseProgram")) {
        goto draw_cleanup;
    }

    updateResult = ASurfaceTexture_updateTexImage(surfaceTexture);
    if (updateResult) {
        LOGE("Could not update surfaceTexture, error: %i", updateResult);
        goto draw_cleanup;
    }

    float transformMatrix[16];
    ASurfaceTexture_getTransformMatrix(surfaceTexture, transformMatrix);

    glUniformMatrix4fv(s_shaderTransformMatrixHandle, 1, GL_FALSE, transformMatrix);
    if (hasErrors("glUniformMatrix4fv")) {
        goto draw_cleanup;
    }

    glActiveTexture(GL_TEXTURE0);
    glBindTexture(GL_TEXTURE_EXTERNAL_OES, _sourceTexture);
    glUniform1i(s_shaderTextureSamplerHandle, 0);

    glBindVertexArray(s_vertexArrayObj);
    glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);
    result = !hasErrors("glDrawArrays");

draw_cleanup:
    glBindFramebuffer(GL_FRAMEBUFFER, 0);
    glBindVertexArray(0);
    glBindTexture(GL_TEXTURE_EXTERNAL_OES, 0);

    if (srgbEnabled) {
        glEnable(GL_FRAMEBUFFER_SRGB_EXT);
    }

    return result;
}

void GLES_YUVConverter::dispose() {
    if (_disposed) {
        return;
    }

    _disposed = true;
    if (_frameBufferObj) {
        glDeleteFramebuffers(1, &_frameBufferObj);
    }

    if (_sourceTexture) {
        glDeleteTextures(1, &_sourceTexture);
    }

    deregisterStaticResourceRef();
    LOGI("Renderer disposed.");
}
