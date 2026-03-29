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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.Android;
using Uralstech.Utils.Singleton;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>Entry point for the native Camera2 plugin.</summary>
    [AddComponentMenu("Uralstech/UXR/Quest Camera/Quest Camera Manager")]
    public sealed class QuestCameraManager : DontCreateNewSingleton<QuestCameraManager>
    {
        /// <summary>Meta Quest Passthrough Camera API permission string.</summary>
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

        /// <summary>Meta Quest Avatar Camera API permission string.</summary>
        public const string AvatarCameraPermission = "android.permission.CAMERA";

        /// <summary>Tries to get the runtime's support for the Passthrough Camera Access and Camera2 APIs.</summary>
        public static PCASupport Support => GetPassthroughCameraSupport();

        private static PCASupport GetPassthroughCameraSupport()
        {
            #if META_XR_SDK_CORE

            OVRPlugin.SystemHeadset headset = OVRPlugin.GetSystemHeadsetType();
            if (headset is not OVRPlugin.SystemHeadset.Meta_Quest_3
                        and not OVRPlugin.SystemHeadset.Meta_Quest_3S)
            {
                return PCASupport.Unsupported;
            }

            const int MinHorizonOSVersion = 74;
            
            using AndroidJavaClass osBuild = new("vros.os.VrosBuild");
            int version = osBuild.CallStatic<int>("getSdkVersion");
            return version >= MinHorizonOSVersion
                ? PCASupport.Supported
                : PCASupport.Unsupported;

            #elif !UNITY_ANDROID || UNITY_EDITOR

            return PCASupport.Unsupported;

            #else

            return PCASupport.Unknown;
            
            #endif
        }
    
        /// <summary>The shader and kernel to use for YUV 4:2:0 to RGBA conversion.</summary>
        [Tooltip("The shader and kernel to use for YUV 4:2:0 to RGBA conversion.")]
        public ComputeShaderKernel ConversionKernel;

        /// <summary>A managed, cached array of available cameras and their characteristics.</summary>
        public IReadOnlyList<CameraInfo> Cameras => _managedInfos ??= GetDevices();

        private CameraInfo[]? _managedInfos;
        private AndroidJavaObject _native;

        protected override void Awake()
        {
            base.Awake();
            DontDestroyOnLoad(gameObject);

            const string ClassName = "com.uralstech.uxr.questcamera.CameraDeviceProvider";
            using AndroidJavaClass deviceProviderCls = new(ClassName);

#if UNITY_6000_0_OR_NEWER
            AndroidJavaObject currentContext = AndroidApplication.currentContext;
#else
            using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject currentContext = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
#endif

            _native = deviceProviderCls.CallStatic<AndroidJavaObject>("getInstance", currentContext);
        }

        private void OnDestroy()
        {
            DisposeManagedCameraInfos();
            _native.Dispose();
        }

        /// <summary>Gets the IDs and intrinsics of all connected camera devices.</summary>
        public CameraInfo[] GetDevices()
        {
            AndroidJavaObject[] cameraCharacteristicsProviders = _native.Call<AndroidJavaObject[]>("getDevices");
            return Array.ConvertAll(cameraCharacteristicsProviders, static provider =>
            {
                CameraInfo cameraInfo = new(provider);
                provider.Dispose();
                return cameraInfo;
            });
        }

        /// <summary>Refreshes cached camera device information.</summary>
        public void RefreshDevices()
        {
            DisposeManagedCameraInfos();
            _managedInfos = GetDevices();
        }

        /// <summary>Finds a camera device by its corresponding eye.</summary>
        public bool TryGetDevice(CameraInfo.CameraEye eye, [NotNullWhen(true)] out CameraInfo? cameraInfo)
        {
            foreach (CameraInfo val in Cameras)
            {
                if (val.Eye == eye)
                {
                    cameraInfo = val;
                    return true;
                }
            }

            cameraInfo = null;
            return false;
        }

        /// <summary>Opens a camera device for use.</summary>
        /// <remarks>Once you have finished using the camera, close and dispose of it using <see cref="CameraDevice.DisposeAsync()"/>.</remarks>
        /// <param name="camera">The camera to open.</param>
        public CameraDevice OpenCamera(CameraInfo cameraInfo) => OpenCamera(cameraInfo.CameraId);

        /// <inheritdoc cref="OpenCamera(CameraInfo)"/>
        /// <param name="cameraId">The ID of the camera to open.</param>
        public CameraDevice OpenCamera(string cameraId)
        {
            CameraDevice cameraDevice = new(cameraId);
            _native.Call("openCamera", cameraDevice._native, cameraId);

            return cameraDevice;
        }

        private void DisposeManagedCameraInfos()
        {
            if (_managedInfos != null)
                Array.ForEach(_managedInfos, static cameraInfo => cameraInfo.Dispose());

            _managedInfos = null;
        }
    }
}