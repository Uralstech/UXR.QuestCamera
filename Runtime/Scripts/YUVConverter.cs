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

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

#if !UNITY_6000_0_OR_NEWER
using Utilities.Async;
#endif

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>Converts raw camera capture session frames to Unity-supported RGBA.</summary>
    public sealed class YUVConverter : IDisposable
    {
        private static readonly int s_shaderYBufferId = Shader.PropertyToID("YBuffer");
        private static readonly int s_shaderUBufferId = Shader.PropertyToID("UBuffer");
        private static readonly int s_shaderVBufferId = Shader.PropertyToID("VBuffer");

        private static readonly int s_shaderStrideParamsId = Shader.PropertyToID("StrideParams");

        private static readonly int s_shaderOutputTextureWidthId = Shader.PropertyToID("OutputTextureWidth");
        private static readonly int s_shaderOutputTextureHeightId = Shader.PropertyToID("OutputTextureHeight");
        private static readonly int s_shaderOutputTextureId = Shader.PropertyToID("OutputTexture");

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct StrideParams
        {
            // Row strides
            public readonly uint YRowStride;
            public readonly uint UVRowStride;

            // Pixel strides
            public readonly uint UVPixelStride;
            private readonly uint _strideParamsPadding;

            public StrideParams(uint yRowStride, uint uvRowStride, uint uvPixelStride)
            {
                YRowStride = yRowStride;
                UVRowStride = uvRowStride;
                UVPixelStride = uvPixelStride;
                _strideParamsPadding = default;
            }
        }
    
        private static readonly int s_strideParamsStructSize = Marshal.SizeOf<StrideParams>();

        /// <summary>Callback for when a frame has been dispatched for conversion, with the frame texture and capture timestamp.</summary>
        public event Action<RenderTexture, long>? OnFrameProcessed;

        /// <summary>The shader used for conversion.</summary>
        public ComputeShaderKernel ShaderKernel
        {
            get => _kernel;
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (!value.Validate())
                    throw new ArgumentException("Provided kernel is invalid.", nameof(value));

                _kernel = value;
                ConfigureCommandBuffer();
            }
        }

        /// <summary><see langword="true"/> if a capture was processed this frame; <see langword="false"/> otherwise.</summary>
        public bool HasNewFrame => _lastUpdateFrame == Time.frameCount;

        /// <summary>The output texture with converted frames.</summary>
        public readonly RenderTexture Texture;

        /// <summary>The capture timestamp of the last processed frame.</summary>
        public long CaptureTimestamp { get; private set; }

        private ComputeShaderKernel _kernel;

        private readonly CommandBuffer _commandBuffer;
        private readonly GraphicsBuffer _strideParams, _yBuffer, _uBuffer, _vBuffer;
        private readonly NativeArray<byte> _yCopyBuffer, _uCopyBuffer, _vCopyBuffer;
        private readonly int _yBufferSize, _uvBufferSize;

        private int _lastUpdateFrame;
        private int _isProcessing;
        private bool _disposed;

        private static ComputeShaderKernel GetDefaultKernel()
        {
            QuestCameraManager cameraManager = QuestCameraManager.Instance;
            return cameraManager != null
                ? cameraManager.ConversionKernel
                : throw new InvalidOperationException($"Cannot create {nameof(YUVConverter)}: no shader kernel provided and {nameof(QuestCameraManager)} is missing.");
        }

        /// <summary>Creates a new converter with the shader and kernel described in the scene instance of <see cref="QuestCameraManager"/>.</summary>
        /// <param name="textureFormat">If not specified, uses equivalent of <see cref="RenderTextureFormat.ARGB32"/>.</param>
        public YUVConverter(Resolution resolution, GraphicsFormat textureFormat = GraphicsFormat.None) : this(resolution, GetDefaultKernel(), textureFormat) { }

        /// <param name="textureFormat">If not specified, uses equivalent of <see cref="RenderTextureFormat.ARGB32"/>.</param>
        public YUVConverter(Resolution resolution, ComputeShaderKernel kernel, GraphicsFormat textureFormat = GraphicsFormat.None)
        {
            if (kernel == null)
                throw new ArgumentNullException(nameof(kernel));

            if (!kernel.Validate())
                throw new ArgumentException("Provided kernel is invalid.", nameof(kernel));

            if (textureFormat == GraphicsFormat.None)
                textureFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

#if UNITY_6000_0_OR_NEWER
            if (!SystemInfo.IsFormatSupported(textureFormat, GraphicsFormatUsage.Render))
                throw new ArgumentException($"Format {textureFormat} is not supported on device conversion.", nameof(textureFormat));
#else
            if (!SystemInfo.IsFormatSupported(textureFormat, FormatUsage.Render))
                throw new ArgumentException($"Format {textureFormat} is not supported on device conversion.", nameof(textureFormat));
#endif

            _kernel = kernel;
            Texture = new RenderTexture(resolution.width, resolution.height, 0, textureFormat)
            {
                enableRandomWrite = true
            };

            if (!Texture.Create())
                throw new UnityException("Could not create RenderTexture.");

            _yBufferSize = resolution.width * resolution.height;
            _uvBufferSize = Mathf.CeilToInt(_yBufferSize / 2f);

            _commandBuffer = new CommandBuffer();
            
            _strideParams = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, 1, s_strideParamsStructSize);

            int alignedYBufferSize = Mathf.CeilToInt(_yBufferSize / 4f);
            int alignedUVBufferSize = Mathf.CeilToInt(_uvBufferSize / 4f);

            _yBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, alignedYBufferSize, sizeof(uint));
            _uBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, alignedUVBufferSize, sizeof(uint));
            _vBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, GraphicsBuffer.UsageFlags.LockBufferForWrite, alignedUVBufferSize, sizeof(uint));

            _yCopyBuffer = new NativeArray<byte>(_yBufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _uCopyBuffer = new NativeArray<byte>(_uvBufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _vCopyBuffer = new NativeArray<byte>(_uvBufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            ConfigureCommandBuffer();
        }

        private void ConfigureCommandBuffer()
        {
            _commandBuffer.Clear();

            ComputeShader shader = _kernel.Shader;
            int kernelIdx = _kernel.Index;

            _commandBuffer.SetComputeBufferParam(shader, kernelIdx, s_shaderYBufferId, _yBuffer);
            _commandBuffer.SetComputeBufferParam(shader, kernelIdx, s_shaderUBufferId, _uBuffer);
            _commandBuffer.SetComputeBufferParam(shader, kernelIdx, s_shaderVBufferId, _vBuffer);

            _commandBuffer.SetComputeConstantBufferParam(shader, s_shaderStrideParamsId, _strideParams, 0, s_strideParamsStructSize);

            _commandBuffer.SetComputeIntParam(shader, s_shaderOutputTextureWidthId, Texture.width);
            _commandBuffer.SetComputeIntParam(shader, s_shaderOutputTextureHeightId, Texture.height);
            _commandBuffer.SetComputeTextureParam(shader, kernelIdx, s_shaderOutputTextureId, Texture);

            _commandBuffer.DispatchCompute(
                shader,
                kernelIdx,
                Mathf.CeilToInt((float)Texture.width / _kernel.ThreadGroupSizes.x),
                Mathf.CeilToInt((float)Texture.height / _kernel.ThreadGroupSizes.y),
                1
            );
        }

        /// <inheritdoc cref="ContinuousCaptureSession.OnFrameReadyCallback"/>
        public void OnFrameReady(
            IntPtr yBuffer, long yBufferSize,
            IntPtr uBuffer,
            IntPtr vBuffer, long uvBufferSize,
            int yRowStride,
            int uvRowStride,
            int uvPixelStride,
            long timestamp)
        {
            // THIS IS CALLED FROM A KOTLIN THREAD
            if (_disposed || Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 1)
                return;
                
            MemCpy(yBuffer, yBufferSize,    _yCopyBuffer);
            MemCpy(uBuffer, uvBufferSize,   _uCopyBuffer);
            MemCpy(vBuffer, uvBufferSize,   _vCopyBuffer);

            DispatchAsync(yRowStride, uvRowStride, uvPixelStride, timestamp).Forget();
        }

        private async Task DispatchAsync(int yRowStride, int uvRowStride, int uvPixelStride, long timestamp)
        {
            try
            {
#if UNITY_6000_0_OR_NEWER
                await Awaitable.MainThreadAsync();
#else
                await Awaiters.UnityMainThread;
#endif

                if (_disposed)
                    return;
                
                MemCpy(_yCopyBuffer, _yBuffer);
                MemCpy(_uCopyBuffer, _uBuffer);
                MemCpy(_vCopyBuffer, _vBuffer);

                NativeArray<StrideParams> strideParams = _strideParams.LockBufferForWrite<StrideParams>(0, 1);
                strideParams[0] = new StrideParams((uint)yRowStride, (uint)uvRowStride, (uint)uvPixelStride);

                _strideParams.UnlockBufferAfterWrite<StrideParams>(1);
                Graphics.ExecuteCommandBuffer(_commandBuffer);

                CaptureTimestamp = timestamp;
                _lastUpdateFrame = Time.frameCount;
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessing, 0);
            }

            OnFrameProcessed?.OnMainThread(Texture, timestamp).Forget();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            
            Texture.Release();
            UnityEngine.Object.Destroy(Texture);

            _commandBuffer.Dispose();
            _strideParams.Dispose();

            _yBuffer.Dispose();
            _uBuffer.Dispose();
            _vBuffer.Dispose();

            _yCopyBuffer.Dispose();
            _uCopyBuffer.Dispose();
            _vCopyBuffer.Dispose();

            GC.SuppressFinalize(this);
        }

        private static unsafe void MemCpy(IntPtr src, long len, NativeArray<byte> dst)
        {
            int copy = Mathf.Min((int)len, dst.Length);
            void* dstPtr = dst.GetUnsafePtr();

            UnsafeUtility.MemCpy(dstPtr, (void*)src, copy);
        }

        private static unsafe void MemCpy(NativeArray<byte> src, GraphicsBuffer dst)
        {
            int copy = Mathf.Min(src.Length, dst.count * dst.stride);
            void* srcPtr = src.GetUnsafeReadOnlyPtr();

            NativeArray<byte> copyArray = dst.LockBufferForWrite<byte>(0, copy);
            void* dstPtr = copyArray.GetUnsafePtr();

            UnsafeUtility.MemCpy(dstPtr, srcPtr, copy);
            dst.UnlockBufferAfterWrite<byte>(copy);
        }
    }
}
