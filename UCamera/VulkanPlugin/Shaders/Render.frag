#version 450

layout(set = 0, binding = 0) uniform sampler2D src;

layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 outColor;

// Inverse transfer function for SMPTE 170M, formulae from:
// https://www.kernel.org/doc/Documentation/media/uapi/v4l/colorspaces-details.rst
// Adjusted for 0-1 input range.

const float inverseExponent = 1.0 / 0.45;

float smpte170mInverseTransfer(float val) {

    return val >= 0.081
        ? pow((val + 0.099) / 1.099, inverseExponent)
        : val / 4.5;
}

void main() {
    vec4 color = texture(src, uv);

    outColor = vec4(
        smpte170mInverseTransfer(color.r),
        smpte170mInverseTransfer(color.g),
        smpte170mInverseTransfer(color.b),
        1.0
    );
}