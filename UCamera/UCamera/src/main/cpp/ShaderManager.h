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

#ifndef UCAMERA_SHADERMANAGER_H
#define UCAMERA_SHADERMANAGER_H

#include <GLES3/gl3.h>
#include <chrono>

// Optional: Wrap in a namespace to avoid polluting the global namespace
namespace ShaderManager {

    /**
     * @brief Global information for rendering.
     */
    struct GlobalRenderInfo {
        GLuint program = 0;
        GLuint vbo = 0;
        GLuint ebo = 0;
        GLuint vao = 0;

        GLint textureUniformLocation = -1;
        GLint resolutionUniformLocation = -1;
    };

    /**
     * @brief Information for draw calls for a pair of source and target textures.
     */
    struct DrawInfo {
        GLuint sourceTextureId;
        GLuint targetTextureId;
        GLuint fbo;

        GLint viewportWidth;
        GLint viewportHeight;
    };

    /**
     * @brief Checks if a GlobalRenderInfo struct contains valid data. If not, releases it and creates a new one.
     * @param renderInfo Pointer to a GlobalRenderInfo struct to be checked.
     * @return True if the struct is valid or creation was successful, false otherwise.
     */
    bool checkGlobalRenderInfo(GlobalRenderInfo* renderInfo);

    /**
     * @brief Sets up global OpenGL resources.
     *
     * Compiles shaders, links program, finds uniforms and creates geometry (VBO/EBO/VAO).
     *
     * @param output Pointer to a GlobalRenderInfo struct to be populated.
     * @return True if setup was successful, false otherwise.
     */
    bool setupGlobals(GlobalRenderInfo* output);

    /**
     * @brief Cleans up global OpenGL resources.
     *
     * Deletes the shader program, VBO, EBO and VAO.
     *
     * @param renderInfo Pointer to the GlobalRenderInfo struct whose resources should be cleaned up.
     */
    void cleanupGlobals(GlobalRenderInfo* renderInfo);

    /**
     * @brief Creates a new FrameBuffer object.
     * @return The ID of the FrameBuffer object, 0 if creation failed.
     */
    GLuint createFrameBuffer();

    /**
     * @brief Cleans up a FrameBuffer object.
     * @param frameBufferId The ID of the FrameBuffer object to delete.
     */
    void cleanupFrameBuffer(GLuint frameBufferId);

    /**
     * @brief Renders (with conversion) a source Texture into a target Texture using an FBO.
     *
     * Binds the FBO, attaches the targetTextureId, sets viewport,
     * uses the shader program, binds the sourceTextureId (external)
     * for sampling, draws the quad with conversion, and unbinds resources.
     *
     * @param renderInfo Global OpenGL rendering info for the call.
     * @param drawInfo OpenGL info for this call.
     */
    void renderFrame(GlobalRenderInfo* renderInfo, DrawInfo* drawInfo);

} // namespace ShaderManager

#endif //UCAMERA_SHADERMANAGER_H
