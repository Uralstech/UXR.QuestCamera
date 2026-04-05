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

using UnityEngine;
using UnityEngine.Experimental.Rendering;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    internal static class GraphicsUtils
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0022:Use expression body for method", Justification = "Method is multi-line.")]
        public static bool IsGraphicsFormatSupportedForRender(GraphicsFormat format)
        {
#if UNITY_6000_0_OR_NEWER
            return SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render);
#else
            return SystemInfo.IsFormatSupported(format, FormatUsage.Render);
#endif
        }
    }
}