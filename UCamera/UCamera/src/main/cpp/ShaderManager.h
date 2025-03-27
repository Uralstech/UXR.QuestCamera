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
     * @brief Holds the necessary OpenGL object IDs and state for rendering.
     */
    struct RenderInfo {
        GLuint program = 0;
        GLuint vbo = 0;
        GLuint ebo = 0;
        GLuint vao = 0;
        GLuint fbo = 0;

        GLint textureUniformLocation = -1;
        GLint resolutionUniformLocation = -1;
    };

    /**
     * @brief Sets up the OpenGL rendering pipeline, including an FBO.
     *
     * Compiles shaders, links program, creates geometry (VBO/VAO),
     * generates a source texture, finds uniforms, and creates an FBO.
     *
     * @param output Pointer to a RenderInfo struct to be populated.
     * @return True if setup was successful, false otherwise.
     */
    bool setupGraphics(RenderInfo* output);

    /**
     * @brief Renders (copies) the sourceTextureId into the targetTextureId using an FBO.
     *
     * Binds the FBO, attaches the targetTextureId, sets viewport,
     * uses the shader program, binds the sourceTextureId (external)
     * for sampling, draws the quad, and unbinds resources.
     *
     * @param renderInfo Pointer to the RenderInfo struct.
     * @param sourceTextureId The OpenGL texture ID of the source EXTERNAL texture.
     *                        This texture is typically managed externally (e.g., from SurfaceTexture).
     * @param targetTextureId The OpenGL texture ID of the destination texture (GL_TEXTURE_2D).
     *                        This texture MUST be properly allocated (e.g., via glTexImage2D)
     *                        by the caller beforehand.
     * @param targetWidth The width of the target texture.
     * @param targetHeight The height of the target texture.
     */
    void renderFrame(RenderInfo *renderInfo, GLuint sourceTextureId, GLuint targetTextureId, int targetWidth, int targetHeight);

    /**
     * @brief Cleans up OpenGL resources.
     *
     * Deletes the shader program, VBO, VAO, source texture, and FBO.
     *
     * @param renderInfo Pointer to the RenderInfo struct whose resources should be cleaned up.
     */
    void cleanupGraphics(RenderInfo* renderInfo);

} // namespace ShaderManager

#endif //UCAMERA_SHADERMANAGER_H
