#version 450

layout(set = 0, binding = 0) uniform sampler2D src;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

void main() {
    vec4 color = texture(src, uv);

    // TODO: well, uh... yeah.
    outColor = vec4(pow(color.rgb, vec3(2.2)), color.a);
}