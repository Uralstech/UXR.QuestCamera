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

using AOT;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera.GLES
{
    /// <summary>Exposes the native GLES Texture Conversion API.</summary>
    public static class GLESAPI
    {
        /// <summary>Returns a pointer to the native render job management function.</summary>
        [DllImport("UXRQC_NativeConverters")]
        public static extern IntPtr getGLESManageConverterJobEvent();

        /// <summary>Registry of job setup callbacks. This is a single-call registry, i.e. the entry is removed after the callback occurs.</summary>
        public static readonly ConcurrentDictionary<uint, RenderJobSetupData.Callback>      SetupCallbacksRegistry     = new();

        /// <summary>Registry of job disposal callbacks. This is a single-call registry, i.e. the entry is removed after the callback occurs.</summary>
        public static readonly ConcurrentDictionary<uint, RenderJobDisposeData.Callback>    DisposeCallbacksRegistry   = new();

        /// <summary>Registry of job run callbacks.</summary>
        public static readonly ConcurrentDictionary<uint, RenderJobRunData.Callback>        RunCallbacksRegistry       = new();

        /// <summary>Static marshalled pointer to <see cref="OnRenderJobSetup"/>.</summary>
        public static readonly IntPtr RenderJobSetupCallbackPtr     = Marshal.GetFunctionPointerForDelegate<RenderJobSetupData.Callback>(OnRenderJobSetup);

        /// <summary>Static marshalled pointer to <see cref="OnRenderJobDispose"/>.</summary>
        public static readonly IntPtr RenderJobDisposeCallbackPtr   = Marshal.GetFunctionPointerForDelegate<RenderJobDisposeData.Callback>(OnRenderJobDispose);

        /// <summary>Static marshalled pointer to <see cref="OnRenderJobRun"/>.</summary>
        public static readonly IntPtr RenderJobRunCallbackPtr       = Marshal.GetFunctionPointerForDelegate<RenderJobRunData.Callback>(OnRenderJobRun);

        /// <inheritdoc cref="RenderJobSetupData.Callback"/>
        [MonoPInvokeCallback(typeof(RenderJobSetupData.Callback))]
        public static void OnRenderJobSetup(uint nativeTextureId, uint renderTextureId)
        {
            GL.InvalidateState();
            if (SetupCallbacksRegistry.TryRemove(renderTextureId, out RenderJobSetupData.Callback? callback))
                callback.Invoke(nativeTextureId, renderTextureId);
            else
                Debug.LogWarning($"Dangling {nameof(OnRenderJobSetup)} for render texture ID {renderTextureId}.");
        }

        /// <inheritdoc cref="RenderJobDisposeData.Callback"/>
        [MonoPInvokeCallback(typeof(RenderJobDisposeData.Callback))]
        public static void OnRenderJobDispose(bool result, uint renderTextureId)
        {
            if (DisposeCallbacksRegistry.TryRemove(renderTextureId, out RenderJobDisposeData.Callback? callback))
                callback.Invoke(result, renderTextureId);
            else
                Debug.LogWarning($"Dangling {nameof(OnRenderJobDispose)} for render texture ID {renderTextureId}.");
        }

        /// <inheritdoc cref="RenderJobRunData.Callback"/>
        [MonoPInvokeCallback(typeof(RenderJobRunData.Callback))]
        public static void OnRenderJobRun(long timestamp, uint renderTextureId)
        {
            GL.InvalidateState();
            if (RunCallbacksRegistry.TryGetValue(renderTextureId, out RenderJobRunData.Callback? callback))
                callback.Invoke(timestamp, renderTextureId);
            else
                Debug.LogWarning($"Dangling {nameof(OnRenderJobRun)} for render texture ID {renderTextureId}.");
        }
    }
}