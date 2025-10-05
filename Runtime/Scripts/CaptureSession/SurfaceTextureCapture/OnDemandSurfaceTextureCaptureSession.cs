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
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static Uralstech.UXR.QuestCamera.SurfaceTextureCapture.STCaptureSessionNative;

#if !UNITY_6000_0_OR_NEWER
using Utilities.Async;
#endif

#nullable enable
namespace Uralstech.UXR.QuestCamera.SurfaceTextureCapture
{
    /// <summary>
    /// On-demand version of <see cref="SurfaceTextureCaptureSession"/>.
    /// </summary>
    /// <remarks>
    /// The results of this capture session may be more noisy compared to <see cref="OnDemandCaptureSession"/>.
    /// Requires OpenGL ES 3.0 as the project's Graphics API. Works with single and multi-threaded rendering.
    /// </remarks>
    public class OnDemandSurfaceTextureCaptureSession : SurfaceTextureCaptureSession
    {
        public OnDemandSurfaceTextureCaptureSession(Resolution resolution) : base(resolution) { }

        /// <inheritdoc/>
        public override IntPtr Invoke(string methodName, IntPtr javaArgs)
        {
            if (methodName == "onCaptureCompleted")
            {
                CaptureTimestamp = JNIExtensions.UnboxLongElement(javaArgs, 0);
                return IntPtr.Zero;
            }

            return base.Invoke(methodName, javaArgs);
        }

        /// <summary>
        /// Updates the unity texture with the latest capture from the camera.
        /// </summary>
        /// <param name="onDone">Called when the capture has been rendered in unity, with its timestamp.</param>
        /// <returns><see langword="true"/> if the renderer was invoked, <see langword="false"/> otherwise.</returns>
        public bool RequestCapture(Action<Texture2D, long> onDone)
        {
            if (_nativeTextureId == null || CaptureTimestamp == 0)
                return false;

            SendNativeUpdate(NativeEventId.RenderTextures, (_, result, timestamp) =>
            {
                if (result)
                {
                    GL.InvalidateState();
                    OnFrameReadyInvk(Texture, timestamp);
                    onDone.InvokeOnMainThread(Texture, timestamp);
                }
            }, CaptureTimestamp);
            return true;
        }

        /// <summary>
        /// Updates the unity texture with the latest capture from the camera.
        /// </summary>
        /// <returns>Returns a WaitUntil operation if the renderer was invoked, <see langword="null"/> otherwise.</returns>
        public WaitUntil? RequestCapture()
        {
            bool isDone = false;
            return RequestCapture((_, _) => isDone = true)
                ? new WaitUntil(() => isDone) : null;
        }

#if UNITY_6000_0_OR_NEWER
        /// <summary>
        /// Updates the unity texture with the latest capture from the camera.
        /// </summary>
        /// <returns>The rendered texture and timestamp, or default values if the renderer could not be invoked.</returns>
        public async Awaitable<(Texture2D?, long)> RequestCaptureAsync(CancellationToken token = default)
        {
            TaskCompletionSource<(Texture2D?, long)> tcs = new();
            using (token.Register((tcs) => ((TaskCompletionSource<(Texture2D?, long)>)tcs).TrySetCanceled(), tcs))
            {
                return RequestCapture((texture, timestamp) => tcs.SetResult((texture, timestamp)))
                    ? await tcs.Task : (null, 0);
            }
        }
#endif
    }
}