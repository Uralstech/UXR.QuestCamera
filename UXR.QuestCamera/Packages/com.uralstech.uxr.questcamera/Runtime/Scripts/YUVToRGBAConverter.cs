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
using System.Buffers;
using System.Runtime.InteropServices;
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
                SetupShader(_shader);
            }
        }

        /// <summary>
        /// Called when a frame has been converted from YUV 4:2:0 to RGBA.
        /// Also includes the timestamp the frame was captured at in nanoseconds.
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

        /// <summary>
        /// 
        /// </summary>
        protected readonly int _threadGroupsX;
        protected readonly int _threadGroupsY;
        protected readonly int _yBufferSize;
        protected readonly int _uvBufferSize;

        private ComputeShader _shader;
        protected int _kernelHandle;

        public YUVToRGBAConverter(Resolution resolution)
        {
            if (_shader == null)
                _shader = UCameraManager.Instance.YUVToRGBAComputeShader;

            FrameRenderTexture = new RenderTexture(resolution.width, resolution.height, 0, RenderTextureFormat.ARGB32)
            {
                enableRandomWrite = true
            };

            FrameRenderTexture.Create();
            _threadGroupsX = Mathf.CeilToInt(FrameRenderTexture.width / 8.0f);
            _threadGroupsY = Mathf.CeilToInt(FrameRenderTexture.height / 8.0f);

            _yBufferSize = resolution.width * resolution.height;
            _uvBufferSize = _yBufferSize / 2;

            _yComputeBuffer = new ComputeBuffer(_yBufferSize, sizeof(byte), ComputeBufferType.Raw, ComputeBufferMode.SubUpdates);
            _uComputeBuffer = new ComputeBuffer(_uvBufferSize, sizeof(byte), ComputeBufferType.Raw, ComputeBufferMode.SubUpdates);
            _vComputeBuffer = new ComputeBuffer(_uvBufferSize, sizeof(byte), ComputeBufferType.Raw, ComputeBufferMode.SubUpdates);
            SetupShader(_shader);
        }

        /// <summary>
        /// Sets up the parameters for the given shader for YUV to RGBA conversion.
        /// </summary>
        /// <param name="shader">The shader to configure.</param>
        protected virtual void SetupShader(ComputeShader shader)
        {
            _kernelHandle = shader.FindKernel("CSMain");

            shader.SetInt(s_targetWidthID, FrameRenderTexture.width);
            shader.SetInt(s_targetHeightID, FrameRenderTexture.height);
            shader.SetTexture(_kernelHandle, s_outputTextureID, FrameRenderTexture);

            shader.SetBuffer(_kernelHandle, s_yBufferID, _yComputeBuffer);
            shader.SetBuffer(_kernelHandle, s_uBufferID, _uComputeBuffer);
            shader.SetBuffer(_kernelHandle, s_vBufferID, _vComputeBuffer);
        }

        /// <summary>
        /// Copies an array to a compute buffer. If the array is bigger than the buffer, the extra content of the array is ignored.
        /// </summary>
        /// <param name="source">The array to copy from.</param>
        /// <param name="target">The buffer to copy to.</param>
        protected static unsafe void CopyArrayToComputeBuffer(byte[] source, ComputeBuffer target)
        {
            int length = Mathf.Min(source.Length, target.count);
            Unity.Collections.NativeArray<byte> destination = target.BeginWrite<byte>(0, length);

            fixed (byte* sourcePtr = source)
            {
                fixed (byte* destinationPtr = destination.AsSpan())
                {
                    Buffer.MemoryCopy(sourcePtr, destinationPtr, length, length);
                }
            }

            target.EndWrite<byte>(length);
        }

        /// <summary>
        /// Callback for <see cref="CameraFrameForwarder"/>.
        /// </summary>
        /// <param name="yBuffer">Pointer to the buffer containing Y (luminance) data of the frame.</param>
        /// <param name="uBuffer">Pointer to the buffer containing U (color) data of the frame.</param>
        /// <param name="vBuffer">Pointer to the buffer containing V (color) data of the frame.</param>
        /// <param name="yRowStride">The size of each row of the image in <paramref name="yBuffer"/> in bytes.</param>
        /// <param name="uvRowStride">The size of each row of the image in <paramref name="uBuffer"/> and <paramref name="vBuffer"/> in bytes.</param>
        /// <param name="uvPixelStride">The size of a pixel in a row of the image in <paramref name="uBuffer"/> and <paramref name="vBuffer"/> in bytes.</param>
        /// <param name="timestamp">The timestamp the frame was captured at in nanoseconds.</param>
        public virtual void OnFrameReady(
            IntPtr yBuffer,
            IntPtr uBuffer,
            IntPtr vBuffer,
            int yRowStride,
            int uvRowStride,
            int uvPixelStride,
            long timestamp)
        {
            if (_disposed)
                return;

            byte[] yCpuBuffer = ArrayPool<byte>.Shared.Rent(_yBufferSize);
            byte[] uCpuBuffer = ArrayPool<byte>.Shared.Rent(_uvBufferSize);
            byte[] vCpuBuffer = ArrayPool<byte>.Shared.Rent(_uvBufferSize);

            try
            {
                Marshal.Copy(yBuffer, yCpuBuffer, 0, _yBufferSize);
                Marshal.Copy(uBuffer, uCpuBuffer, 0, _uvBufferSize);
                Marshal.Copy(vBuffer, vCpuBuffer, 0, _uvBufferSize);

                PrepareDataForComputeBuffer(yCpuBuffer, uCpuBuffer, vCpuBuffer,
                    yRowStride, uvRowStride, uvPixelStride, timestamp);
            }
            catch (Exception ex)
            {
                ArrayPool<byte>.Shared.Return(yCpuBuffer);
                ArrayPool<byte>.Shared.Return(uCpuBuffer);
                ArrayPool<byte>.Shared.Return(vCpuBuffer);
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Copies the given data into the shader's buffers and dispatches it.
        /// </summary>
        /// <param name="yCpuBuffer">Array containing Y (luminance) data of the frame.</param>
        /// <param name="uCpuBuffer">Array containing U (color) data of the frame.</param>
        /// <param name="vCpuBuffer">Array containing V (color) data of the frame.</param>
        /// <param name="yRowStride">The size of each row of the image in <see cref="_yComputeBuffer"/> in bytes.</param>
        /// <param name="uvRowStride">The size of each row of the image in <see cref="_uComputeBuffer"/> and <see cref="_vComputeBuffer"/> in bytes.</param>
        /// <param name="uvPixelStride">The size of a pixel in a row of the image in <see cref="_uComputeBuffer"/> and <see cref="_vComputeBuffer"/> in bytes.</param>
        /// <param name="timestamp">The timestamp the frame was captured at in nanoseconds.</param>
        protected virtual async void PrepareDataForComputeBuffer(byte[] yCpuBuffer, byte[] uCpuBuffer, byte[] vCpuBuffer,
            int yRowStride, int uvRowStride, int uvPixelStride, long timestamp)
        {
            try
            {
#if UNITY_6000_0_OR_NEWER
                await Awaitable.MainThreadAsync();
#else
                await Awaiters.UnityMainThread;
#endif
                if (_disposed || _shader == null)
                    return;

                CopyArrayToComputeBuffer(yCpuBuffer, _yComputeBuffer);
                CopyArrayToComputeBuffer(uCpuBuffer, _uComputeBuffer);
                CopyArrayToComputeBuffer(vCpuBuffer, _vComputeBuffer);

                _shader.SetInt(s_yRowStrideID, yRowStride);
                _shader.SetInt(s_uvRowStrideID, uvRowStride);
                _shader.SetInt(s_uvPixelStrideID, uvPixelStride);
                _shader.Dispatch(_kernelHandle, _threadGroupsX, _threadGroupsY, 1);

                FrameCaptureTimestamp = timestamp;
                OnFrameProcessed?.Invoke(FrameRenderTexture, timestamp);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(yCpuBuffer);
                ArrayPool<byte>.Shared.Return(uCpuBuffer);
                ArrayPool<byte>.Shared.Return(vCpuBuffer);
            }
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
            _yComputeBuffer.Release();
            _uComputeBuffer.Release();
            _vComputeBuffer.Release();
        }
    }
}