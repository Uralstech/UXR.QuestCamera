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
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct RenderData
    {
        public static readonly int Size = UnsafeUtility.SizeOf<RenderData>();
        public static readonly int Align = UnsafeUtility.AlignOf<RenderData>();
        
        public readonly IntPtr SourceHardwareBuffer;
        public readonly ulong SourceHardwareBufferId;
        public readonly IntPtr DestinationImage;
        public readonly IntPtr OnDone;

        public delegate void Callback(ulong hardwareBufferId);

        public RenderData(IntPtr sourceHardwareBuffer, ulong sourceHardwareBufferId, IntPtr destinationImage, IntPtr onDone)
        {
            SourceHardwareBuffer = sourceHardwareBuffer;
            SourceHardwareBufferId = sourceHardwareBufferId;
            DestinationImage = destinationImage;
            OnDone = onDone;
        }
    }
}