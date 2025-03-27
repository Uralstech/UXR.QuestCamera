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
#define LOGW(...) __android_log_print(ANDROID_LOG_WARN,     LOG_TAG, __VA_ARGS__)
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
layout(location = 0) in vec2 a_position; // Vertex position
layout(location = 1) in vec2 a_texCoord; // Texture coordinate input

out vec2 v_texCoord; // Pass texture coordinate to fragment shader

void main() {
    gl_Position = vec4(a_position.xy, 0.0, 1.0); // Output clip space position
    v_texCoord = a_texCoord;                     // Pass tex coord
}
)glsl";

const char* FRAGMENT_SHADER_SOURCE = R"glsl(
#version 300 es
#extension GL_EXT_YUV_target : require

precision mediump float;
precision mediump __samplerExternal2DY2YEXT;

uniform __samplerExternal2DY2YEXT u_texture;

in vec2 v_texCoord;
out vec4 outColor;

void main() {
    // Sample the external texture at the interpolated coordinate
    vec2 flippedTexCoord = vec2(v_texCoord.x, 1.0 - v_texCoord.y);
    vec4 yuv = texture(u_texture, flippedTexCoord);

    vec3 converted = yuv_2_rgb(yuv.xyz, itu_601);
    outColor = vec4(converted, 1.0);
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

        glBindAttribLocation(output->program, 0, "a_position");
        glBindAttribLocation(output->program, 1, "a_texCoord");

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

                LOGE("Error linking shader program:\n%s", infoLog);
                free(infoLog);
            } else {
                LOGE("Error linking shader program. Unknown error.");
            }

            glDeleteShader(vertexShader);
            glDeleteShader(fragmentShader);
            cleanupGraphics(output);
            return false;
        }

        glDeleteShader(vertexShader);
        glDeleteShader(fragmentShader);

        // --- Texture Uniform Location ---
        output->textureUniformLocation = glGetUniformLocation(output->program, "u_texture");
        checkGlError("glGetUniformLocation u_texture");
        if (output->textureUniformLocation == -1) {
            LOGE("Could not find uniform location for u_texture. Texture copying will fail.");
            cleanupGraphics(output);
            return false;
        }

        // --- Vertex Data (Position + Texture Coordinates) ---
        // Format: PosX, PosY, TexCoordX, TexCoordY
        const GLfloat vertices[] = {
                // Position      // Tex Coords
                1.0f,  1.0f,    1.0f, 1.0f, // Top Right
                1.0f, -1.0f,    1.0f, 0.0f, // Bottom Right
                -1.0f, -1.0f,    0.0f, 0.0f, // Bottom Left
                -1.0f,  1.0f,    0.0f, 1.0f  // Top Left
        };

        const GLuint indices[] = {
                0, 1, 3,  // First Triangle (TR, BR, TL)
                1, 2, 3   // Second Triangle (BR, BL, TL)
        };

        // --- Vertex Buffer Object (VBO), Element Buffer Object (EBO), Vertex Array Object (VAO) ---
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
        GLsizei stride = 4 * sizeof(GLfloat); // Stride for position (vec2) + texCoord (vec2)

        // Position attribute (location = 0)
        glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, stride, nullptr);
        checkGlError("glVertexAttribPointer Pos");
        glEnableVertexAttribArray(0);
        checkGlError("glEnableVertexAttribArray Pos");

        // Texture coordinate attribute (location = 1)
        glVertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, stride, (void*)(2 * sizeof(GLfloat))); // Offset by 2 floats
        checkGlError("glVertexAttribPointer TexCoord");
        glEnableVertexAttribArray(1);
        checkGlError("glEnableVertexAttribArray TexCoord");

        // Unbind VAO, VBO, EBO (VAO binding already captures EBO binding)
        glBindVertexArray(0);
        glBindBuffer(GL_ARRAY_BUFFER, 0);
        glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, 0); // Unbind EBO after VAO unbind

        checkGlError("setup VBO, EBO and VAO attributes");

        // --- Frame Buffer Object (FBO) ---
        glGenFramebuffers(1, &output->fbo);
        checkGlError("glGenFramebuffers FBO");

        if (output->fbo == 0) {
            LOGE("Graphics setup failed as FrameBuffer could not be created.");
            cleanupGraphics(output);
            return false;
        }

        LOGI("setupGraphics completed successfully.");
        return true;
    }

    void renderFrame(RenderInfo *renderInfo, GLuint sourceTextureId, GLuint targetTextureId, int targetWidth, int targetHeight) {
        // Basic validation
        if (!renderInfo || renderInfo->program == 0 || renderInfo->vao == 0 || renderInfo->fbo == 0 || renderInfo->textureUniformLocation == -1) {
            LOGE("renderFrame error: Invalid RenderInfo or setup incomplete.");
            return;
        }

        if (sourceTextureId == 0 || targetTextureId == 0) {
            LOGE("renderFrame error: Invalid source or target texture ID.");
            return;
        }

        if (targetWidth <= 0 || targetHeight <= 0) {
            LOGE("renderFrame error: Invalid target dimensions (%dx%d).", targetWidth, targetHeight);
            return;
        }

        // LOGI("renderFrame called. SourceTex: %u, TargetTex: %u, Size: %dx%d", sourceTextureId, targetTextureId, targetWidth, targetHeight);

        // 1. Bind the FBO
        glBindFramebuffer(GL_FRAMEBUFFER, renderInfo->fbo);
        checkGlError("glBindFramebuffer");

        // 2. Attach the caller's target texture as color attachment
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

        // 6. Bind the source EXTERNAL texture
        glActiveTexture(GL_TEXTURE0); // Activate texture unit 0
        checkGlError("glActiveTexture");
        glBindTexture(GL_TEXTURE_EXTERNAL_OES, sourceTextureId); // Bind the external texture
        checkGlError("glBindTexture GL_TEXTURE_EXTERNAL_OES");

        // 7. Set the sampler uniform to use texture unit 0
        glUniform1i(renderInfo->textureUniformLocation, 0);
        checkGlError("glUniform1i u_texture");

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

        // LOGI("renderFrame completed.");
    }

    void cleanupGraphics(RenderInfo *renderInfo) {
        if (!renderInfo) return;
        LOGI("cleanupGraphics called");

        if (renderInfo->fbo) {
            glDeleteFramebuffers(1, &renderInfo->fbo);
            renderInfo->fbo = 0;
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

        if (renderInfo->program) {
            glDeleteProgram(renderInfo->program);
            renderInfo->program = 0;
        }

        // Reset other members
        renderInfo->textureUniformLocation = -1;

        LOGI("cleanupGraphics finished.");
    }

} // namespace ShaderManager