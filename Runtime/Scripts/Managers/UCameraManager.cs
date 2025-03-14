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
        /// The compute shader to use to convert the camera's YUV 4:2:0 images to RGBA.
        /// </summary>
        public ComputeShader YUVToRGBAComputeShader;

        /// <summary>
        /// Gets the available camera devices. May be null.
        /// </summary>
        public string[] CameraDevices => _camera2Wrapper?.Call<string[]>("getCameraDevices");

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
            _camera2Wrapper?.Dispose();
            _camera2Wrapper = null;
        }

        /// <summary>
        /// Gets the supported resolutions for the specified camera.
        /// </summary>
        /// <param name="camera">The ID of the camera. You can get it from <see cref="CameraDevices"/>.</param>
        public Resolution[] GetSupportedResolutions(string camera)
        {
            string[] rawResolutions = _camera2Wrapper?.Call<string[]>("getSupportedResolutionsForCamera", camera);
            if (rawResolutions is null)
                return null;

            int totalResolutions = rawResolutions.Length;
            Resolution[] resolutions = new Resolution[totalResolutions];

            for (int i = 0; i < totalResolutions; i++)
            {
                string[] resolutionWidthHeight = rawResolutions[i].Split('x');
                resolutions[i] = new Resolution
                {
                    width = int.Parse(resolutionWidthHeight[0]),
                    height = int.Parse(resolutionWidthHeight[1])
                };
            }

            return resolutions;
        }

        /// <summary>
        /// Opens a camera device for use.
        /// </summary>
        /// <remarks>
        /// Once you have finished using the camera, either destroy its GameObject or call <see cref="CameraDevice.Release"/>
        /// to close the camera and free up native resources.
        /// </remarks>
        /// <param name="camera">The ID of the camera. You can get it from <see cref="CameraDevices"/>.</param>
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