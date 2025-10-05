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

#include "Renderer.h"
#include <android/log.h>
#include <GLES2/gl2ext.h>
#include <malloc.h>

#define TAG "STRenderer"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

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

uint8_t Renderer::_staticReferenceHolders =     0;
GLuint  Renderer::_shaderProgram =              0;
GLint   Renderer::_transformMatrixHandle =      0;
GLint   Renderer::_textureSamplerHandle =       0;
GLuint  Renderer::_vertexBufferObject =         0;
GLuint  Renderer::_vertexArrayObject =          0;

Renderer::Renderer(GLuint unityTexture, GLint width, GLint height) {
    _unityTexture = unityTexture;
    _sourceTexture = 0;
    _frameBufferObject = 0;

    _width = width; _height = height;
    _disposed = false;
}

bool Renderer::initialize(GLuint *texture) {
    if (_shaderProgram == 0) {
        GLuint vertexShader, fragmentShader;
        if (!compileShader(GL_VERTEX_SHADER, VERTEX_SHADER_SOURCE, &vertexShader)) {
            return false;
        }

        if (!compileShader(GL_FRAGMENT_SHADER, FRAGMENT_SHADER_SOURCE, &fragmentShader)) {
            glDeleteShader(vertexShader);
            return false;
        }

        bool linkStatus = linkShaderProgram(vertexShader, fragmentShader);
        glDeleteShader(vertexShader);
        glDeleteShader(fragmentShader);
        if (!linkStatus) {
            return false;
        }
    }

    if (_vertexBufferObject == 0 && !setupGeometry()) {
        glDeleteProgram(_shaderProgram);
        _shaderProgram = 0;
        return false;
    }

    glGenFramebuffers(1, &_frameBufferObject);
    glGenTextures(1, &_sourceTexture);
    hasGlErrors("glGenTextures");

    glBindTexture(GL_TEXTURE_EXTERNAL_OES, _sourceTexture);
    hasGlErrors("glBindTexture");

    glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    glTexParameteri(GL_TEXTURE_EXTERNAL_OES, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

    glBindTexture(GL_TEXTURE_EXTERNAL_OES, 0);

    *texture = _sourceTexture;
    LOGI("Renderer setup complete.");

    _staticReferenceHolders++;
    return true;
}

bool Renderer::render(ASurfaceTexture* surfaceTexture) const {
    ASurfaceTexture_updateTexImage(surfaceTexture);

    float transformMatrix[16];
    ASurfaceTexture_getTransformMatrix(surfaceTexture, transformMatrix);

    glBindFramebuffer(GL_FRAMEBUFFER, _frameBufferObject);
    glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _unityTexture, 0);

    if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE) {
        glBindFramebuffer(GL_FRAMEBUFFER, 0);
        LOGE("Could not bind framebuffer to texture.");
        return false;
    }

    glViewport(0, 0, _width, _height);
    glUseProgram(_shaderProgram);

    glUniformMatrix4fv(_transformMatrixHandle, 1, GL_FALSE, transformMatrix);

    glActiveTexture(GL_TEXTURE0);
    glBindTexture(GL_TEXTURE_EXTERNAL_OES, _sourceTexture);
    glUniform1i(_textureSamplerHandle, 0);

    glBindVertexArray(_vertexArrayObject);
    glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);

    glBindFramebuffer(GL_FRAMEBUFFER, 0);
    glBindVertexArray(0);
    glBindTexture(GL_TEXTURE_EXTERNAL_OES, 0);
    return true;
}

void Renderer::dispose() {
    // Delete all resources, including static, since we don't know when or if
    // Unity will call any graphics related plugin methods.
    if (_disposed) {
        return;
    }

    _disposed = true;
    _staticReferenceHolders--;

    if (_frameBufferObject) {
        glDeleteFramebuffers(1, &_frameBufferObject);
        _frameBufferObject = 0;
    }

    if (_sourceTexture) {
        glDeleteTextures(1, &_sourceTexture);
        _sourceTexture = 0;
    }

    if (_staticReferenceHolders <= 0) {
        if (_shaderProgram) {
            glDeleteProgram(_shaderProgram);
            _shaderProgram = 0;
        }

        if (_vertexArrayObject) {
            glDeleteVertexArrays(1, &_vertexArrayObject);
            _vertexArrayObject = 0;
        }

        if (_vertexBufferObject) {
            glDeleteBuffers(1, &_vertexBufferObject);
            _vertexBufferObject = 0;
        }
    }

    LOGI("Renderer disposed.");
}

