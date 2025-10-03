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
#include <GLES2/gl2ext.h>
#include <android/log.h>
#include <vector>

#define LOG_TAG "NativeShaderManager"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,     LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR,    LOG_TAG, __VA_ARGS__)

GLuint compileShader(GLenum type, const char* shaderSource) {
    GLuint shader = glCreateShader(type);
    if (shader == 0) {
        LOGE("Could not create shader of type \"%i\". Error: 0x%x", type, glGetError());
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
        } else {
            LOGE("Could not compile shader of type \"%i\". Unknown error.", type);
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
layout(location = 0) in vec2 a_position; // Vertex position ONLY

// No texture coordinate input or output needed

void main() {
    gl_Position = vec4(a_position.xy, 0.0, 1.0); // Output clip space position
}
)glsl";

const char* FRAGMENT_SHADER_SOURCE = R"glsl(
#version 300 es
#extension GL_EXT_YUV_target : require

precision mediump float;
precision mediump __samplerExternal2DY2YEXT;

uniform __samplerExternal2DY2YEXT u_texture;
uniform vec2 u_resolution;

out vec4 outColor;

// Helper function to convert YUV to RGB, using the compute shader's BT.601 matrix
// but corrected for full-range input from the texture sampler.
vec3 computeShader_YUVtoRGB_corrected(vec3 yuv)
{
    // The 'yuv' input from texture() is normalized (0.0 to 1.0).
    // We scale them up to the 0-255 range to use the same matrix math.
    float y = yuv.r * 255.0;
    float u = yuv.g * 255.0;
    float v = yuv.b * 255.0;

    // The U and V components are centered around 128.
    float uf = u - 128.0;
    float vf = v - 128.0;

    // The Y component is now treated as full-range.
    // The incorrect '+ 16.0' offset, which caused the excessive brightness, is removed.
    float yf = y;

    // Apply the ITU-R BT.601 conversion matrix for full-range signals.
    vec3 rgb = vec3(
        yf + 1.402 * vf,
        yf - 0.344136 * uf - 0.714136 * vf,
        yf + 1.772 * uf
    );

    // Normalize the final result back to the 0.0-1.0 range and clamp.
    return clamp(rgb / 255.0, 0.0, 1.0);
}

void main() {
    // Calculate texture coordinates based on fragment position.
    vec2 texCoord = vec2(gl_FragCoord.x / u_resolution.x, 1.0 - (gl_FragCoord.y / u_resolution.y));

    // Sample the external texture to get a YUV value.
    vec4 yuv = texture(u_texture, texCoord);

    // Use the corrected conversion function.
    vec3 converted = computeShader_YUVtoRGB_corrected(yuv.xyz);

    outColor = vec4(converted, 1.0);
}
)glsl";

namespace ShaderManager {
    bool checkGlobalRenderInfo(GlobalRenderInfo *renderInfo) {
        if (renderInfo->program == 0 || renderInfo->vao == 0 || renderInfo->ebo == 0 || renderInfo->vbo == 0 || renderInfo->resolutionUniformLocation == -1 || renderInfo->textureUniformLocation == -1) {
            return setupGlobals(renderInfo);
        }

        return true;
    }

