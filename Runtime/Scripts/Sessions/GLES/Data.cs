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
using System.Runtime.InteropServices;

#nullable enable
namespace Uralstech.UXR.QuestCamera.GLES
{
    /// <summary>
    /// IDs of all Render Job events.
    /// </summary>
    public enum RenderJobEvent
    {
        /// <summary>Sets up a native texture to convert from, and creates a registerable job for the capture session.</summary>
        Setup       = 1,

        /// <summary>Disposes the native texture and cleans up the registered job.</summary>
        Dispose     = 2,

        /// <summary>Runs a job.</summary>
        Run         = 3,
    }

    /// <summary>Data for <see cref="RenderJobEvent.Setup"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RenderJobSetupData
    {
        /// <summary>The GLES ID of the texture which will be the Job's ID and render target.</summary>
        public readonly uint RenderTextureId;

        /// <summary>The width of <see cref="RenderTextureId"/>.</summary>
        public readonly int Width;

        /// <summary>The height of <see cref="RenderTextureId"/>.</summary>
        public readonly int Height;

        /// <summary>Method with signature of <see cref="Callback"/>.</summary>
        public readonly IntPtr OnDone;

        /// <summary>Callback for when the job is setup or the process fails.</summary>
        /// <param name="nativeTexture">The created texture, or 0 if the operation failed.</param>
        /// <param name="renderTextureId"><see cref="RenderTextureId"/>, for lookup.</param>
        public delegate void Callback(uint nativeTexture, uint renderTextureId);

        public RenderJobSetupData(uint renderTextureId, int width, int height, IntPtr onDone)
        {
            RenderTextureId = renderTextureId;
            Width = width;
            Height = height;
            OnDone = onDone;
        }
    }

    /// <summary>Data for <see cref="RenderJobEvent.Run"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RenderJobRunData
    {
        /// <summary>The Job's ID and render target.</summary>
        public readonly uint RenderTextureId;

        /// <summary>Method with signature of <see cref="Callback"/>.</summary>
        public readonly IntPtr OnDone;

        /// <summary>Callback for when the job finishes rendering or the process fails.</summary>
        /// <param name="timestamp">The timestamp returned by the SurfaceTexture, or -1 if the operation failed.</param>
        /// <param name="renderTextureId"><see cref="RenderTextureId"/>, for lookup.</param>
        public delegate void Callback(long timestamp, uint renderTextureId);

        public RenderJobRunData(uint renderTextureId, IntPtr onDone)
        {
            RenderTextureId = renderTextureId;
            OnDone = onDone;
        }
    }

    /// <summary>Data for <see cref="RenderJobEvent.Dispose"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RenderJobDisposeData
    {
        /// <summary>The Job's ID and render target.</summary>
        public readonly uint RenderTextureId;

        /// <summary>Method with signature of <see cref="Callback"/>.</summary>
        public readonly IntPtr OnDone;

        /// <summary>Callback for when the job is disposed or the process fails.</summary>
        /// <param name="result">Whether the operation was a success or not.</param>
        /// <param name="renderTextureId"><see cref="RenderTextureId"/>, for lookup.</param>
        public delegate void Callback(bool result, uint renderTextureId);

        public RenderJobDisposeData(uint renderTextureId, IntPtr onDone)
        {
            RenderTextureId = renderTextureId;
            OnDone = onDone;
        }
    }
}