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

using AOT;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

#nullable enable
namespace Uralstech.UXR.QuestCamera.SurfaceTextureCapture
{
    public static class STCaptureSessionNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct NativeSetupData
        {
            public uint UnityTexture;
            public int Width;
            public int Height;
            public long Timestamp;
            public IntPtr OnDoneCallback;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeUpdateData
        {
            public uint NativeTexture;
            public IntPtr OnDoneCallback;
        }

        /// <summary>
        /// Gets the pointer to the native rendering function.
        /// </summary>
        [DllImport("NativeTextureHelper")]
        public static extern IntPtr GetRenderEventFunction();

        public enum NativeEventId
        {
            SetupNativeTexture = 1,
            CleanupNativeTexture = 2,
            RenderTextures = 3,
        }

        public delegate void NativeUpdateCallbackType(uint textureId, bool success);
        public delegate void NativeUpdateCallbackWithTimestampType(uint textureId, bool success, long timestamp);
        public readonly struct AdditionalUpdateCallbackData
        {
            public readonly NativeUpdateCallbackWithTimestampType? NextCall;
            public readonly IntPtr NativeData;
            public readonly long Timestamp;

            public AdditionalUpdateCallbackData(NativeUpdateCallbackWithTimestampType? nextCall, IntPtr nativeData, long timestamp)
            {
                NextCall = nextCall;
                NativeData = nativeData;
                Timestamp = timestamp;
            }
        }

        public static readonly ConcurrentDictionary<uint, ConcurrentQueue<AdditionalUpdateCallbackData>> NativeUpdateCallbacksQueue = new();

        [MonoPInvokeCallback(typeof(NativeUpdateCallbackType))]
        public static void NativeUpdateCallback(uint textureId, bool success)
        {
            if (NativeUpdateCallbacksQueue.TryGetValue(textureId, out ConcurrentQueue<AdditionalUpdateCallbackData> queue)
                && queue.TryDequeue(out AdditionalUpdateCallbackData data))
            {
                if (data.NativeData != IntPtr.Zero)
                    Marshal.FreeHGlobal(data.NativeData);

                data.NextCall?.Invoke(textureId, success, data.Timestamp);
            }
        }

        public delegate void NativeSetupCallbackType(bool glIsClean, bool sessionCallSent, uint textureId, bool idIsValid);

        public static readonly ConcurrentQueue<NativeSetupCallbackType> NativeSetupCallbacksQueue = new();

        [MonoPInvokeCallback(typeof(NativeSetupCallbackType))]
        public static void NativeSetupCallback(bool glIsClean, bool sessionCallSent, uint textureId, bool idIsValid)
        {
            if (NativeSetupCallbacksQueue.TryDequeue(out NativeSetupCallbackType callback))
                callback.Invoke(glIsClean, sessionCallSent, textureId, idIsValid);
        }
    }
}