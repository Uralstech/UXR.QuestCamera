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
using System.Collections.Generic;
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

        private delegate void TextureEventCallback(uint textureId);

        [StructLayout(LayoutKind.Sequential)]
        struct TextureSetupData
        {
            public uint UnityTextureId;
            
            public long TimeStamp;
            public IntPtr OnDoneCallback;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TextureUpdateData
        {
            public uint UnityTextureId;
            public int CameraTextureId;
            public int Width;
            public int Height;

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
        private static readonly Dictionary<uint, Action> s_nativeTextureCallbacks = new();

        [MonoPInvokeCallback(typeof(Action<uint>))]
        private static void NativeTextureCallback(uint textureId)
        {
            lock (s_nativeTextureCallbacks)
            {
                if (s_nativeTextureCallbacks.Remove(textureId, out Action action))
                    action?.Invoke();
            }
        }
        #endregion

        public Resolution Resolution { get; private set; }

        public Texture2D Texture { get; private set; }

        internal void CreateNativeTexture(Resolution resolution, long timeStamp)
        {
            Texture = new Texture2D(resolution.width, resolution.height, TextureFormat.RGBA32, false, true);
            Resolution = resolution;

            TextureSetupData data = new()
            {
                UnityTextureId = (uint)Texture.GetNativeTexturePtr(),

                TimeStamp = timeStamp,
                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<TextureEventCallback>(NativeTextureCallback)
            };

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            lock (s_nativeTextureCallbacks)
            {
                s_nativeTextureCallbacks.Add(data.UnityTextureId, () =>
                {
                    Debug.Log("Native texture setup completed.");
                    Marshal.FreeHGlobal(dataPtr);
                });
            }

            using CommandBuffer commandBuffer = new();

            commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), CreateGlTextureEvent, dataPtr);
            Graphics.ExecuteCommandBuffer(commandBuffer);
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
                UnityTextureId = (uint)Texture.GetNativeTexturePtr(),
                CameraTextureId = texId,
                Width = Resolution.width,
                Height = Resolution.height,

                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<TextureEventCallback>(NativeTextureCallback)
            };

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            lock (s_nativeTextureCallbacks)
            {
                s_nativeTextureCallbacks.Add(data.UnityTextureId, () =>
                {
                    Debug.Log("Updating texture in Unity.");
                    Marshal.FreeHGlobal(dataPtr);
                    GL.InvalidateState();
                });
            }

            using CommandBuffer commandBuffer = new();
            commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), UpdateSurfaceTextureEvent, dataPtr);

            Graphics.ExecuteCommandBuffer(commandBuffer);
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
                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<TextureEventCallback>(NativeTextureCallback)
            };

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            lock (s_nativeTextureCallbacks)
            {
                s_nativeTextureCallbacks.Add(data.TextureId, async () =>
                {
                    Debug.Log("Native rendering data destroyed.");
                    Marshal.FreeHGlobal(dataPtr);

                    await Awaitable.MainThreadAsync();
                    
                    base.Release();
                    Destroy(gameObject);
                });
            }

            using CommandBuffer commandBuffer = new();
            commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), DestroyGlTextureEvent, dataPtr);

            Graphics.ExecuteCommandBuffer(commandBuffer);
        }
#pragma warning restore IDE1006 // Naming Styles
        #endregion
    }
}