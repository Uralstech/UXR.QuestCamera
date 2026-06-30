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
    public static class VkAPI
    {
        [DllImport("UXRQC_VkGluePlugin")]
        public static extern void releaseHardwareBuffer(IntPtr acquiredBufferPtr);
        
        [DllImport("GfxPlugin_UXRQC_VulkanPlugin")]
        public static extern IntPtr getVulkanRenderEvent();
        
        public static readonly ConcurrentDictionary<long, IntPtr> RenderCallbacksRegistry = new();
        public static readonly IntPtr RenderCallbackPtr = Marshal.GetFunctionPointerForDelegate<RenderData.Callback>(OnRenderDone);
        
        public static unsafe IntPtr AllocateRenderData(RenderData renderData)
        {
            void* allocated = UnsafeUtility.Malloc(RenderData.Size, RenderData.Align, Allocator.TempJob);
            UnsafeUtility.CopyStructureToPtr(ref renderData, allocated);
            return new IntPtr(allocated);
        }
        
        public static unsafe bool TryFreeRenderData(long hardwareBufferId)
        {
            if (!RenderCallbacksRegistry.TryRemove(hardwareBufferId, out IntPtr dataToFree))
                return false;
            
            UnsafeUtility.Free((void*)dataToFree, Allocator.TempJob);
            return true;
        }
        
        [MonoPInvokeCallback(typeof(RenderData.Callback))]
        public static void OnRenderDone(ulong hardwareBufferId)
        {
            if (!TryFreeRenderData((long)hardwareBufferId))
                Debug.LogWarning($"Dangling {nameof(OnRenderDone)} for hardware buffer ID {hardwareBufferId}.");
        }
    }
}