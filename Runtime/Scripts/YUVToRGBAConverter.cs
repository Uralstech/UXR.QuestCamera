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
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

#if !UNITY_6000_0_OR_NEWER && UTILITIES_ASYNC
using Utilities.Async;
#endif

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// The default YUV 4:2:0 to RGBA converter that uses a compute shader to convert the camera texture to RGBA.
    /// </summary>
    public class YUVToRGBAConverter : MonoBehaviour
    {
        /// <summary>
        /// The native camera frame forwarder.
        /// </summary>
        public CameraFrameForwarder CameraFrameForwarder { get; protected set; }

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
        public ComputeShader Shader;

        /// <summary>
        /// Called when a frame has been converted from YUV 4:2:0 to RGBA.
        /// </summary>
        public UnityEvent<RenderTexture> OnFrameProcessed = new();

        /// <summary>
        /// Pointer to the buffer containing Y (luminance) data of the frame being processed.
        /// </summary>
        protected ComputeBuffer _yComputeBuffer;

        /// <summary>
        /// Pointer to the buffer containing U (color) data of the frame being processed.
        /// </summary>
        protected ComputeBuffer _uComputeBuffer;

        /// <summary>
        /// Pointer to the buffer containing V (color) data of the frame being processed.
        /// </summary>
        protected ComputeBuffer _vComputeBuffer;

        /// <summary>
        /// Have the converter's resources been released?
        /// </summary>
#pragma warning disable IDE1006 // Naming Styles
        protected bool _isReleased { get; private set; } = false;
#pragma warning restore IDE1006 // Naming Styles

        /// <summary>
        /// Copies native (unmanaged) byte data to a compute buffer.
        /// </summary>
        /// <param name="computeBuffer">The buffer to copy to.</param>
        /// <param name="nativeBufferPtr">The memory to copy from.</param>
        /// <param name="nativeBufferSize">The number of bytes to copy.</param>
        protected static unsafe void CopyNativeDataToComputeBuffer(ref ComputeBuffer computeBuffer, IntPtr nativeBufferPtr, int nativeBufferSize)
        {
            if (computeBuffer is null || !computeBuffer.IsValid() || computeBuffer.count < nativeBufferSize)
            {
                computeBuffer?.Release();
                computeBuffer = new ComputeBuffer(nativeBufferSize, sizeof(byte), ComputeBufferType.Raw, ComputeBufferMode.SubUpdates);
            }

            Unity.Collections.NativeArray<byte> destination = computeBuffer.BeginWrite<byte>(0, nativeBufferSize);
            fixed (byte* destinationPointer = destination.AsSpan())
            {
                Buffer.MemoryCopy(nativeBufferPtr.ToPointer(), destinationPointer, nativeBufferSize, nativeBufferSize);
            }

            computeBuffer.EndWrite<byte>(nativeBufferSize);
        }

        protected void Awake()
        {
            if (Shader == null)
                Shader = UCameraManager.Instance.YUVToRGBAComputeShader;
        }

        protected void OnDestroy()
        {
            Release();
        }

        /// <summary>
        /// Sets the camera frame forwarder.
        /// </summary>
        public virtual void SetupCameraFrameForwarder(CameraFrameForwarder cameraFrameForwarder, Resolution textureResolution)
        {
            CameraFrameForwarder = cameraFrameForwarder;
            CameraFrameForwarder.OnFrameReady += OnFrameReady;

            if (FrameRenderTexture == null
                || FrameRenderTexture.width != textureResolution.width
                || FrameRenderTexture.height != textureResolution.height)
            {
                if (FrameRenderTexture != null)
                    FrameRenderTexture.Release();

                FrameRenderTexture = new RenderTexture(textureResolution.width, textureResolution.height, 0, RenderTextureFormat.ARGB32)
                {
                    enableRandomWrite = true
                };

                FrameRenderTexture.Create();
            }
        }

        /// <summary>
        /// Releases the ComputeBuffers and RenderTextures associated with this converter.
        /// </summary>
        public void Release()
        {
            _isReleased = true;
            if (CameraFrameForwarder is not null)
            {
                CameraFrameForwarder.OnFrameReady -= OnFrameReady;
                CameraFrameForwarder = null;
            }

            if (FrameRenderTexture != null)
            {
                FrameRenderTexture.Release();
                FrameRenderTexture = null;
            }

            _yComputeBuffer?.Release();
            _yComputeBuffer = null;

            _uComputeBuffer?.Release();
            _uComputeBuffer = null;

            _vComputeBuffer?.Release();
            _vComputeBuffer = null;
        }

        /// <summary>
        /// Callback for <see cref="CameraFrameForwarder"/>.
        /// </summary>
        /// <param name="yBuffer">Pointer to the buffer containing Y (luminance) data of the frame.</param>
        /// <param name="uBuffer">Pointer to the buffer containing U (color) data of the frame.</param>
        /// <param name="vBuffer">Pointer to the buffer containing V (color) data of the frame.</param>
        /// <param name="ySize">The size of <paramref name="yBuffer"/>.</param>
        /// <param name="uSize">The size of <paramref name="uBuffer"/>.</param>
        /// <param name="vSize">The size of <paramref name="vBuffer"/>.</param>
        /// <param name="yRowStride">The size of each row of the image in <paramref name="yBuffer"/> in bytes.</param>
        /// <param name="uvRowStride">The size of each row of the image in <paramref name="uBuffer"/> and <paramref name="vBuffer"/> in bytes.</param>
        /// <param name="uvPixelStride">The size of a pixel in a row of the image in <paramref name="uBuffer"/> and <paramref name="vBuffer"/> in bytes.</param>
        /// <param name="timestamp">The timestamp the frame was captured at in nanoseconds.</param>
        protected virtual async Task OnFrameReady(
            IntPtr yBuffer,
            IntPtr uBuffer,
            IntPtr vBuffer,
            int ySize,
            int uSize,
            int vSize,
            int yRowStride,
            int uvRowStride,
            int uvPixelStride,
            long timestamp)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#elif UTILITIES_ASYNC
            await Awaiters.UnityMainThread;
#endif
            if (_isReleased)
                return;

            CopyNativeDataToComputeBuffer(ref _yComputeBuffer, yBuffer, ySize);
            CopyNativeDataToComputeBuffer(ref _uComputeBuffer, uBuffer, uSize);
            CopyNativeDataToComputeBuffer(ref _vComputeBuffer, vBuffer, vSize);
            SendFrameToComputeBuffer(yRowStride, uvRowStride, uvPixelStride);
            FrameCaptureTimestamp = timestamp;
        }

        /// <summary>
        /// Sends the camera frame stored in the compute buffers to the compute shader and dispatches it.
        /// </summary>
        /// <param name="yRowStride">The size of each row of the image in <see cref="_yComputeBuffer"/> in bytes.</param>
        /// <param name="uvRowStride">The size of each row of the image in <see cref="_uComputeBuffer"/> and <see cref="_vComputeBuffer"/> in bytes.</param>
        /// <param name="uvPixelStride">The size of a pixel in a row of the image in <see cref="_uComputeBuffer"/> and <see cref="_vComputeBuffer"/> in bytes.</param>
        protected virtual async void SendFrameToComputeBuffer(int yRowStride, int uvRowStride, int uvPixelStride)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#elif UTILITIES_ASYNC
            await Awaiters.UnityMainThread;
#endif
            if (_isReleased)
                return;

            int kernelHandle = Shader.FindKernel("CSMain");

            Shader.SetBuffer(kernelHandle, "YBuffer", _yComputeBuffer);
            Shader.SetBuffer(kernelHandle, "UBuffer", _uComputeBuffer);
            Shader.SetBuffer(kernelHandle, "VBuffer", _vComputeBuffer);

            Shader.SetInt("YRowStride", yRowStride);
            Shader.SetInt("UVRowStride", uvRowStride);
            Shader.SetInt("UVPixelStride", uvPixelStride);

            Shader.SetInt("TargetWidth", FrameRenderTexture.width);
            Shader.SetInt("TargetHeight", FrameRenderTexture.height);

            Shader.SetTexture(kernelHandle, "OutputTexture", FrameRenderTexture);

            int threadGroupsX = Mathf.CeilToInt(FrameRenderTexture.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(FrameRenderTexture.height / 8.0f);
            Shader.Dispatch(kernelHandle, threadGroupsX, threadGroupsY, 1);

            OnFrameProcessed?.Invoke(FrameRenderTexture);
        }
    }
}
