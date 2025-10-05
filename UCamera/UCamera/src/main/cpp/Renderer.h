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

#ifndef UCAMERA_RENDERER_H
#define UCAMERA_RENDERER_H

#include <GLES3/gl3.h>
#include <android/surface_texture.h>

class Renderer {
public:
    Renderer(GLuint unityTexture, GLint width, GLint height);
    bool initialize(GLuint* texture);
    bool render(ASurfaceTexture* surfaceTexture) const;
    void dispose();

private:
    GLuint _unityTexture;
    GLuint _sourceTexture;
    GLuint _frameBufferObject;

    GLint _width; GLint _height;
    bool _disposed;

    static uint8_t _staticReferenceHolders;

    static GLuint _shaderProgram;
    static GLint _transformMatrixHandle;
    static GLint _textureSamplerHandle;

    static GLuint _vertexBufferObject;
    static GLuint _vertexArrayObject;

    static bool hasGlErrors(const char* methodName);
    static bool linkShaderProgram(GLuint vertexShader, GLuint fragmentShader);
    static bool compileShader(GLenum type, const char* source, GLuint* shader);
    static bool setupGeometry();
};


#endif //UCAMERA_RENDERER_H
