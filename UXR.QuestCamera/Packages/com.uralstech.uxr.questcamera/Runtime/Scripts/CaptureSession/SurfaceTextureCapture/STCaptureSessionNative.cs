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
    /// <summary>
    /// Class to interact with the native graphics plugin for SurfaceTexture rendering.
    /// </summary>
    public static class STCaptureSessionNative
    {
        /// <summary>
        /// Data for setting up a native renderer.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct NativeSetupData
        {
            /// <summary>
            /// The unity texture to render to.
            /// </summary>
            public uint UnityTexture;

            /// <summary>
            /// The width of the texture;
            /// </summary>
            public int Width;

            /// <summary>
            /// The height of the texture.
            /// </summary>
            public int Height;

            /// <summary>
            /// Timestamp associated with the STCaptureSessionWrapper which will be the source for rendering.
            /// </summary>
            public long Timestamp;
        
            /// <summary>
            /// Callback for when the operation is done, type: <see cref="NativeSetupCallbackType"/>.
            /// </summary>
            public IntPtr OnDoneCallback;
        }

        /// <summary>
        /// Data for updating a native renderer.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct NativeUpdateData
        {
            /// <summary>
            /// The native texture to update.
            /// </summary>
            public uint NativeTexture;

            /// <summary>
            /// Callback for when the operation is done, type: <see cref="NativeUpdateCallbackType"/>.
            /// </summary>
            public IntPtr OnDoneCallback;
        }

        /// <summary>
        /// Gets the pointer to the native render event handler.
        /// </summary>
        [DllImport("NativeTextureHelper")]
        public static extern IntPtr GetRenderEventFunction();

        /// <summary>
        /// Event ID for native rendering events.
        /// </summary>
        public enum NativeEventId
        {
            /// <summary>
            /// Sets up a native renderer.
            /// </summary>
            SetupNativeTexture = 1,

            /// <summary>
            /// Disposes a native renderer.
            /// </summary>
            CleanupNativeTexture = 2,

            /// <summary>
            /// Renders the textures.
            /// </summary>
            RenderTextures = 3,
        }

        /// <summary>
        /// Callback type for <see cref="NativeEventId.CleanupNativeTexture"/> and <see cref="NativeEventId.RenderTextures"/> events.
        /// </summary>
        /// <param name="textureId">The ID of the native texture which was updated.</param>
        /// <param name="success">If the operation was successful.</param>
        public delegate void NativeUpdateCallbackType(uint textureId, bool success);

        /// <summary>
        /// Same as <see cref="NativeUpdateCallbackType"/>, but can include a timestamp tracked from C#.
        /// </summary>
        /// <param name="timestamp">The timestamp tracked from C#.</param>
        /// <inheritdoc cref="NativeUpdateCallbackType"/>
        public delegate void NativeUpdateCallbackWithTimestampType(uint textureId, bool success, long timestamp);

        /// <summary>
        /// Additional data tracked in C# related to a native renderer update event.
        /// </summary>
        public readonly struct AdditionalUpdateCallbackData
        {
            /// <summary>
            /// Optional callback that should be called after processing for the current native callback is done.
            /// </summary>
            public readonly NativeUpdateCallbackWithTimestampType? NextCall;

            /// <summary>
            /// Native data that should be disposed as part of this callback.
            /// </summary>
            public readonly IntPtr NativeData;

            /// <summary>
            /// Timestamp value which will be provided in <see cref="NextCall"/>.
            /// </summary>
            public readonly long Timestamp;

            public AdditionalUpdateCallbackData(NativeUpdateCallbackWithTimestampType? nextCall, IntPtr nativeData, long timestamp)
            {
                NextCall = nextCall;
                NativeData = nativeData;
                Timestamp = timestamp;
            }
        }

        /// <summary>
        /// Event queues for update events, mapped to the IDs of the native textures they are for.
        /// </summary>
        public static readonly ConcurrentDictionary<uint, ConcurrentQueue<AdditionalUpdateCallbackData>> NativeUpdateCallbacksQueue = new();

        /// <summary>
        /// The actual callback for native rendering updates.
        /// </summary>
        /// <remarks>
        /// This will dequeue from <see cref="NativeUpdateCallbacksQueue"/> and process the callback data.
        /// </remarks>
        /// <inheritdoc cref="NativeUpdateCallbackType"/>
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

        /// <summary>
        /// Callback type for <see cref="NativeEventId.SetupNativeTexture"/> events.
        /// </summary>
        /// <param name="glIsClean">Was the GL context successfully cleaned up in this call?</param>
        /// <param name="sessionCallSent">Was the call to start the capture session sent to the Kotlin class?</param>
        /// <param name="unityTextureId">The unity texture associated with the event.</param>
        /// <param name="textureId">The native texture created by the call, may be invalid.</param>
        /// <param name="idIsValid">Is <paramref name="textureId"/> a valid texture?</param>
        public delegate void NativeSetupCallbackType(bool glIsClean, bool sessionCallSent, uint unityTextureId, uint textureId, bool idIsValid);

        /// <summary>
        /// Event queues for renderer setup events, mapped to the IDs of the unity textures they are for.
        /// </summary>
        public static readonly ConcurrentDictionary<uint, NativeSetupCallbackType> NativeSetupCallbacksQueue = new();

        /// <summary>
        /// The actual callback for native renderer setup events.
        /// </summary>
        /// <remarks>
        /// This will dequeue from <see cref="NativeSetupCallbacksQueue"/> and process the callbacks.
        /// </remarks>
        /// <inheritdoc cref="NativeSetupCallbackType"/>
        [MonoPInvokeCallback(typeof(NativeSetupCallbackType))]
        public static void NativeSetupCallback(bool glIsClean, bool sessionCallSent, uint unityTextureId, uint textureId, bool idIsValid)
        {
            if (NativeSetupCallbacksQueue.TryRemove(unityTextureId, out NativeSetupCallbackType callback))
                callback.Invoke(glIsClean, sessionCallSent, unityTextureId, textureId, idIsValid);
        }
    }
}