#version 450

layout(set = 0, binding = 0) uniform sampler2D src;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = texture(src, uv);
}