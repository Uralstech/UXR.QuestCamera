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
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

#if !UNITY_6000_0_OR_NEWER
using Utilities.Async;
#endif

#nullable enable
namespace Uralstech.UXR.QuestCamera.Vulkan
{
    /// <summary>Manages a camera capture session with a continuous/repeating request.</summary>
    /// <remarks>Texture conversion is done through a native Vulkan plugin.</remarks>
    public class VulkanContinuousCaptureSession : CaptureSessionBase<VulkanContinuousCaptureSession.Proxy>
    {
        /// <summary>Signals the Vulkan plugin that a frame is ready for conversion.</summary>
        /// <remarks>
        /// <a href="https://developer.android.com/ndk/reference/group/a-hardware-buffer#ahardwarebuffer_acquire">AHardwareBuffer_acquire</a>
        /// has already been called on <paramref name="acquiredBufferPtr"/>, and this method <b>must</b> release it either through native
        /// plugin render events or <see cref="VulkanAPI.releaseHardwareBuffer"/>.
        /// </remarks>
        /// <param name="acquiredBufferPtr">The HardwareBuffer associated with the frame.</param>
        /// <param name="bufferDataSpace">The <a href="https://developer.android.com/reference/android/hardware/DataSpace">DataSpace</a> of the buffer.</param>
        /// <param name="bufferId">The global unique ID (<a href="https://developer.android.com/ndk/reference/group/a-hardware-buffer#ahardwarebuffer_getid">AHardwareBuffer_getId</a>) of the buffer.</param>
        /// <param name="timestampNs">The timestamp the frame was captured at in nanoseconds.</param>
        public delegate void OnFrameReadyCallback(IntPtr acquiredBufferPtr, int bufferDataSpace, long bufferId, long timestampNs);
        
        /// <inheritdoc/>
        public sealed class Proxy : ProxyBase
        {
            private const string ClassName = "com.uralstech.uxr.questcamera.sessions.VulkanCaptureSessionManager$Callbacks";
            
            /// <inheritdoc cref="OnFrameReadyCallback"/>
            public event OnFrameReadyCallback? OnFrameReady;

            public Proxy() : base(ClassName) { }

            public override IntPtr Invoke(string methodName, IntPtr javaArgs)
            {
                if (methodName != "onFrameReady")
                    return base.Invoke(methodName, javaArgs);
                
                IntPtr acquiredBufferPtr = (IntPtr)JNIExtensions.UnboxLongElement(javaArgs, 0);
                int bufferDataSpace = JNIExtensions.UnboxIntElement(javaArgs, 1);
                long bufferId = JNIExtensions.UnboxLongElement(javaArgs, 2);
                long timestampNs = JNIExtensions.UnboxLongElement(javaArgs, 3);
                
                OnFrameReady?.Invoke(acquiredBufferPtr, bufferDataSpace, bufferId, timestampNs);
                return IntPtr.Zero;
            }
        }
        
        private const string ClassName = "com.uralstech.uxr.questcamera.sessions.VulkanCaptureSessionManager";

        /// <summary>Callback for when a frame has been processed, with the frame texture and capture timestamp.</summary>
        /// <remarks>The image at this point has not <i>actually</i> finished processing, but all GPU commands to do so have been executed.</remarks>
        public event Action<RenderTexture, long>? OnFrameProcessed;

        /// <summary><see langword="true"/> if a capture was processed this frame; <see langword="false"/> otherwise.</summary>
        public bool HasNewFrame => _lastUpdateFrame == Time.frameCount;

        /// <summary>The output texture with converted frames.</summary>
        public readonly RenderTexture Texture;

        /// <summary>The capture timestamp of the last processed frame.</summary>
        public long CaptureTimestamp { get; private set; }

        private readonly CommandBuffer _commandBuffer;
        private readonly IntPtr _texturePtr;
        private int _lastUpdateFrame;
        
        public VulkanContinuousCaptureSession(Resolution resolution, GraphicsFormat textureFormat = GraphicsFormat.None)
            : base(MakeProxy(out Proxy proxy), new(ClassName, resolution.width, resolution.height, proxy))
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan)
                throw new NotSupportedException("Unsupported graphics device, requires Vulkan!");
            
            if (AndroidAPILevel.Current < AndroidAPILevel.Tiramisu)
                throw new NotSupportedException($"{GetType().Name} requires Android 13 (API level 33) or higher to function!");
            
            if (textureFormat == GraphicsFormat.None)
                textureFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

            if (!GraphicsUtils.IsGraphicsFormatSupportedForRender(textureFormat))
                throw new ArgumentException($"Format {textureFormat} is not supported on device.", nameof(textureFormat));
            
            Texture = new RenderTexture(resolution.width, resolution.height, 0, textureFormat);
            if (!Texture.Create())
                throw new UnityException("Could not create RenderTexture.");
            
            _texturePtr = Texture.GetNativeTexturePtr();
            
            _commandBuffer = new CommandBuffer();
            NativeProxy.OnFrameReady += OnFrameReadyNative;
        }

        private void OnFrameReadyNative(IntPtr acquiredBufferPtr, int bufferDataSpace, long bufferId, long timestampNs)
        {
            if (State != ResourceState.Valid)
                VulkanAPI.releaseHardwareBuffer(acquiredBufferPtr);
            else
                DispatchFrameConversionAsync(acquiredBufferPtr, bufferDataSpace, bufferId, timestampNs).Forget();
        }

        private async Task DispatchFrameConversionAsync(IntPtr acquiredBufferPtr, int bufferDataSpace, long bufferId, long timestampNs)
        {
            try
            {
#if UNITY_6000_0_OR_NEWER
                await Awaitable.MainThreadAsync();
#else
                await Awaiters.UnityMainThread;
#endif
                if (State != ResourceState.Valid)
                {
                    VulkanAPI.releaseHardwareBuffer(acquiredBufferPtr);
                    return;
                }

                RenderData data = new(
                    acquiredBufferPtr,
                    bufferDataSpace,
                    (ulong)bufferId,
                    _texturePtr,
                    VulkanAPI.RenderCallbackPtr);

                IntPtr allocated = VulkanAPI.AllocateRenderData(data);
                VulkanAPI.RenderCallbacksRegistry[bufferId] = allocated;

                _commandBuffer.Clear();
                _commandBuffer.IssuePluginEventAndData(VulkanAPI.getVulkanRenderEvent(), 1, allocated);
                Graphics.ExecuteCommandBuffer(_commandBuffer);
            }
            catch
            {
                VulkanAPI.releaseHardwareBuffer(acquiredBufferPtr);
                VulkanAPI.TryFreeRenderData(bufferId);
                throw;
            }
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;
            State = ResourceState.Invalid;

            try
            {
                await CloseWork();
            }
            finally
            {
                _commandBuffer.Dispose();
            
                Texture.Release();
                UnityEngine.Object.Destroy(Texture);
            }
            
            GC.SuppressFinalize(this);
        }
    }
}