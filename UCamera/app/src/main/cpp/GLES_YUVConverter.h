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

#ifndef UXR_QUESTCAMERA_GLES_YUVCONVERTER_H
#define UXR_QUESTCAMERA_GLES_YUVCONVERTER_H

#include <GLES3/gl3.h>
#include <android/surface_texture.h>

class GLES_YUVConverter {

public:
    GLES_YUVConverter(GLuint renderTexture, GLint width, GLint height);

    bool initialize(GLuint* createdSourceTexture);
    bool render(ASurfaceTexture* surfaceTexture) const;
    void dispose();

private:
    GLuint _renderTexture;
    GLuint _sourceTexture;
    GLuint _frameBufferObj;

    GLint _width; GLint _height;
    bool _disposed;

    static uint8_t s_staticReferenceHolders;

    static GLuint s_shaderProgram;
    static GLint s_shaderTransformMatrixHandle;
    static GLint s_shaderTextureSamplerHandle;

    static GLuint s_vertexBufferObj;
    static GLuint s_vertexArrayObj;

    static bool registerStaticResourceRef();
    static void deregisterStaticResourceRef();

};


#endif //UXR_QUESTCAMERA_GLES_YUVCONVERTER_H
