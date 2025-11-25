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

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

#if !UNITY_6000_0_OR_NEWER
using Utilities.Async;
#endif

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// The default YUV 4:2:0 to RGBA converter that uses a compute shader to convert the camera texture to RGBA.
    /// </summary>
    public class YUVToRGBAConverter : IDisposable
    {
        /// <summary>
        /// Handles YUV frame data.
        /// </summary>
        /// <remarks>
        /// The NativeArrays used by this object are allocated using <see cref="Allocator.TempJob"/>.
        /// </remarks>
        protected record CPUDepthFrame : IDisposable
        {
            /// <summary>
            /// Represents YUV frame data on the CPU.
            /// </summary>
            public readonly NativeArray<byte> YBuffer, UBuffer, VBuffer;

            private readonly object _lock = new();
            private bool _disposed;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static long Min(long a, long b) => a < b ? a : b;

            public CPUDepthFrame(int yBufferSize, int uvBufferSize)
            {
                YBuffer = new NativeArray<byte>(yBufferSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                UBuffer = new NativeArray<byte>(uvBufferSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                VBuffer = new NativeArray<byte>(uvBufferSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            }

            /// <summary>
            /// Copies YUV data from native pointers.
            /// </summary>
            /// <param name="yNativeBuffer">The Y channel data.</param>
            /// <param name="yLength">The length of the Y channel data in bytes.</param>
            /// <param name="uNativeBuffer">The U channel data.</param>
            /// <param name="vNativeBuffer">The V channel data.</param>
            /// <param name="uvLength">The length of the U and V channel data in bytes.</param>
            public unsafe void CopyFrom(IntPtr yNativeBuffer, long yLength, IntPtr uNativeBuffer, IntPtr vNativeBuffer, long uvLength)
            {
                lock (_lock)
                {
                    Buffer.MemoryCopy((void*)yNativeBuffer, YBuffer.GetUnsafePtr(), YBuffer.Length, Min(YBuffer.Length, yLength));
                    Buffer.MemoryCopy((void*)uNativeBuffer, UBuffer.GetUnsafePtr(), UBuffer.Length, Min(UBuffer.Length, uvLength));
                    Buffer.MemoryCopy((void*)vNativeBuffer, VBuffer.GetUnsafePtr(), VBuffer.Length, Min(VBuffer.Length, uvLength));
                }
            }

            /// <summary>
            /// Copies this data to ComputeBuffers.
            /// </summary>
            /// <param name="yComputeBuffer">The Y channel buffer.</param>
            /// <param name="uComputeBuffer">The U channel buffer.</param>
            /// <param name="vComputeBuffer">The V channel buffer.</param>
            public void CopyTo(ComputeBuffer yComputeBuffer, ComputeBuffer uComputeBuffer, ComputeBuffer vComputeBuffer)
            {
                lock (_lock)
                {
                    yComputeBuffer.SetData(YBuffer);
                    uComputeBuffer.SetData(UBuffer);
                    vComputeBuffer.SetData(VBuffer);
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (_lock)
                {
                    if (_disposed)
                        return;

                    YBuffer.Dispose();
                    UBuffer.Dispose();
                    VBuffer.Dispose();
                    GC.SuppressFinalize(this);
                    _disposed = true;
                }
            }
        }

        private static readonly int s_yBufferID = UnityEngine.Shader.PropertyToID("YBuffer");
        private static readonly int s_uBufferID = UnityEngine.Shader.PropertyToID("UBuffer");
        private static readonly int s_vBufferID = UnityEngine.Shader.PropertyToID("VBuffer");

        private static readonly int s_yRowStrideID = UnityEngine.Shader.PropertyToID("YRowStride");
        private static readonly int s_uvRowStrideID = UnityEngine.Shader.PropertyToID("UVRowStride");
        private static readonly int s_uvPixelStrideID = UnityEngine.Shader.PropertyToID("UVPixelStride");

        private static readonly int s_targetWidthID = UnityEngine.Shader.PropertyToID("TargetWidth");
        private static readonly int s_targetHeightID = UnityEngine.Shader.PropertyToID("TargetHeight");

        private static readonly int s_outputTextureID = UnityEngine.Shader.PropertyToID("OutputTexture");

        /// <summary>
        /// The RenderTexture which will contain the RGBA camera frames.
        /// </summary>
        public RenderTexture FrameRenderTexture { get; protected set; }

        /// <summary>
        /// The timestamp the last frame processed was captured at in nanoseconds.
        /// </summary>
        public long FrameCaptureTimestamp { get; protected set; }

        /// <summary>
        /// The shader used to convert YUV 4:2:0 to an RGBA RenderTexture.
        /// Uses <see cref="UCameraManager.YUVToRGBAComputeShader"/> if not specified here.
        /// </summary>
        public ComputeShader Shader
        {
            get => _shader;
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (_shader == value)
                    return;

                _shader = value;
                _kernelHandle = _shader.FindKernel("CSMain");
            }
        }

        /// <summary>
        /// Called when a capture is dispatched for conversion to RGBA, with the capture's timestamp in nanoseconds.
        /// </summary>
        public event Action<RenderTexture, long>? OnFrameProcessed;

        /// <summary>
        /// Buffer containing Y (luminance) data of the frame being processed.
        /// </summary>
        protected readonly ComputeBuffer _yComputeBuffer;

        /// <summary>
        /// Buffer containing U (color) data of the frame being processed.
        /// </summary>
        protected readonly ComputeBuffer _uComputeBuffer;

        /// <summary>
        /// Buffer containing V (color) data of the frame being processed.
        /// </summary>
        protected readonly ComputeBuffer _vComputeBuffer;

        protected readonly int _threadGroupsX;
        protected readonly int _threadGroupsY;
        protected readonly int _yBufferSize;
        protected readonly int _uvBufferSize;

        private ComputeShader _shader;
        protected int _kernelHandle;

        public YUVToRGBAConverter(Resolution resolution)
        {
            if (_shader == null)
            {
                _shader = UCameraManager.Instance.YUVToRGBAComputeShader;
                _kernelHandle = _shader.FindKernel("CSMain");
            }

            FrameRenderTexture = new RenderTexture(resolution.width, resolution.height, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true
            };

            FrameRenderTexture.Create();
            _threadGroupsX = Mathf.CeilToInt(FrameRenderTexture.width / 8.0f);
            _threadGroupsY = Mathf.CeilToInt(FrameRenderTexture.height / 8.0f);

            _yBufferSize = resolution.width * resolution.height;
            _uvBufferSize = Mathf.CeilToInt(_yBufferSize / 2f);

            _yComputeBuffer = new ComputeBuffer(_yBufferSize, sizeof(byte), ComputeBufferType.Raw, ComputeBufferMode.SubUpdates);
            _uComputeBuffer = new ComputeBuffer(_uvBufferSize, sizeof(byte), ComputeBufferType.Raw, ComputeBufferMode.SubUpdates);
            _vComputeBuffer = new ComputeBuffer(_uvBufferSize, sizeof(byte), ComputeBufferType.Raw, ComputeBufferMode.SubUpdates);
        }

        /// <summary>
        /// Returns the next frame to be received by this processor.
        /// </summary>
        /// <returns>The frame's <see cref="RenderTexture"/> and capture timestamp, in nanoseconds..</returns>
        public async Task<(RenderTexture, long)> GetNextFrameAsync(CancellationToken token = default)
        {
            ThrowIfDisposed();

            TaskCompletionSource<(RenderTexture, long)> tcs = new();
            void OnFrameReceived(RenderTexture texture, long timestamp) => tcs.SetResult((texture, timestamp));

            OnFrameProcessed += OnFrameReceived;

            try
            {
                using CancellationTokenRegistration _ = token.Register(tcs.SetCanceled);
                return await tcs.Task;
            }
            finally
            {
                OnFrameProcessed -= OnFrameReceived;
            }
        }

        /// <summary>
        /// Processes a frame received from the native capture session.
        /// </summary>
        /// <param name="yBuffer">The pointer to this frame's Y (luminance) data.</param>
        /// <param name="yBufferSize">The size of the Y buffer in bytes.</param>
        /// <param name="uBuffer">The pointer to this frame's U (color) data.</param>
        /// <param name="vBuffer">The pointer to this frame's V (color) data.</param>
        /// <param name="uvBufferSize">The size of the U and V buffers in bytes.</param>
        /// <param name="yRowStride">The size of each row of the image in <paramref name="yBuffer"/> in bytes.</param>
        /// <param name="uvRowStride">The size of each row of the image in <paramref name="uBuffer"/> and <paramref name="vBuffer"/> in bytes.</param>
        /// <param name="uvPixelStride">The size of a pixel in a row of the image in <paramref name="uBuffer"/> and <paramref name="vBuffer"/> in bytes.</param>
        /// <param name="timestamp">The timestamp the frame was captured at in nanoseconds.</param>
        public virtual void OnFrameReady(
            IntPtr yBuffer, long yBufferSize,
            IntPtr uBuffer,
            IntPtr vBuffer, long uvBufferSize,
            int yRowStride,
            int uvRowStride,
            int uvPixelStride,
            long timestamp)
        {
            if (_disposed)
                return;

            CPUDepthFrame cpuFrame = new(_yBufferSize, _uvBufferSize);

            try
            {
                cpuFrame.CopyFrom(yBuffer, yBufferSize, uBuffer, vBuffer, uvBufferSize);
                _ = PrepareDataForComputeBuffer(cpuFrame, yRowStride, uvRowStride, uvPixelStride, timestamp)
                    .ContinueWith(static (_, frame) => ((CPUDepthFrame)frame).Dispose(), cpuFrame);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                cpuFrame.Dispose();
            }
        }

        /// <summary>
        /// Copies the given data into the shader's buffers and dispatches it.
        /// </summary>
        /// <param name="frame">The frame data on the CPU.</param>
        /// <param name="yRowStride">The size of each row of the image in <see cref="_yComputeBuffer"/> in bytes.</param>
        /// <param name="uvRowStride">The size of each row of the image in <see cref="_uComputeBuffer"/> and <see cref="_vComputeBuffer"/> in bytes.</param>
        /// <param name="uvPixelStride">The size of a pixel in a row of the image in <see cref="_uComputeBuffer"/> and <see cref="_vComputeBuffer"/> in bytes.</param>
        /// <param name="timestamp">The timestamp the frame was captured at in nanoseconds.</param>
        protected virtual async Task PrepareDataForComputeBuffer(CPUDepthFrame frame, int yRowStride, int uvRowStride, int uvPixelStride, long timestamp)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#else
            await Awaiters.UnityMainThread;
#endif
            if (_disposed || _shader == null)
                return;

            frame.CopyTo(_yComputeBuffer, _uComputeBuffer, _vComputeBuffer);

            _shader.SetInt(s_targetWidthID, FrameRenderTexture.width);
            _shader.SetInt(s_targetHeightID, FrameRenderTexture.height);
            _shader.SetTexture(_kernelHandle, s_outputTextureID, FrameRenderTexture);

            _shader.SetBuffer(_kernelHandle, s_yBufferID, _yComputeBuffer);
            _shader.SetBuffer(_kernelHandle, s_uBufferID, _uComputeBuffer);
            _shader.SetBuffer(_kernelHandle, s_vBufferID, _vComputeBuffer);

            _shader.SetInt(s_yRowStrideID, yRowStride);
            _shader.SetInt(s_uvRowStrideID, uvRowStride);
            _shader.SetInt(s_uvPixelStrideID, uvPixelStride);
            _shader.Dispatch(_kernelHandle, _threadGroupsX, _threadGroupsY, 1);

            FrameCaptureTimestamp = timestamp;
            OnFrameProcessed?.Invoke(FrameRenderTexture, timestamp);
        }

        private bool _disposed = false;

        /// <summary>
        /// Releases the frame RenderTexture and buffers.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            FrameRenderTexture.Release();
            UnityEngine.Object.Destroy(FrameRenderTexture);

            _yComputeBuffer.Dispose();
            _uComputeBuffer.Dispose();
            _vComputeBuffer.Dispose();
            GC.SuppressFinalize(this);
        }

        ~YUVToRGBAConverter()
        {
            Debug.LogWarning(
                $"A {nameof(YUVToRGBAConverter)} object was finalized by the GC without being properly disposed.\n" +
                "Its RenderTexture was NOT released — call Dispose() on the main thread."
            );
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(YUVToRGBAConverter));
        }
    }
}