bool Renderer::linkShaderProgram(GLuint vertexShader, GLuint fragmentShader) {
    _shaderProgram = glCreateProgram();
    hasGlErrors("glCreateProgram");
    if (_shaderProgram == 0) {
        LOGE("Could not create shader program.");
        return false;
    }

    glAttachShader(_shaderProgram, vertexShader);
    glAttachShader(_shaderProgram, fragmentShader);
    glLinkProgram(_shaderProgram);

    GLint linkStatus;
    glGetProgramiv(_shaderProgram, GL_LINK_STATUS, &linkStatus);
    if (!linkStatus) {
        GLint infoLogLength;
        glGetProgramiv(_shaderProgram, GL_INFO_LOG_LENGTH, &infoLogLength);

        if (infoLogLength > 0) {
            char* infoLog = (char*)malloc(sizeof(char) * infoLogLength);
            glGetProgramInfoLog(_shaderProgram, infoLogLength, nullptr, infoLog);

            LOGE("Could not link shader due to error:\n%s", infoLog);
            free(infoLog);
        } else {
            LOGE("Could not link shader.");
        }

        glDeleteProgram(_shaderProgram);
        _shaderProgram = 0;

        return false;
    }

    _transformMatrixHandle = glGetUniformLocation(_shaderProgram, "uTransformMatrix");
    _textureSamplerHandle = glGetUniformLocation(_shaderProgram, "sYUVTexture");

    LOGI("Linked shader program.");
    return true;
}

bool Renderer::compileShader(GLenum type, const char *source, GLuint* shader) {
    *shader = glCreateShader(type);
    hasGlErrors("glCreateShader");
    if (*shader == 0) {
        LOGE("Could not create shader of type: %u", type);
        return false;
    }

    glShaderSource(*shader, 1, &source, nullptr);
    glCompileShader(*shader);

    GLint compileStatus;
    glGetShaderiv(*shader, GL_COMPILE_STATUS, &compileStatus);
    if (!compileStatus) {
        GLint infoLogLength;
        glGetShaderiv(*shader, GL_INFO_LOG_LENGTH, &infoLogLength);

        if (infoLogLength > 0) {
            char* infoLog = (char*)malloc(sizeof(char) * infoLogLength);
            glGetShaderInfoLog(*shader, infoLogLength, nullptr, infoLog);

            LOGE("Could not compile shader of type \"%i\" due to error:\n%s", type, infoLog);
            free(infoLog);
        } else {
            LOGE("Could not compile shader of type: %u", type);
        }

        glDeleteShader(*shader);
        *shader = 0;

        return false;
    }

    LOGI("Compiled shader of type: %u", type);
    return true;
}

bool Renderer::setupGeometry() {
    const GLfloat quadVertices[] = {
        // positions                     // texture Coords
        -1.0f, 1.0f, 0.0f,      0.0f, 1.0f,
        -1.0f,-1.0f, 0.0f,      0.0f, 0.0f,
        1.0f, 1.0f, 0.0f,   1.0f, 1.0f,
        1.0f,-1.0f, 0.0f,   1.0f, 0.0f,
    };

    glGenVertexArrays(1, &_vertexArrayObject);
    if (_vertexArrayObject == 0) {
        LOGE("Could not create vertex array object.");
        return false;
    }

    glGenBuffers(1, &_vertexBufferObject);
    if (_vertexBufferObject == 0) {
        LOGE("Could not create vertex buffer object.");
        glDeleteVertexArrays(1, &_vertexArrayObject);
        _vertexArrayObject = 0;
        return false;
    }

    glBindVertexArray(_vertexArrayObject);
    glBindBuffer(GL_ARRAY_BUFFER, _vertexBufferObject);
    glBufferData(GL_ARRAY_BUFFER, sizeof(quadVertices), &quadVertices, GL_STATIC_DRAW);
    if (hasGlErrors("glBufferData")) {
        glBindVertexArray(0);
        glBindBuffer(GL_ARRAY_BUFFER, 0);

        glDeleteVertexArrays(1, &_vertexArrayObject);
        glDeleteBuffers(1, &_vertexBufferObject);

        _vertexArrayObject = 0;
        _vertexBufferObject = 0;
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

bool Renderer::hasGlErrors(const char* methodName) {
    bool hasError = false;

    GLenum error;
    while ((error = glGetError()) != GL_NO_ERROR) {
        LOGE("Encountered GL error %u after \"%s\"", error, methodName);
        hasError = true;
    }

    return hasError;
}
