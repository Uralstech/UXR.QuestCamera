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
using UnityEngine;

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// Forwards frame callbacks from the native Kotlin plugin to Unity.
    /// </summary>
    public class CameraFrameForwarder : AndroidJavaProxy
    {
        /// <summary>
        /// Callback for processing the YUV 4:2:0 frame.
        /// </summary>
        /// <remarks>
        /// <list type="table">
        ///     <listheader>
        ///         <term>Parameters</term>
        ///     </listheader>
        ///     <item>
        ///         <term>yBuffer (IntPtr)</term>
        ///         <description>Pointer to the buffer containing Y (luminance) data of the frame.</description>
        ///     </item>
        ///     <item>
        ///         <term>uBuffer (IntPtr)</term>
        ///         <description>Pointer to the buffer containing U (color) data of the frame.</description>
        ///     </item>
        ///     <item>
        ///         <term>vBuffer (IntPtr)</term>
        ///         <description>Pointer to the buffer containing V (color) data of the frame.</description>
        ///     </item>
        ///     <item>
        ///         <term>ySize (int)</term>
        ///         <description>The size of yBuffer.</description>
        ///     </item>
        ///     <item>
        ///         <term>uSize (int)</term>
        ///         <description>The size of uBuffer.</description>
        ///     </item>
        ///     <item>
        ///         <term>vSize (int)</term>
        ///         <description>The size of vBuffer.</description>
        ///     </item>
        ///     <item>
        ///         <term>yRowStride (int)</term>
        ///         <description>The size of each row of the image in yBuffer in bytes.</description>
        ///     </item>
        ///     <item>
        ///         <term>uvRowStride (int)</term>
        ///         <description>The size of each row of the image in uBuffer and vBuffer in bytes.</description>
        ///     </item>
        ///     <item>
        ///         <term>uvPixelStride (int)</term>
        ///         <description>The size of a pixel in a row of the image in uBuffer and vBuffer in bytes.</description>
        ///     </item>
        /// </list>
        /// </remarks>
        public Action<IntPtr, IntPtr, IntPtr, int, int, int, int, int, int> OnFrameReady;

        public CameraFrameForwarder() : base("com.uralstech.ucamera.CameraFrameCallback") { }

        /// <summary>
        /// Gets the pointer to a native buffer from a Java ByteBuffer object.
        /// </summary>
        /// <param name="byteBuffer">The Java ByteBuffer object.</param>
        /// <returns>A pointer to the native buffer.</returns>
        protected static unsafe IntPtr GetBufferPointer(AndroidJavaObject byteBuffer)
        {
            IntPtr rawBuffer = byteBuffer.GetRawObject();
            sbyte* data = AndroidJNI.GetDirectBufferAddress(rawBuffer);

            return new IntPtr(data);
        }

#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable IDE1006 // Naming Styles
        /// <summary>
        /// Called by the native Kotlin plugin when a new YUV 4:2:0 frame is ready.
        /// </summary>
        /// <remarks>
        /// <paramref name="yBufferObj"/>, <paramref name="uBufferObj"/> and
        /// <paramref name="vBufferObj"/> are Java ByteBuffer objects that are
        /// guaranteed to be direct buffers.
        /// </remarks>
        private unsafe void onFrameReady(
            AndroidJavaObject yBufferObj,
            AndroidJavaObject uBufferObj,
            AndroidJavaObject vBufferObj,
            int ySize,
            int uSize,
            int vSize,
            int yRowStride,
            int uvRowStride,
            int uvPixelStride)
        {
            IntPtr yBuffer = GetBufferPointer(yBufferObj);
            IntPtr uBuffer = GetBufferPointer(uBufferObj);
            IntPtr vBuffer = GetBufferPointer(vBufferObj);

            OnFrameReady?.Invoke(
                yBuffer, uBuffer, vBuffer,
                ySize, uSize, vSize,
                yRowStride, uvRowStride,
                uvPixelStride);
        }
#pragma warning restore IDE0051 // Remove unused private members
#pragma warning restore IDE1006 // Naming Styles
    }
}
