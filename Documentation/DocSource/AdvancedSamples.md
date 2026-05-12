# Advanced Samples

This page contains some samples for advanced use-cases, like custom texture converters or multi-camera streaming.

## Custom Texture Converters

The texture converter in `CapturePipeline<T>.Converter` allows you to easily change the conversion compute shader to custom
ones. All you have to do is assign a new `ComputeShaderKernel` to `Converter.ShaderKernel`. `ComputeShaderKernel` references
the compute shader and the name of the shader kernel to use.

You can also change the compute shader for all new capture sessions by changing `QuestCameraManager.ConversionKernel`.

For example, the following compute shader ignores the U and V values of the YUV stream to provide a Luminance-only image:

```hlsl
#pragma kernel CSMain

// ---------------- YUVConverter *required* parameters ----------------

// Camera frame
ByteAddressBuffer YBuffer;
ByteAddressBuffer UBuffer;
ByteAddressBuffer VBuffer;

cbuffer StrideParams {
    // Row strides
    uint YRowStride;
    uint UVRowStride;

    // Pixel strides
    uint UVPixelStride;
    uint StrideParamsPadding;
}

uniform uint OutputTextureWidth;
uniform uint OutputTextureHeight;
RWTexture2D<float4> OutputTexture;

// ----------------     End of required parameters     ----------------

// Converts byte array indexing to ByteAddressBuffer indexing and returns the value
uint GetByteFromBuffer(const ByteAddressBuffer buffer, const uint byteIndex) {
    
    const uint word = buffer.Load(byteIndex & ~3);
    const uint byteInWord = byteIndex & 3;

    return (word >> (byteInWord * 8)) & 0xFF;
}

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID) {

    if (id.x >= OutputTextureWidth || id.y >= OutputTextureHeight)
        return;

    // The YUV stream is flipped, so we have to un-flip it.
    const uint flippedY = OutputTextureHeight - 1 - id.y;

    // Index of Y value in buffer.
    const uint yIndex = flippedY * YRowStride + id.x;

    // Get the Y (luminance) value.
    const uint y = GetByteFromBuffer(YBuffer, yIndex);
    
    // Convert the value from 0-255 to 0-1 and set the output texture.
    const float3 color = float3(y, y, y) / 255.0;
    OutputTexture[id.xy] = float4(color, 1.0);
}
```

## Multiple Streams from One Camera

By adding multiple texture converters to the same request, you can emulate the effect of having more than one image stream from a
single camera. For example, you can have one converter stream the camera image as-is, and another streaming with a simple Sepia
post-processing effect:

> [!WARNING]
> This is not the best way to do this! Creating multiple `YUVConverter`s duplicates almost all conversion resources for each instance!
>
> Each `YUVConverter` allocates its own:
> - CPU-side `NativeArray` copy buffers
> - GPU-side `GraphicsBuffer`s
> - Output `RenderTexture`
> - Conversion `CommandBuffer`
>
> The CPU-side copy buffers and GPU-side graphics buffers are each approximately the size of one frame of
> YUV image data. The output `RenderTexture` size depends on the selected texture format, but is usually
> larger than an equivalent YUV texture.
>
> Please implement a custom converter which shares the above data between different consumers.

```csharp
// Create the session.
_session = _camera.CreateContinuousSession(highestResolution, CaptureTemplate.Preview, useCase);
if (!await _session.WaitForInitializationAsync())
{
    await _session.DisposeAsync();
    await _camera.DisposeAsync();
    return;
}

// Primary converter, defaults to the shader set in QuestCameraManager.
_primaryConverter = new YUVConverter(highestResolution);

// Secondary converter with the sepia effect, ComputeShaderKernel uses "CSMain" by default.
_secondaryConverter = new YUVConverter(highestResolution, new ComputeShaderKernel(_sepiaShader));

// Register converters to session.
_session.NativeProxy.OnFrameReady += _primaryConverter.OnFrameReady;
_session.NativeProxy.OnFrameReady += _secondaryConverter.OnFrameReady;

_rawImagePrimary.texture = _primaryConverter.Texture;
_rawImageSecondary.texture = _secondaryConverter.Texture;
```

### YUV To RGBA Converter With Sepia Effect

```hlsl
#pragma kernel CSMain

ByteAddressBuffer YBuffer;
ByteAddressBuffer UBuffer;
ByteAddressBuffer VBuffer;

cbuffer StrideParams {
    uint YRowStride;
    uint UVRowStride;

    uint UVPixelStride;
    uint StrideParamsPadding;
}

uniform uint OutputTextureWidth;
uniform uint OutputTextureHeight;
RWTexture2D<float4> OutputTexture;

uint GetByteFromBuffer(const ByteAddressBuffer buffer, const uint byteIndex) {
    
    const uint word = buffer.Load(byteIndex & ~3);
    const uint byteInWord = byteIndex & 3;

    return (word >> (byteInWord * 8)) & 0xFF;
}

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID) {

    if (id.x >= OutputTextureWidth || id.y >= OutputTextureHeight)
        return;

    const uint flippedY = OutputTextureHeight - 1 - id.y;
    const uint yIndex = flippedY * YRowStride + id.x;

    const uint y = GetByteFromBuffer(YBuffer, yIndex);
    const float3 color = float3(y, y, y) / 255.0;

    float3 sepiaColor;
    sepiaColor.r = dot(color.rgb, float3(0.393, 0.769, 0.189));
    sepiaColor.g = dot(color.rgb, float3(0.349, 0.686, 0.168));
    sepiaColor.b = dot(color.rgb, float3(0.272, 0.534, 0.131));

    OutputTexture[id.xy] = float4(sepiaColor, 1.0);
}
```