    bool setupGlobals(GlobalRenderInfo *output) {
        if (output == nullptr) {
            LOGE("setupGlobals failed: output GlobalRenderInfo pointer is null.");
            return false;
        }

        *output = {};

        // --- Vertex Shader ---
        GLuint vertexShader = compileShader(GL_VERTEX_SHADER, VERTEX_SHADER_SOURCE);
        if (!vertexShader) {
            LOGE("setupGlobals failed: vertex shader compilation failed.");
            return false;
        }

        // --- Fragment Shader ---
        GLuint fragmentShader = compileShader(GL_FRAGMENT_SHADER, FRAGMENT_SHADER_SOURCE);
        if (!fragmentShader) {
            LOGE("setupGlobals failed: fragment shader compilation failed.");
            glDeleteShader(vertexShader);
            return false;
        }

        // --- Shader Program ---
        output->program = glCreateProgram();
        checkGlError("glCreateProgram");

        if (output->program == 0) {
            LOGE("setupGlobals failed: shader program creation failed.");
            glDeleteShader(vertexShader);
            glDeleteShader(fragmentShader);
            return false;
        }

        glAttachShader(output->program, vertexShader);
        checkGlError("glAttachShader Vertex");

        glAttachShader(output->program, fragmentShader);
        checkGlError("glAttachShader Fragment");

        glBindAttribLocation(output->program, 0, "a_position");

        glLinkProgram(output->program);
        checkGlError("glLinkProgram");

        GLint linkStatus;
        glGetProgramiv(output->program, GL_LINK_STATUS, &linkStatus);
        if (!linkStatus) {
            GLint infoLen = 0;
            glGetProgramiv(output->program, GL_INFO_LOG_LENGTH, &infoLen);
            if (infoLen > 1) {
                char *infoLog = (char*)malloc(sizeof(char) * infoLen);
                glGetProgramInfoLog(output->program, infoLen, nullptr, infoLog);

                LOGE("setupGlobals failed: error linking shader program:\n%s", infoLog);
                free(infoLog);
            } else {
                LOGE("setupGlobals failed: unknown error linking shader program.");
            }

            glDeleteShader(vertexShader);
            glDeleteShader(fragmentShader);
            cleanupGlobals(output);
            return false;
        }

        glDeleteShader(vertexShader);
        glDeleteShader(fragmentShader);

        // --- Texture Uniform Location ---
        output->textureUniformLocation = glGetUniformLocation(output->program, "u_texture");
        checkGlError("glGetUniformLocation u_texture");
        if (output->textureUniformLocation == -1) {
            LOGE("setupGlobals failed: could not find uniform location for u_texture.");
            cleanupGlobals(output);
            return false;
        }

        // --- Resolution Uniform Location ---
        output->resolutionUniformLocation = glGetUniformLocation(output->program, "u_resolution");
        checkGlError("glGetUniformLocation u_resolution");
        if (output->resolutionUniformLocation == -1) {
            LOGE("setupGlobals failed: could not find uniform location for u_resolution.");
            cleanupGlobals(output);
            return false;
        }

        // --- Vertex Data ---
        // Format: PosX, PosY
        const GLfloat vertices[] = {
                // Position
                1.0f,  1.0f, // Top Right
                1.0f, -1.0f, // Bottom Right
                -1.0f, -1.0f, // Bottom Left
                -1.0f,  1.0f // Top Left
        };

        const GLuint indices[] = {
                0, 1, 3,  // First Triangle (TR, BR, TL)
                1, 2, 3   // Second Triangle (BR, BL, TL)
        };

        // --- Vertex Buffer Object (VBO), Element Buffer Object (EBO), Vertex Array Object (VAO) ---
        glGenBuffers(1, &output->vbo);
        checkGlError("glGenBuffers (VBO)");
        if (output->vbo == 0) {
            LOGE("setupGlobals failed: VBO could not be created.");
            cleanupGlobals(output);
            return false;
        }

        glGenBuffers(1, &output->ebo);
        checkGlError("glGenBuffers (EBO)");
        if (output->ebo == 0) {
            LOGE("setupGlobals failed: EBO could not be created.");
            cleanupGlobals(output);
            return false;
        }

        glGenVertexArrays(1, &output->vao);
        checkGlError("glGenVertexArrays (VAO)");
        if (output->vao == 0) {
            LOGE("setupGlobals failed: VAO could not be created.");
            cleanupGlobals(output);
            return false;
        }

        // --- Configure VAO ---
        glBindVertexArray(output->vao);
        checkGlError("glBindVertexArray");

        // Bind and load VBO data
        glBindBuffer(GL_ARRAY_BUFFER, output->vbo);
        checkGlError("glBindBuffer VBO");
        glBufferData(GL_ARRAY_BUFFER, sizeof(vertices), vertices, GL_STATIC_DRAW);
        checkGlError("glBufferData VBO");

        // Bind and load EBO data
        glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, output->ebo);
        checkGlError("glBindBuffer EBO");
        glBufferData(GL_ELEMENT_ARRAY_BUFFER, sizeof(indices), indices, GL_STATIC_DRAW);
        checkGlError("glBufferData EBO");

