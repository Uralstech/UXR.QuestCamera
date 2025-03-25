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
        public CameraInfo[] Cameras
        {
            get
            {
                if (_cameraInfosCached is not null)
                    return _cameraInfosCached;

                AndroidJavaObject[] nativeCameraInfos = _camera2Wrapper?.Call<AndroidJavaObject[]>("getCameraDevices");
                if (nativeCameraInfos is null)
                    return null;

                int cameraInfoCount = nativeCameraInfos.Length;

                _cameraInfosCached = new CameraInfo[cameraInfoCount];
                for (int i = 0; i < cameraInfoCount; i++)
                    _cameraInfosCached[i] = new CameraInfo(nativeCameraInfos[i]);

                return _cameraInfosCached;
            }
        }

        private CameraInfo[] _cameraInfosCached = null;
        private AndroidJavaObject _camera2Wrapper;

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
        /// Gets a camera device by the eye it is closest to.
        /// </summary>
        /// <param name="eye">The eye.</param>
        /// <returns>The camera's <see cref="CameraInfo"/>, <see langword="null"/> if not found.</returns>
        public CameraInfo GetCamera(CameraInfo.CameraEye eye)
        {
            foreach (CameraInfo cameraInfo in Cameras)
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
        /// Once you have finished using the camera, either destroy its GameObject or call <see cref="CameraDevice.Release"/>
        /// to close the camera and free up native resources.
        /// </remarks>
        /// <param name="camera">The ID of the camera. You can get it from <see cref="Cameras"/> or <see cref="GetCamera(CameraInfo.CameraEye)"/>.</param>
        /// <returns>A new camera device wrapper. May be null if the current object is disposed/unusable.</returns>
        public CameraDevice OpenCamera(string camera)
        {
            GameObject wrapperGO = new($"{nameof(CameraDevice)} ({camera}, {DateTime.UtcNow.Ticks})");
            
            AndroidJavaObject nativeObject = _camera2Wrapper?.Call<AndroidJavaObject>("openCameraDevice", camera, wrapperGO.name);
            if (nativeObject is null)
            {
                Destroy(wrapperGO);
                return null;
            }

            CameraDevice wrapper = wrapperGO.AddComponent<CameraDevice>();
            wrapper.SetCameraDevice(nativeObject);
            return wrapper;
        }
    }
}