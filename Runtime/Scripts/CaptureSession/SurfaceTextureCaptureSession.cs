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
        private struct TextureUpdateData
        {
            public uint UnityTextureId;
            public int CameraTextureId;
            public int Width;
            public int Height;
        };
        #endregion

        public Resolution Resolution { get; private set; }

        public Texture2D Texture { get; private set; }

        public NativeWrapperState NativeTextureState { get; private set; }

        internal async void CreateNativeTexture(Resolution resolution, long timeStamp)
        {
            Texture = new Texture2D(resolution.width, resolution.height, TextureFormat.RGBA32, false);
            Resolution = resolution;

            IntPtr timeStampPtr = Marshal.AllocHGlobal(sizeof(long));
            Marshal.WriteInt64(timeStampPtr, timeStamp);

            using CommandBuffer commandBuffer = new();

            commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), CreateGlTextureEvent, timeStampPtr);
            Graphics.ExecuteCommandBuffer(commandBuffer);

            while (NativeTextureState == NativeWrapperState.Initializing)
                await Awaitable.NextFrameAsync();

            Debug.Log("Releasing timeStampPtr.");
            Marshal.FreeHGlobal(timeStampPtr);
        }

        public void Destroy()
        {
            base.Release();
            Destroy(gameObject);
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
                UnityTextureId = (uint)Texture.GetNativeTexturePtr(),
                Width = Resolution.width,
                Height = Resolution.height,
            };

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            using CommandBuffer commandBuffer = new();
            commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), UpdateSurfaceTextureEvent, dataPtr);

            Graphics.ExecuteCommandBuffer(commandBuffer);
            Marshal.FreeHGlobal(dataPtr);
        }

        public void _onTextureCreated(string _)
        {
            NativeTextureState = NativeWrapperState.Opened;
        }

        public void _destroyNativeTexture(string textureId)
        {
            if (!int.TryParse(textureId, out int texId))
            {
                Debug.LogError($"Could not get texture ID for {nameof(SurfaceTextureCaptureSession)}.{nameof(_destroyNativeTexture)}.");
                return;
            }

            NativeTextureState = NativeWrapperState.Closed;
            if (Texture != null)
                Destroy(Texture);

            IntPtr dataPtr = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(dataPtr, texId);

            using CommandBuffer commandBuffer = new();
            commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), DestroyGlTextureEvent, dataPtr);

            Graphics.ExecuteCommandBuffer(commandBuffer);
            Marshal.FreeHGlobal(dataPtr);
        }
#pragma warning restore IDE1006 // Naming Styles
        #endregion
    }
}