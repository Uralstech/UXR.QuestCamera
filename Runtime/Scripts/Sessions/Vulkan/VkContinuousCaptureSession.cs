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
using System.Threading;
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
    public class VkContinuousCaptureSession : CaptureSessionBase<VkContinuousCaptureSession.Proxy>
    {
        public delegate void OnFrameReadyCallback(IntPtr acquiredBufferPtr, long bufferId, long timestampNs);
        
        /// <inheritdoc/>
        public sealed class Proxy : ProxyBase
        {
            private const string ClassName = "com.uralstech.uxr.questcamera.sessions.vulkan.VkContinuousCaptureSessionManager$Callbacks";
            
            public event OnFrameReadyCallback? OnFrameReady;

            public Proxy() : base(ClassName) { }

            public override IntPtr Invoke(string methodName, IntPtr javaArgs)
            {
                if (methodName != "onFrameReady")
                    return base.Invoke(methodName, javaArgs);
                
                IntPtr acquiredBufferPtr = (IntPtr)JNIExtensions.UnboxLongElement(javaArgs, 0);
                long bufferId = JNIExtensions.UnboxLongElement(javaArgs, 1);
                long timestampNs = JNIExtensions.UnboxLongElement(javaArgs, 2);
                
                OnFrameReady?.Invoke(acquiredBufferPtr, bufferId, timestampNs);
                return IntPtr.Zero;
            }
        }
        
        private const string ClassName = "com.uralstech.uxr.questcamera.sessions.vulkan.VkContinuousCaptureSessionManager";

        public readonly Texture2D Texture;

        private readonly CommandBuffer _commandBuffer;
        private readonly IntPtr _texturePtr;
        private int _isProcessing;
        
        public VkContinuousCaptureSession(Resolution resolution) : this(resolution, ClassName) { }

        // Creates proxy and returns it via out param so it can be passed to both base and native constructor
        private static Proxy MakeProxy(out Proxy proxy) => proxy = new Proxy();

        protected VkContinuousCaptureSession(Resolution resolution, string className)
            : base(MakeProxy(out Proxy proxy), new(className, resolution.width, resolution.height, proxy))
        {
            GraphicsFormat textureFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Texture = new Texture2D(resolution.width, resolution.height, textureFormat, TextureCreationFlags.DontUploadUponCreate | TextureCreationFlags.DontInitializePixels);
            _texturePtr = Texture.GetNativeTexturePtr();
            
            _commandBuffer = new CommandBuffer();
            NativeProxy.OnFrameReady += OnFrameReadyNative;
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            
            _commandBuffer.Dispose();
            UnityEngine.Object.Destroy(Texture);
        }

        private void OnFrameReadyNative(IntPtr acquiredBufferPtr, long bufferId, long timestampNs)
        {
            if (State != ResourceState.Valid || Interlocked.CompareExchange(ref _isProcessing, 1, 0) == 1)
            {
                VkAPI.releaseHardwareBuffer(acquiredBufferPtr);
                return;
            }
            
            DispatchFrameConversionAsync(acquiredBufferPtr, bufferId, timestampNs).Forget();
        }

        private async Task DispatchFrameConversionAsync(IntPtr acquiredBufferPtr, long bufferId, long timestampNs)
        {
            try
            {
#if UNITY_6000_0_OR_NEWER
                await Awaitable.MainThreadAsync();
#else
                await Awaiters.UnityMainThread;
#endif

                RenderData data = new(
                    acquiredBufferPtr,
                    (ulong)bufferId,
                    _texturePtr,
                    VkAPI.RenderCallbackPtr);

                IntPtr allocated = VkAPI.AllocateRenderData(data);
                VkAPI.RenderCallbacksRegistry[bufferId] = allocated;

                _commandBuffer.Clear();
                _commandBuffer.IssuePluginEventAndData(VkAPI.getVulkanRenderEvent(), 1, allocated);
                Graphics.ExecuteCommandBuffer(_commandBuffer);
            }
            catch
            {
                VkAPI.releaseHardwareBuffer(acquiredBufferPtr);
                VkAPI.TryFreeRenderData(bufferId);
                throw;
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessing, 0);
            }
        }
    }
}