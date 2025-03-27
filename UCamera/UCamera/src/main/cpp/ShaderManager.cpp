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

#include "ShaderManager.h"
#include <android/log.h>
#include <vector>

#define LOG_TAG "NativeShaderManager"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,     LOG_TAG, __VA_ARGS__)
#define LOGW(...) __android_log_print(ANDROID_LOG_WARN,     LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR,    LOG_TAG, __VA_ARGS__)

GLuint compileShader(GLenum type, const char* shaderSource) {
    GLuint shader = glCreateShader(type);
    if (shader == 0) {
        LOGE("Could not create shader of type \"%i\".", type);
        return 0;
    }

    glShaderSource(shader, 1, &shaderSource, nullptr);
    glCompileShader(shader);

    GLint compileStatus;
    glGetShaderiv(shader, GL_COMPILE_STATUS, &compileStatus);
    if (compileStatus == 0) {
        GLint infoLength = 0;
        glGetShaderiv(shader, GL_INFO_LOG_LENGTH, &infoLength);
        if (infoLength > 1) {
            char* infoLog = (char*)malloc(sizeof(char) * infoLength);
            glGetShaderInfoLog(shader, infoLength, nullptr, infoLog);

            LOGE("Could not compile shader of type \"%i\" due to error:\n%s", type, infoLog);
            free(infoLog);
        }

        glDeleteShader(shader);
        return 0;
    }

    return shader;
}

void checkGlError(const char* operation) {
    for (GLenum error = glGetError(); error != GL_NO_ERROR; error = glGetError()) {
        LOGE("Error after \"%s\" operation: 0x%x", operation, error);
    }
}

const char* VERTEX_SHADER_SOURCE = R"glsl(
#version 300 es
layout(location = 0) in vec2 a_position;

void main() {
    gl_Position = vec4(a_position.xy, 0.0, 1.0);
}
)glsl";

const char* FRAGMENT_SHADER_SOURCE = R"glsl(
#version 300 es
precision mediump float; // Default precision

uniform float u_time; // Time uniform
out vec4 outColor; // Output color

void main() {
    outColor = vec4(0.0f, sin(u_time) / 2.0f + 0.5f, 0.0f, 1.0f);
}
)glsl";

namespace ShaderManager {
    bool setupGraphics(RenderInfo *output) {
        LOGI("setupGraphics called.");
        if (output == nullptr) {
            LOGE("setupGraphics failed: output RenderInfo pointer is null.");
            return false;
        }

        *output = {};

        // --- Vertex Shader ---
        GLuint vertexShader = compileShader(GL_VERTEX_SHADER, VERTEX_SHADER_SOURCE);
        if (!vertexShader) {
            LOGE("setupGraphics failed: vertex shader compilation failed.");
            return false;
        }

        // --- Fragment Shader ---
        GLuint fragmentShader = compileShader(GL_FRAGMENT_SHADER, FRAGMENT_SHADER_SOURCE);
        if (!fragmentShader) {
            LOGE("setupGraphics failed: fragment shader compilation failed.");
            glDeleteShader(vertexShader);
            return false;
        }

        // --- Shader Program ---
        output->program = glCreateProgram();
        checkGlError("glCreateProgram");

        if (output->program == 0) {
            LOGE("setupGraphics failed: shader program creation failed.");
            glDeleteShader(vertexShader);
            glDeleteShader(fragmentShader);
            return false;
        }

        glAttachShader(output->program, vertexShader);
        checkGlError("glAttachShader Vertex");

        glAttachShader(output->program, fragmentShader);
        checkGlError("glAttachShader Fragment");

        glLinkProgram(output->program);

        GLint linkStatus;
        glGetProgramiv(output->program, GL_LINK_STATUS, &linkStatus);
        if (!linkStatus) {
            GLint infoLen = 0;
            glGetProgramiv(output->program, GL_INFO_LOG_LENGTH, &infoLen);
            if (infoLen > 1) {
                char *infoLog = (char*)malloc(sizeof(char) * infoLen);
                glGetProgramInfoLog(output->program, infoLen, nullptr, infoLog);

                LOGE("Error linking shader program:\n%s", infoLog);
                free(infoLog);
            }

            glDeleteShader(vertexShader);
            glDeleteShader(fragmentShader);
            cleanupGraphics(output);
            return false;
        }

        glDeleteShader(vertexShader);
        glDeleteShader(fragmentShader);

        // --- Time Uniform Location ---
        output->timeUniformLocation = glGetUniformLocation(output->program, "u_time");
        if (output->timeUniformLocation == -1) {
            LOGW("Could not find uniform location for u_time. Time animation will not work.");
        }

        // --- Element Buffer and Array Objects ---
        const GLfloat vertices[] = {
            1.0f, 1.0f,     // Top right
            1.0f, -1.0f,    // Bottom right
            -1.0f, -1.0f,   // Bottom left
            -1.0f, 1.0f,    // Top right
        };

        const GLuint indices[] = {
            0, 1, 3,
            1, 2, 3
        };

        glGenBuffers(1, &output->vbo);
        checkGlError("glGenBuffers (VBO)");
        if (output->vbo == 0) {
            LOGE("Graphics setup failed as the VBO could not be created.");
            cleanupGraphics(output);
            return false;
        }

        glGenBuffers(1, &output->ebo);
        checkGlError("glGenBuffers (EBO)");
        if (output->ebo == 0) {
            LOGE("Graphics setup failed as the EBO could not be created.");
            cleanupGraphics(output);
            return false;
        }

        glGenVertexArrays(1, &output->vao);
        checkGlError("glGenVertexArrays (VAO)");
        if (output->vao == 0) {
            LOGE("Graphics setup failed as VAO could not be created.");
            cleanupGraphics(output);
            return false;
        }

        glBindVertexArray(output->vao);
        glBindBuffer(GL_ARRAY_BUFFER, output->vbo);
        glBufferData(GL_ARRAY_BUFFER, sizeof(vertices), vertices, GL_STATIC_DRAW);

        glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, output->ebo);
        glBufferData(GL_ELEMENT_ARRAY_BUFFER, sizeof(indices), indices, GL_STATIC_DRAW);

        glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, 2 * sizeof(GLfloat), nullptr);
        glEnableVertexAttribArray(0);

        glBindVertexArray(0);
        glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, 0);
        glBindBuffer(GL_ARRAY_BUFFER, 0);

        checkGlError("setup VBO, EBO and VAO");

        // --- Frame Buffer Object ---
        glGenFramebuffers(1, &output->fbo);
        checkGlError("glGenFramebuffers FBO");

        if (output->fbo == 0) {
            LOGE("Graphics setup failed as FrameBuffer could not be created.");
            cleanupGraphics(output);
            return false;
        }

        // -- Record Start Time ---
        output->startTime = std::chrono::high_resolution_clock::now();

        LOGI("setupGraphics completed successfully.");
        return true;
    }

    void renderFrame(RenderInfo *renderInfo, GLuint targetTextureId, int targetWidth, int targetHeight) {
        LOGI("renderFrame called.");
        if (!renderInfo || renderInfo->program == 0 || renderInfo->fbo == 0) {
            LOGE("renderFrame error: Invalid RenderInfo provided or not setup.");
            return;
        }

        // 1. Bind the FBO
        glBindFramebuffer(GL_FRAMEBUFFER, renderInfo->fbo);
        checkGlError("glBindFramebuffer");

        // 2. Attach the caller's texture as color attachment
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, targetTextureId, 0);
        checkGlError("glFramebufferTexture2D");

        // 3. Check FBO status
        GLenum status = glCheckFramebufferStatus(GL_FRAMEBUFFER);
        if (status != GL_FRAMEBUFFER_COMPLETE) {
            LOGE("FrameBuffer is not complete! Status: 0x%x", status);

            glBindFramebuffer(GL_FRAMEBUFFER, 0);
            return;
        }

        // 4. Set Viewport to target texture size
        glViewport(0, 0, targetWidth, targetHeight);
        checkGlError("glViewport (FBO)");

        // 5. Use the shader program
        glUseProgram(renderInfo->program);
        checkGlError("glUseProgram");

        // 6. Update time uniform
        if (renderInfo->timeUniformLocation != -1) {
            auto now = std::chrono::high_resolution_clock::now();
            std::chrono::duration<float> elapsed = now - renderInfo->startTime;

            glUniform1f(renderInfo->timeUniformLocation, elapsed.count());
            checkGlError("glUniform1f u_time");
        }

        // 7. Bind VAO
        glBindVertexArray(renderInfo->vao);
        checkGlError("glBindVertexArray");

        // 8. Draw the quad (output goes to the FBO's attached texture)
        glDrawElements(GL_TRIANGLES, 6, GL_UNSIGNED_INT, nullptr);
        checkGlError("glDrawElements");

        // 9. Unbind resources
        glBindVertexArray(0);
        glUseProgram(0);
        glBindFramebuffer(GL_FRAMEBUFFER, 0);

        glFinish();
        LOGI("renderFrame completed.");
    }

    void cleanupGraphics(RenderInfo *renderInfo) {
        if (!renderInfo) return;
        LOGI("cleanupGraphics called");

        if (renderInfo->program) {
            glDeleteProgram(renderInfo->program);
            renderInfo->program = 0;
        }

        if (renderInfo->vao) {
            glDeleteVertexArrays(1, &renderInfo->vao);
            renderInfo->vao = 0;
        }

        if (renderInfo->vbo) {
            glDeleteBuffers(1, &renderInfo->vbo);
            renderInfo->vbo = 0;
        }

        if (renderInfo->ebo) {
            glDeleteBuffers(1, &renderInfo->ebo);
            renderInfo->ebo = 0;
        }

        if (renderInfo->fbo) {
            glDeleteFramebuffers(1, &renderInfo->fbo);
            renderInfo->fbo = 0;
        }

        renderInfo->timeUniformLocation = -1;
    }

}