// Copyright 2025 URAV ADVANCED LEARNING SYSTEMS PRIVATE LIMITED
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
using UnityEngine;
using Uralstech.Utils.Singleton;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// Class for interfacing with the native Camera2 API on Android.
    /// </summary>
    [AddComponentMenu("Uralstech/UXR/Quest Camera/Quest Camera Manager")]
    public class UCameraManager : DontCreateNewSingleton<UCameraManager>
    {
        /// <summary>
        /// The permission required to access the Meta Quest's cameras.
        /// </summary>
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

        /// <summary>
        /// The permission required to access the Meta Quest Avatar Camera.
        /// </summary>
        public const string AvatarCameraPermission = "android.permission.CAMERA";

        /// <summary>
        /// The compute shader to use to convert the camera's YUV 4:2:0 images to RGBA.
        /// </summary>
        public ComputeShader YUVToRGBAComputeShader;

        /// <summary>
        /// Returns all available cameras and their characteristics. This is a cached value.
        /// </summary>
        public CameraInfo[]? Cameras => _cameraInfosCached is not null ? _cameraInfosCached : (_cameraInfosCached = GetCameraInfos());

        private CameraInfo[]? _cameraInfosCached = null;
        private AndroidJavaObject? _camera2Wrapper;

        protected override void Awake()
        {
            base.Awake();
            DontDestroyOnLoad(gameObject);

            using AndroidJavaClass camera2WrapperClass = new("com.uralstech.ucamera.Camera2Wrapper");
            _camera2Wrapper = camera2WrapperClass.CallStatic<AndroidJavaObject>("getInstance");
        }

        protected void OnDestroy()
        {
            if (_cameraInfosCached is not null)
            {
                int cameraInfoCount = _cameraInfosCached.Length;
                for (int i = 0; i < cameraInfoCount; i++)
                    _cameraInfosCached[i].Dispose();

                _cameraInfosCached = null;
            }

            _camera2Wrapper?.Dispose();
            _camera2Wrapper = null;
        }

        /// <summary>
        /// Gets all available cameras and their characteristics. This is <b>not</b> cached.
        /// </summary>
        /// <remarks>
        /// <see cref="CameraInfo"/> implements <see cref="IDisposable"/>, so make sure to dispose every returned value.
        /// </remarks>
        /// <returns>An array of <see cref="CameraInfo"/> objects or <see langword="null"/> if any errors occurred.</returns>
        public CameraInfo[]? GetCameraInfos()
        {
            AndroidJavaObject[]? nativeObjects = _camera2Wrapper?.Call<AndroidJavaObject[]>("getCameraDevices");
            if (nativeObjects is null)
            {
                Debug.LogError("Could not get camera device information.");
                return null;
            }

            int count = nativeObjects.Length;
            CameraInfo[] wrappers = new CameraInfo[count];

            for (int i = 0; i < count; i++)
                wrappers[i] = new CameraInfo(nativeObjects[i]);

            return wrappers;
        }

        /// <summary>
        /// Gets a camera device by the eye it is closest to.
        /// </summary>
        /// <param name="eye">The eye.</param>
        /// <returns>A <see cref="CameraInfo"/> object or <see langword="null"/> if none were found.</returns>
        public CameraInfo? GetCamera(CameraInfo.CameraEye eye)
        {
            if (Cameras is not CameraInfo[] cameras)
                return null;

            foreach (CameraInfo cameraInfo in cameras)
            {
                if (cameraInfo.Eye == eye)
                    return cameraInfo;
            }

            return null;
        }

        /// <summary>
        /// Opens a camera device for use.
        /// </summary>
        /// <remarks>
        /// Once you have finished using the camera, close the camera using <see cref="CameraDevice.Close()"/>
        /// or <see cref="CameraDevice.CloseAsync(System.Threading.CancellationToken)"/> and dispose it using
        /// <see cref="CameraDevice.Dispose()"/> to release all of its native resources.
        /// </remarks>
        /// <param name="camera">The ID of the camera to open. You can get it from <see cref="Cameras"/> or <see cref="GetCamera(CameraInfo.CameraEye)"/>.</param>
        /// <returns>A new camera device wrapper or <see langword="null"/> if any errors occurred.</returns>
        public CameraDevice? OpenCamera(string camera)
        {
            CameraDevice cameraDevice = new();
            AndroidJavaObject? nativeObject = _camera2Wrapper?.Call<AndroidJavaObject>("openCameraDevice", camera, cameraDevice);
            if (nativeObject is null)
            {
                cameraDevice.Dispose();
                return null;
            }

            cameraDevice._cameraDevice = nativeObject;
            return cameraDevice;
        }
    }
}