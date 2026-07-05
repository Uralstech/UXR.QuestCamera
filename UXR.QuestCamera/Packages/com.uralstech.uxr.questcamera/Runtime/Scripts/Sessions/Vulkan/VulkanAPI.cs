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
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera.Vulkan
{
    /// <summary>Exposes the native Vulkan Texture Conversion API.</summary>
    public static class VulkanAPI
    {
        /// <summary>Releases an acquired AHardwareBuffer object.</summary>
        /// <param name="acquiredBufferPtr">The buffer to release.</param>
        [DllImport("UXRQC_VulkanGluePlugin")]
        public static extern void releaseHardwareBuffer(IntPtr acquiredBufferPtr);
        
        /// <summary>Returns a pointer to the native rendering function.</summary>
        [DllImport("GfxPlugin_UXRQC_VulkanPlugin")]
        public static extern IntPtr getVulkanRenderEvent();
        
        /// <summary>Registry of render callbacks.</summary>
        public static readonly ConcurrentDictionary<long, ManagedRenderCallback> RenderCallbacksRegistry = new();
        
        /// <summary>Static marshalled pointer to <see cref="OnRenderDone"/>.</summary>
        public static readonly IntPtr RenderCallbackPtr = Marshal.GetFunctionPointerForDelegate<RenderData.Callback>(OnRenderDone);
        
        /// <summary>Allocates native memory of a <see cref="RenderData"/> struct to use with <see cref="OnRenderDone"/>/<see cref="FreeRenderData"/>.</summary>
        /// <remarks>
        /// Allocations are made with <see cref="UnsafeUtility"/> and <see cref="Allocator.TempJob"/>,
        /// matching de-allocation in <see cref="OnRenderDone"/> and <see cref="FreeRenderData"/>.
        /// </remarks>
        /// <param name="renderData">The data to allocate.</param>
        /// <returns>A pointer to the allocated memory.</returns>
        public static unsafe IntPtr AllocateRenderData(ref RenderData renderData)
        {
            void* allocated = UnsafeUtility.Malloc(RenderData.Size, RenderData.Align, Allocator.TempJob);
            UnsafeUtility.CopyStructureToPtr(ref renderData, allocated);
            return new IntPtr(allocated);
        }

        /// <summary>Frees the memory of a <see cref="RenderData"/> struct allocated using <see cref="AllocateRenderData"/>.</summary>
        /// <param name="renderDataPtr">The memory to free.</param>
        public static unsafe void FreeRenderData(in IntPtr renderDataPtr) =>
            UnsafeUtility.Free((void*)renderDataPtr, Allocator.TempJob);

        /// <inheritdoc cref="RenderData.Callback"/>
        [MonoPInvokeCallback(typeof(RenderData.Callback))]
        public static void OnRenderDone(byte success, ulong hardwareBufferId)
        {
            if (!RenderCallbacksRegistry.TryRemove((long)hardwareBufferId, out ManagedRenderCallback callback))
            {
                Debug.LogWarning($"Dangling {nameof(OnRenderDone)} for hardware buffer ID {hardwareBufferId}.");
                return;
            }

            FreeRenderData(callback.RenderDataMemory);
            callback.OnDone(success != 0, callback.TimestampNs);
        }
    }
}