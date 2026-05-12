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
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>Manages a camera capture session with a continuous/repeating request.</summary>
    /// <remarks>
    /// This does not do any image processing/conversion on its own,
    /// but classes like <see cref="YUVConverter"/> can register callbacks
    /// to <see cref="Proxy.OnFrameReady"/> to do their own processing of
    /// the raw image YUV 4:2:0, BT.601 Full Range data.
    /// </remarks>
    public class ContinuousCaptureSession : CaptureSessionBase<ContinuousCaptureSession.Proxy>
    {
        /// <summary>Processes a frame received from the native capture session.</summary>
        /// <param name="yBuffer">The pointer to this frame's Y (luminance) data.</param>
        /// <param name="yBufferSize">The size of the Y buffer in bytes.</param>
        /// <param name="uBuffer">The pointer to this frame's U (color) data.</param>
        /// <param name="vBuffer">The pointer to this frame's V (color) data.</param>
        /// <param name="uvBufferSize">The size of the U and V buffers in bytes.</param>
        /// <param name="yRowStride">The size of each row of the image in <paramref name="yBuffer"/> in bytes.</param>
        /// <param name="uvRowStride">The size of each row of the image in <paramref name="uBuffer"/> and <paramref name="vBuffer"/> in bytes.</param>
        /// <param name="uvPixelStride">The size of a pixel in a row of the image in <paramref name="uBuffer"/> and <paramref name="vBuffer"/> in bytes.</param>
        /// <param name="timestamp">The timestamp the frame was captured at in nanoseconds.</param>
        public delegate void OnFrameReadyCallback(
            IntPtr yBuffer, long yBufferSize,
            IntPtr uBuffer,
            IntPtr vBuffer, long uvBufferSize,
            int yRowStride,
            int uvRowStride,
            int uvPixelStride,
            long timestamp);

        /// <inheritdoc/>
        public sealed class Proxy : ProxyBase
        {
            private const string ClassName = "com.uralstech.uxr.questcamera.ContinuousCaptureSessionManager$Callbacks";

            /// <inheritdoc cref="OnFrameReadyCallback"/>
            /// <remarks>See <see cref="OnFrameReadyCallback"/> for parameters.</remarks>
            public event OnFrameReadyCallback? OnFrameReady;

            public Proxy() : base(ClassName) { }

            public override IntPtr Invoke(string methodName, IntPtr javaArgs)
            {
                if (methodName != "onFrameReady")
                    return base.Invoke(methodName, javaArgs);

                (IntPtr yBufferObj, IntPtr yBufferPtr) = JNIExtensions.UnboxByteBufferElement(javaArgs, 0);
                (IntPtr uBufferObj, IntPtr uBufferPtr) = JNIExtensions.UnboxByteBufferElement(javaArgs, 1);
                (IntPtr vBufferObj, IntPtr vBufferPtr) = JNIExtensions.UnboxByteBufferElement(javaArgs, 2);
                int yRowStride = JNIExtensions.UnboxIntElement(javaArgs, 3);
                int uvRowStride = JNIExtensions.UnboxIntElement(javaArgs, 4);
                int uvPixelStride = JNIExtensions.UnboxIntElement(javaArgs, 5);
                long timestampNs = JNIExtensions.UnboxLongElement(javaArgs, 6);

                try
                {
                    OnFrameReady?.Invoke(
                        yBufferPtr, AndroidJNI.GetDirectBufferCapacity(yBufferObj),
                        uBufferPtr,
                        vBufferPtr, AndroidJNI.GetDirectBufferCapacity(uBufferObj),
                        yRowStride, uvRowStride,
                        uvPixelStride, timestampNs);
                }
                finally
                {
                    AndroidJNI.DeleteLocalRef(yBufferObj);
                    AndroidJNI.DeleteLocalRef(uBufferObj);
                    AndroidJNI.DeleteLocalRef(vBufferObj);
                }

                return IntPtr.Zero;
            }
        }

        private const string ClassName = "com.uralstech.uxr.questcamera.ContinuousCaptureSessionManager";
        
        public ContinuousCaptureSession(Resolution resolution) : this(resolution, ClassName) { }

        // Creates proxy and returns it via out param so it can be passed to both base and native constructor
        private static Proxy MakeProxy(out Proxy proxy) => proxy = new Proxy();
        protected ContinuousCaptureSession(Resolution resolution, string className)
            : base(MakeProxy(out Proxy proxy), new(className, resolution.width, resolution.height, proxy)) { }
    }
}