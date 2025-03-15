# Advanced Samples

This page contains some samples for advanced use-cases, like custom texture converters or multi-camera streaming.

## Custom Texture Converters

The texture converter in `CaptureSessionObject.TextureConverter` allows you to easily change the conversion compute shader to custom
ones. All you have to do is set `CaptureSessionObject.TextureConverter.Shader` to your shader. You can also change the compute shader
for all new capture sessions by changing `UCameraManager.YUVToRGBAComputeShader`.

For example, the following compute shader ignores the U and V values of the YUV stream to provide a Luminance-only image:

```hlsl
#pragma kernel CSMain

// Input buffers (read-only)
ByteAddressBuffer YBuffer;
ByteAddressBuffer UBuffer;
ByteAddressBuffer VBuffer;

// Row strides
uint YRowStride;
uint UVRowStride;

// Pixel strides
uint UVPixelStride;

// Image dimensions
uint TargetWidth;
uint TargetHeight;

// Output texture (read-write)
RWTexture2D<float4> OutputTexture;

// Helper function to get a byte from a ByteAddressBuffer.
//  buffer: The ByteAddressBuffer.
//  byteIndex: The *byte* index (offset) into the buffer.
uint GetByteFromBuffer(ByteAddressBuffer buffer, uint byteIndex)
{
    // Calculate the 32-bit word offset (each word is 4 bytes).
    uint wordOffset = byteIndex / 4;

    // Load the 32-bit word containing the byte.
    uint word = buffer.Load(wordOffset * 4); // MUST multiply by 4 for ByteAddressBuffer.Load()

    // Calculate the byte position *within* the word (0, 1, 2, or 3).
    uint byteInWord = byteIndex % 4;

    // Extract the correct byte using bit shifts and masking.
    return (word >> (byteInWord * 8)) & 0xFF;
}

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= TargetWidth || id.y >= TargetHeight)
        return;
    
    // The YUV stream is flipped, so we have to un-flip it.
    uint flippedY = TargetHeight - 1 - id.y;

    // Index of Y value in buffer.
    uint yIndex = flippedY * YRowStride + id.x;
    uint yValue = GetByteFromBuffer(YBuffer, yIndex);
    
    float3 luminance = float3(yValue, yValue, yValue) / 255.0;
    OutputTexture[id.xy] = float4(luminance.rgb, 1.0);
}
```

## Multiple Streams From One Camera

By adding multiple texture converters to the same request, you can emulate the effect of having more than one image stream from a
single camera. For example, you can have one converter stream the camera image as-is, and another streaming with a simple Sepia
post-processing effect:

```csharp
// Create a capture session with the camera, at the chosen resolution.
CameraDevice.CaptureSessionObject sessionObject = camera.CreateCaptureSession(resolution);
yield return sessionObject.CaptureSession.WaitForInitialization();

// Check if it opened successfully.
if (sessionObject.CaptureSession.CurrentState...

// Set the image texture.
_rawImage.texture = sessionObject.TextureConverter.FrameRenderTexture;

// Create a new YUVToRGBAConverter to the current GameObject.
YUVToRGBAConverter secondary = gameObject.AddComponent<YUVToRGBAConverter>();

// Assign it a different shader.
secondary.Shader = _postProcessShader;

// Setup the camera forwarder, which will forward the camera frames in native memory to the converter.
secondary.SetupCameraFrameForwarder(sessionObject.TextureConverter.CameraFrameForwarder, resolution);

// Set the second image to the post processed RenderTexture.
_rawImagePostProcessed.texture = secondary.FrameRenderTexture;
```

### YUV To RGBA Converter With Sepia Effect

```hlsl
#pragma kernel CSMain

// Input buffers (read-only)
ByteAddressBuffer YBuffer;
ByteAddressBuffer UBuffer;
ByteAddressBuffer VBuffer;

// Row strides
uint YRowStride;
uint UVRowStride;

// Pixel strides
uint UVPixelStride;

// Image dimensions
uint TargetWidth;
uint TargetHeight;

// Output texture (read-write)
RWTexture2D<float4> OutputTexture;

// Helper function to get a byte from a ByteAddressBuffer.
//  buffer: The ByteAddressBuffer.
//  byteIndex: The *byte* index (offset) into the buffer.
uint GetByteFromBuffer(ByteAddressBuffer buffer, uint byteIndex)
{
    // Calculate the 32-bit word offset (each word is 4 bytes).
    uint wordOffset = byteIndex / 4;

    // Load the 32-bit word containing the byte.
    uint word = buffer.Load(wordOffset * 4); // MUST multiply by 4 for ByteAddressBuffer.Load()

    // Calculate the byte position *within* the word (0, 1, 2, or 3).
    uint byteInWord = byteIndex % 4;

    // Extract the correct byte using bit shifts and masking.
    return (word >> (byteInWord * 8)) & 0xFF;
}

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= TargetWidth || id.y >= TargetHeight)
        return;
    
    // The YUV stream is flipped, so we have to un-flip it.
    uint flippedY = TargetHeight - 1 - id.y;

    // Index of Y value in buffer.
    uint yIndex = flippedY * YRowStride + id.x;
    uint yValue = GetByteFromBuffer(YBuffer, yIndex);
    
    float3 luminance = float3(yValue, yValue, yValue) / 255.0;

    // --- Post-processing (Sepia Tone) ---
    float4 color = float4(luminance.rgb, 1.0);

    //Simple Sepia.  Could also do a vignette, bloom, etc. here.
    float4 sepiaColor;
    sepiaColor.r = dot(color.rgb, float3(0.393, 0.769, 0.189));
    sepiaColor.g = dot(color.rgb, float3(0.349, 0.686, 0.168));
    sepiaColor.b = dot(color.rgb, float3(0.272, 0.534, 0.131));
    sepiaColor.a = 1.0;

    OutputTexture[id.xy] = sepiaColor;
}
```
