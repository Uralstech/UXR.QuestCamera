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

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    internal static class AndroidAPILevel
    {
        private static int s_currentAPILevel = -1;

        public const int VanillaIceCream = 35;
        public const int Tiramisu = 33;

        public static int Current
        {
            get
            {
                if (s_currentAPILevel > 0)
                    return s_currentAPILevel;

                using AndroidJavaClass version = new("android.os.Build$VERSION");
                return s_currentAPILevel = version.GetStatic<int>("SDK_INT");
            }
        }
    }
}