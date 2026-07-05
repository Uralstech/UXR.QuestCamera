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
using Unity.Collections.LowLevel.Unsafe;

#nullable enable
namespace Uralstech.UXR.QuestCamera.Vulkan
{
    /// <summary>Data for a <see cref="VulkanAPI"/> render event.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RenderData
    {
        /// <summary>Size of this struct (<see cref="UnsafeUtility.SizeOf{T}()"/>).</summary>
        public static readonly int Size = UnsafeUtility.SizeOf<RenderData>();
        
        /// <summary>Alignment of this struct (<see cref="UnsafeUtility.AlignOf{T}()"/>).</summary>
        public static readonly int Align = UnsafeUtility.AlignOf<RenderData>();
        
        /// <summary>The source AHardwareBuffer to render from.</summary>
        public readonly IntPtr SourceHardwareBuffer;
        
        /// <summary>The data space of <see cref="SourceHardwareBuffer"/>.</summary>
        public readonly int SourceHardwareBufferDataSpace;
        
        /// <summary>The global unique ID of <see cref="SourceHardwareBuffer"/>.</summary>
        public readonly ulong SourceHardwareBufferId;
        
        /// <summary>The target Unity-created VkImage to render to.</summary>
        public readonly IntPtr DestinationImage;
        
        /// <summary>Method with signature of <see cref="Callback"/>.</summary>
        public readonly IntPtr OnDone;

        /// <summary>Callback for when the event completes.</summary>
        /// <param name="success">Was the event executed successfully?</param>
        /// <param name="hardwareBufferId"><see cref="SourceHardwareBufferId"/>, for lookup.</param>
        public delegate void Callback(byte success, ulong hardwareBufferId);

        public RenderData(IntPtr sourceHardwareBuffer, int sourceHardwareBufferDataSpace,
            ulong sourceHardwareBufferId, IntPtr destinationImage, IntPtr onDone)
        {
            SourceHardwareBuffer = sourceHardwareBuffer;
            SourceHardwareBufferDataSpace = sourceHardwareBufferDataSpace;
            SourceHardwareBufferId = sourceHardwareBufferId;
            DestinationImage = destinationImage;
            OnDone = onDone;
        }
    }
        
    /// <summary>A managed callback for a <see cref="VulkanAPI"/> render event.</summary>
    public readonly struct ManagedRenderCallback
    {
        /// <summary>The native memory allocated for event data.</summary>
        public readonly IntPtr RenderDataMemory;
        
        /// <summary>The timestamp of the frame being rendered, in nanoseconds.</summary>
        public readonly long TimestampNs;

        /// <summary>The managed callback (bool Success, long <see cref="TimestampNs"/>).</summary>
        /// <remarks>This will be invoked from the render thread, do not call Unity APIs from it.</remarks>
        public readonly Action<bool, long> OnDone;

        public ManagedRenderCallback(IntPtr renderDataMemory, long timestampNs, Action<bool, long> onDone)
        {
            RenderDataMemory = renderDataMemory;
            TimestampNs = timestampNs;
            OnDone = onDone;
        }
    }
}