// Copyright(c) Meta Platforms, Inc. and affiliates.
// All rights reserved.
// 
// Licensed under the Oculus SDK License Agreement (the "License");
// you may not use the Oculus SDK except in compliance with the License,
// which is provided at the time of installation or download, or which
// otherwise accompanies this software in either electronic or hard copy form.
// 
// You may obtain a copy of the License at
// 
// https://developer.oculus.com/licenses/oculussdk/
// 
// Unless required by applicable law or agreed to in writing, the Oculus SDK
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if META_XR_SDK_CORE
using UnityEngine;

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// Utility to check if the current Meta Quest device supports the Passthrough Camera API.
    /// </summary>
    /// <remarks>
    /// Requires the Meta XR Core SDK.
    /// </remarks>
    public static class CameraSupport
    {

        // The Horizon OS starts supporting PCA with v74.
        public const int MINSUPPORTOSVERSION = 74;

        private static bool? s_isSupported;
        private static int? s_horizonOsVersion;

        /// <summary>
        /// Get the Horizon OS version number on the headset
        /// </summary>
        /// <remarks>
        /// Requires the Meta XR Core SDK.
        /// </remarks>
        public static int? HorizonOSVersion
        {
            get
            {
                if (!s_horizonOsVersion.HasValue)
                {
                    AndroidJavaClass vrosClass = new("vros.os.VrosBuild");
                    s_horizonOsVersion = vrosClass.CallStatic<int>("getSdkVersion");
#if OVR_INTERNAL_CODE
                    // 10000 means that the build doesn't have a proper release version, and it is still in Mainline,
                    // not in a release branch.
#endif // OVR_INTERNAL_CODE
                    if (s_horizonOsVersion == 10000)
                    {
                        s_horizonOsVersion = -1;
                    }
                }

                return s_horizonOsVersion.Value != -1 ? s_horizonOsVersion.Value : null;
            }
        }

        /// <summary>
        /// Returns true if the current headset supports Passthrough Camera API
        /// </summary>
        /// <remarks>
        /// Requires the Meta XR Core SDK.
        /// </remarks>
        public static bool IsSupported
        {
            get
            {
                if (!s_isSupported.HasValue)
                {
                    OVRPlugin.SystemHeadset headset = OVRPlugin.GetSystemHeadsetType();
                    bool isSupported = (headset == OVRPlugin.SystemHeadset.Meta_Quest_3 ||
                                        headset == OVRPlugin.SystemHeadset.Meta_Quest_3S) &&
                                        (!HorizonOSVersion.HasValue || HorizonOSVersion >= MINSUPPORTOSVERSION);

                    s_isSupported = isSupported;
                }

                return s_isSupported.Value;
            }
        }
    }
}

#endif