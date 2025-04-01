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
using UnityEngine;
using UnityEngine.Rendering;

#if !UNITY_6000_0_OR_NEWER && UTILITIES_ASYNC
using Utilities.Async;
#endif

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// This is an experimental capture session type that uses a native OpenGL texture to capture images for better performance.
    /// </summary>
    /// <remarks>
    /// The results of this capture session may be more noisy compared to <see cref="ContinuousCaptureSession"/>.
    /// Requires OpenGL ES 3.0 or higher as the project's Graphics API. Works with single and multi-threaded rendering.
    /// </remarks>
    public class SurfaceTextureCaptureSession : ContinuousCaptureSession
    {
        #region Native Stuff
        /// <summary>Native event to create the native texture.</summary>
        protected const int CreateGlTextureEvent = 1;

        /// <summary>Native event to destroy the native texture.</summary>
        protected const int DestroyGlTextureEvent = 2;

        /// <summary>Native event to update the native texture and convert it to the Unity texture.</summary>
        protected const int UpdateSurfaceTextureEvent = 3;

        /// <summary>
        /// Gets the pointer to the native rendering function.
        /// </summary>
        [DllImport("NativeTextureHelper")]
        protected static extern IntPtr GetRenderEventFunction();

        /// <summary>
        /// Data structure to setup the native texture.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        protected struct TextureSetupData
        {
            /// <summary>The Unity texture that the native texture will convert to.</summary>
            public uint UnityTextureId;

            /// <summary>The width of the texture.</summary>
            public int Width;

            /// <summary>The height of the texture.</summary>
            public int Height;

            /// <summary>The time when this capture session was created.</summary>
            public long TimeStamp;

            /// <summary>The callback to call when the setup is done.</summary>
            public IntPtr OnDoneCallback;
        }

        /// <summary>
        /// Data structure to update the native and Unity textures.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        protected struct TextureUpdateData
        {
            /// <summary>The ID of the native texture.</summary>
            public int CameraTextureId;

            /// <summary>The callback to call when the update is done.</summary>
            public IntPtr OnDoneCallback;
        }

        /// <summary>
        /// Data structure to delete the native texture.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        protected struct TextureDeletionData
        {
            /// <summary>The ID of the native texture.</summary>
            public uint TextureId;

            /// <summary>The callback to call when the deletion is done.</summary>
            public IntPtr OnDoneCallback;
        }
        #endregion

        #region Static
        /// <summary>Queue for native event callbacks.</summary>
        protected static readonly ConcurrentQueue<Action> s_nativeTextureCallbacks = new();

        /// <summary>
        /// Callback for the native texture events. It will dequeue from <see cref="s_nativeTextureCallbacks"/> and call it.
        /// </summary>
        [MonoPInvokeCallback(typeof(Action))]
        protected static void NativeTextureCallback()
        {
            if (s_nativeTextureCallbacks.TryDequeue(out Action action))
                action?.Invoke();
        }
        #endregion

        /// <summary>
        /// The resolution of this capture session.
        /// </summary>
        public Resolution Resolution { get; private set; }

        /// <summary>
        /// The texture that will be updated with the camera feed.
        /// </summary>
        public Texture2D Texture { get; private set; }

        /// <summary>
        /// The command buffer to issue native events.
        /// </summary>
        protected CommandBuffer _commandBuffer;

        protected void Awake()
        {
            _commandBuffer = new CommandBuffer();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _commandBuffer.Dispose();
        }

        /// <summary>
        /// Sends an event to the native plugin.
        /// </summary>
        /// <typeparam name="T">The type of the data to send.</typeparam>
        /// <param name="data">The data to send.</param>
        /// <param name="eventId">The unique ID of the event.</param>
        /// <param name="additionalAction">Any additional action to be done after the event is completed.</param>
        protected virtual void CallNativeEvent<T>(T data, int eventId, Action additionalAction = null)
            where T : struct
        {
            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            s_nativeTextureCallbacks.Enqueue(() =>
            {
                Debug.Log($"Native plugin event of ID \"{eventId}\" completed.");
                Marshal.FreeHGlobal(dataPtr);
                additionalAction?.Invoke();
            });

            _commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), eventId, dataPtr);
            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();
        }

        /// <summary>
        /// Creates the native texture and sets up the OpenGL texture.
        /// </summary>
        /// <param name="resolution">The resolution of the capture session.</param>
        /// <param name="timeStamp">The time at which this capture session was created.</param>
        internal virtual void CreateNativeTexture(Resolution resolution, long timeStamp)
        {
            Texture = new Texture2D(resolution.width, resolution.height, TextureFormat.ARGB32, false);
            Resolution = resolution;

            TextureSetupData data = new()
            {
                UnityTextureId = (uint)Texture.GetNativeTexturePtr(),
                Width = resolution.width,
                Height = resolution.height,

                TimeStamp = timeStamp,
                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<Action>(NativeTextureCallback)
            };
            
            CallNativeEvent(data, CreateGlTextureEvent);
        }

        #region Native Callbacks
#pragma warning disable IDE1006 // Naming Styles
        public virtual void _onCaptureCompleted(string textureId)
        {
            if (!int.TryParse(textureId, out int texId))
            {
                Debug.LogError($"Could not get texture ID for {nameof(SurfaceTextureCaptureSession)}.{nameof(_onCaptureCompleted)}.");
                return;
            }

            TextureUpdateData data = new()
            {
                CameraTextureId = texId,
                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<Action>(NativeTextureCallback)
            };

            CallNativeEvent(data, UpdateSurfaceTextureEvent, GL.InvalidateState);
        }

        public virtual void _destroyNativeTexture(string textureId)
        {
            if (!uint.TryParse(textureId, out uint texId))
            {
                Debug.LogError($"Could not get texture ID for {nameof(SurfaceTextureCaptureSession)}.{nameof(_destroyNativeTexture)}.");
                return;
            }

            TextureDeletionData data = new()
            {
                TextureId = texId,
                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<Action>(NativeTextureCallback)
            };

            CallNativeEvent(data, DestroyGlTextureEvent, async () =>
            {
#if UNITY_6000_0_OR_NEWER
                await Awaitable.MainThreadAsync();
#elif UTILITIES_ASYNC
                await Awaiters.UnityMainThread;
#endif

                base.Release();
                Destroy(gameObject);
            });
        }
#pragma warning restore IDE1006 // Naming Styles
        #endregion
    }
}