        // --- Configure Vertex Attributes ---
        // Position attribute (location = 0)
        glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, 2 * sizeof(GLfloat), nullptr);
        checkGlError("glVertexAttribPointer Pos");
        glEnableVertexAttribArray(0);
        checkGlError("glEnableVertexAttribArray Pos");

        // Unbind VAO, VBO, EBO (VAO binding already captures EBO binding)
        glBindVertexArray(0);
        glBindBuffer(GL_ARRAY_BUFFER, 0);
        glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, 0); // Unbind EBO after VAO unbind

        checkGlError("setup VBO, EBO and VAO attributes");

        LOGI("setupGlobals completed successfully.");
        return true;
    }

    void cleanupGlobals(GlobalRenderInfo *renderInfo) {
        if (renderInfo == nullptr) {
            return;
        }

        LOGI("cleanupGlobals called");

        if (renderInfo->vao) {
            glDeleteVertexArrays(1, &renderInfo->vao);
            renderInfo->vao = 0;
        }

        if (renderInfo->ebo) {
            glDeleteBuffers(1, &renderInfo->ebo);
            renderInfo->ebo = 0;
        }

        if (renderInfo->vbo) {
            glDeleteBuffers(1, &renderInfo->vbo);
            renderInfo->vbo = 0;
        }

        if (renderInfo->program) {
            glDeleteProgram(renderInfo->program);
            renderInfo->program = 0;
        }

        // Reset other members
        renderInfo->textureUniformLocation = -1;
        renderInfo->resolutionUniformLocation = -1;

        LOGI("cleanupGlobals finished.");
    }

    GLuint createFrameBuffer() {
        // --- Frame Buffer Object (FBO) ---
        GLuint frameBuffer;

        glGenFramebuffers(1, &frameBuffer);
        checkGlError("glGenFramebuffers");
        if (frameBuffer == 0) {
            LOGE("createFrameBuffer: FrameBuffer could not be created.");
            return 0;
        }

        LOGI("createFrameBuffer completed successfully.");
        return frameBuffer;
    }

    void cleanupFrameBuffer(GLuint frameBufferId) {
        if (frameBufferId == 0) {
            return;
        }

        glDeleteFramebuffers(1, &frameBufferId);
        LOGI("cleanupFrameBuffer finished.");
    }

    void renderFrame(GlobalRenderInfo* renderInfo, DrawInfo* drawInfo) {
        // Basic validation
        if (!renderInfo || renderInfo->program == 0 || renderInfo->vao == 0 || renderInfo->textureUniformLocation == -1 || renderInfo->resolutionUniformLocation == -1) {
            LOGE("renderFrame error: Invalid RenderInfo or setup incomplete.");
            return;
        }

        if (!drawInfo || drawInfo->fbo == 0 || drawInfo->sourceTextureId == 0 || drawInfo->targetTextureId == 0) {
            LOGE("renderFrame error: Invalid DrawInfo.");
            return;
        }

        if (drawInfo->viewportWidth <= 0 || drawInfo->viewportHeight <= 0) {
            LOGE("renderFrame error: Invalid target dimensions (%dx%d).", drawInfo->viewportWidth, drawInfo->viewportHeight);
            return;
        }

        // 1. Bind the FBO
        glBindFramebuffer(GL_FRAMEBUFFER, drawInfo->fbo);
        checkGlError("glBindFramebuffer");

        // 2. Attach the caller's target texture as color attachment
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, drawInfo->targetTextureId, 0);
        checkGlError("glFramebufferTexture2D");

        // 3. Check FBO status
        GLenum status = glCheckFramebufferStatus(GL_FRAMEBUFFER);
        if (status != GL_FRAMEBUFFER_COMPLETE) {
            LOGE("FrameBuffer is not complete! Status: 0x%x", status);
            glBindFramebuffer(GL_FRAMEBUFFER, 0);
            return;
        }

        // 4. Set Viewport to target texture size
        glViewport(0, 0, drawInfo->viewportWidth, drawInfo->viewportHeight);
        checkGlError("glViewport (FBO)");

        // 5. Use the shader program
        glUseProgram(renderInfo->program);
        checkGlError("glUseProgram");

        // 6. Bind the source EXTERNAL texture
        glActiveTexture(GL_TEXTURE0); // Activate texture unit 0
        checkGlError("glActiveTexture");
        glBindTexture(GL_TEXTURE_EXTERNAL_OES, drawInfo->sourceTextureId); // Bind the external texture
        checkGlError("glBindTexture GL_TEXTURE_EXTERNAL_OES");

        // 7. Set the sampler uniform to use texture unit 0
        glUniform1i(renderInfo->textureUniformLocation, 0);
        checkGlError("glUniform1i u_texture");

        glUniform2f(renderInfo->resolutionUniformLocation, (GLfloat)drawInfo->viewportWidth, (GLfloat)drawInfo->viewportHeight);
        checkGlError("glUniform2i u_resolution");

        // 8. Bind VAO (contains VBO+EBO configuration)
        glBindVertexArray(renderInfo->vao);
        checkGlError("glBindVertexArray");

        // 9. Draw the quad (output goes to the FBO's attached texture)
        glDrawElements(GL_TRIANGLES, 6, GL_UNSIGNED_INT, nullptr);
        checkGlError("glDrawElements");

        // 10. Unbind resources (good practice)
        glBindVertexArray(0);
        glBindTexture(GL_TEXTURE_EXTERNAL_OES, 0); // Unbind external texture from unit 0
        glUseProgram(0);
        glBindFramebuffer(GL_FRAMEBUFFER, 0); // Unbind the FBO, reverting to default framebuffer
    }

} // namespace ShaderManager