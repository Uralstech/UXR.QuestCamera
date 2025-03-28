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

namespace Uralstech.UXR.QuestCamera
{
    public class SurfaceTextureCaptureSession : ContinuousCaptureSession
    {
        #region Native Stuff
        private const int CreateGlTextureEvent = 1;
        private const int DestroyGlTextureEvent = 2;
        private const int UpdateSurfaceTextureEvent = 3;

        [DllImport("NativeTextureHelper")]
        private static extern IntPtr GetRenderEventFunction();

        [StructLayout(LayoutKind.Sequential)]
        struct TextureSetupData
        {
            public uint UnityTextureId;
            public int Width;
            public int Height;

            public long TimeStamp;
            public IntPtr OnDoneCallback;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TextureUpdateData
        {
            public int CameraTextureId;
            public IntPtr OnDoneCallback;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TextureDeletionData
        {
            public uint TextureId;
            public IntPtr OnDoneCallback;
        }
        #endregion

        #region Static
        private static readonly ConcurrentQueue<Action> s_nativeTextureCallbacks = new();

        [MonoPInvokeCallback(typeof(Action))]
        private static void NativeTextureCallback()
        {
            if (s_nativeTextureCallbacks.TryDequeue(out Action action))
                action?.Invoke();
        }
        #endregion

        public Resolution Resolution { get; private set; }

        public Texture2D Texture { get; private set; }

        private CommandBuffer _commandBuffer;

        protected void Awake()
        {
            _commandBuffer = new CommandBuffer();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _commandBuffer.Dispose();
        }

        internal void CreateNativeTexture(Resolution resolution, long timeStamp)
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

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            s_nativeTextureCallbacks.Enqueue(() =>
            {
                Debug.Log("Native texture setup completed.");
                Marshal.FreeHGlobal(dataPtr);
            });

            _commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), CreateGlTextureEvent, dataPtr);
            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();
        }

        #region Native Callbacks
#pragma warning disable IDE1006 // Naming Styles
        public void _onCaptureCompleted(string textureId)
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

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            s_nativeTextureCallbacks.Enqueue(() =>
            {
                Debug.Log("Updating texture in Unity.");
                Marshal.FreeHGlobal(dataPtr);
                GL.InvalidateState();
            });

            _commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), UpdateSurfaceTextureEvent, dataPtr);
            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();
        }

        public void _destroyNativeTexture(string textureId)
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

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            s_nativeTextureCallbacks.Enqueue(async () =>
            {
                Debug.Log("Native rendering data destroyed.");
                Marshal.FreeHGlobal(dataPtr);

                await Awaitable.MainThreadAsync();
                
                base.Release();
                Destroy(gameObject);
            });

            _commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), DestroyGlTextureEvent, dataPtr);
            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();
        }
#pragma warning restore IDE1006 // Naming Styles
        #endregion
    }
}