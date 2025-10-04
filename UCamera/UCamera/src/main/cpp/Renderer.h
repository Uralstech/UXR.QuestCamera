//
// Created by celeste on 03/10/25.
//

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
