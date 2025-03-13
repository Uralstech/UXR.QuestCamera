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
    internal class NativeCameraFrameCallback : AndroidJavaProxy
    {
        public Action<IntPtr, IntPtr, IntPtr, int, int, int, int, int, int> OnFrameReady;

        public NativeCameraFrameCallback() : base("com.uralstech.ucamera.CameraFrameCallback") { }

        private static unsafe IntPtr GetBufferPointer(AndroidJavaObject byteBuffer)
        {
            IntPtr rawBuffer = byteBuffer.GetRawObject();
            sbyte* data = AndroidJNI.GetDirectBufferAddress(rawBuffer);

            return new IntPtr(data);
        }

#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable IDE1006 // Naming Styles
        unsafe void onFrameReady(
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
