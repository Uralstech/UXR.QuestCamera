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
        public Func<IntPtr, IntPtr, IntPtr, int, int, int, int, int, int, Task> OnFrameReady;

        public CameraFrameForwarder() : base("com.uralstech.ucamera.CameraFrameCallback") { }

        /// <inheritdoc/>
        public unsafe override IntPtr Invoke(string methodName, IntPtr javaArgs)
        {
            if (methodName != "onFrameReady")
                return base.Invoke(methodName, javaArgs);

            sbyte* yBuffer = AndroidJNI.GetDirectBufferAddress(AndroidJNI.GetObjectArrayElement(javaArgs, 0));
            sbyte* uBuffer = AndroidJNI.GetDirectBufferAddress(AndroidJNI.GetObjectArrayElement(javaArgs, 1));
            sbyte* vBuffer = AndroidJNI.GetDirectBufferAddress(AndroidJNI.GetObjectArrayElement(javaArgs, 2));

            AndroidJNIHelper.Unbox(AndroidJNI.GetObjectArrayElement(javaArgs, 3), out int ySize);
            AndroidJNIHelper.Unbox(AndroidJNI.GetObjectArrayElement(javaArgs, 4), out int uSize);
            AndroidJNIHelper.Unbox(AndroidJNI.GetObjectArrayElement(javaArgs, 5), out int vSize);
            AndroidJNIHelper.Unbox(AndroidJNI.GetObjectArrayElement(javaArgs, 6), out int yRowStride);
            AndroidJNIHelper.Unbox(AndroidJNI.GetObjectArrayElement(javaArgs, 7), out int uvRowStride);
            AndroidJNIHelper.Unbox(AndroidJNI.GetObjectArrayElement(javaArgs, 8), out int uvPixelStride);

            OnFrameReady?.Invoke(
                (IntPtr)yBuffer, (IntPtr)uBuffer, (IntPtr)vBuffer,
                ySize, uSize, vSize,
                yRowStride, uvRowStride,
                uvPixelStride)?.Wait();

            return IntPtr.Zero;
        }
    }
}
