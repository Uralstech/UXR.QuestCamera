using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

#nullable enable
namespace Uralstech.UXR.QuestCamera.Vulkan
{
    /// <summary>Manages a continuous capture session with user-invoked texture conversion.</summary>
    /// <inheritdoc/>
    public sealed class VulkanOnDemandCaptureSession : VulkanContinuousCaptureSession
    {
        private int _hasRequestedCapture;
        
        /// <inheritdoc/>
        public VulkanOnDemandCaptureSession(Resolution resolution, GraphicsFormat textureFormat = GraphicsFormat.None) : base(resolution, textureFormat) { }

        protected override void OnFrameReadyNative(IntPtr acquiredBufferPtr, int bufferDataSpace, long bufferId, long timestampNs)
        {
            if (State != ResourceState.Valid || Interlocked.Exchange(ref _hasRequestedCapture, 0) == 0)
                VulkanAPI.releaseHardwareBuffer(acquiredBufferPtr);
            else
                DispatchFrameConversionAsync(acquiredBufferPtr, bufferDataSpace, bufferId, timestampNs).Forget();
        }

        /// <summary>Requests that the next frame be processed (converted).</summary>
        /// <exception cref="ObjectDisposedException"/>
        public void RequestFrame()
        {
            ThrowIfDisposed();
            Interlocked.Exchange(ref _hasRequestedCapture, 1);
        }

        /// <summary>Requests and processes a single frame and returns the result.</summary>
        /// <remarks>The image when returned has not <i>actually</i> finished processing, but all GPU commands to do so have been enqueued.</remarks>
        /// <returns>Capture timestamp and updated texture.</returns>
        /// <exception cref="ObjectDisposedException"/>
        public async ValueTask<(long, RenderTexture)> ProcessSingleFrameAsync(CancellationToken token = default)
        {
            ThrowIfDisposed();
            
            TaskCompletionSource<(long, RenderTexture)> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnProcessed(RenderTexture texture, long timestampNs) => tcs.TrySetResult((timestampNs, texture));
            
            OnFrameProcessed += OnProcessed;

            try
            {
                RequestFrame();
                await using (_ = token.Register(tcs.SetCanceled))
                    return await tcs.Task;
            }
            finally
            {
                OnFrameProcessed -= OnProcessed;
            }
        }
    }
}