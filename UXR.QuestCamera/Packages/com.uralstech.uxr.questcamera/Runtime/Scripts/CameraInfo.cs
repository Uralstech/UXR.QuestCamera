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

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// Wrapper for Camera2's CameraCharacteristics.
    /// </summary>
    public record CameraInfo : IDisposable
    {
        #region Enums
        /// <summary>
        /// The camera eye.
        /// </summary>
        public enum CameraEye
        {
            /// <summary>Unknown.</summary>
            Unknown = -1,

            /// <summary>The leftmost camera.</summary>
            Left = 0,

            /// <summary>The rightmost camera.</summary>
            Right = 1,
        }

        /// <summary>
        /// The source of the camera feed.
        /// </summary>
        public enum CameraSource
        {
            /// <summary>Unknown.</summary>
            Unknown = -1,

            /// <summary>Meta Quest Passthrough RGB cameras.</summary>
            PassthroughRGB = 0,
        }
        #endregion

        /// <summary>
        /// Defines the camera's intrinsic properties. All values are in pixels.
        /// </summary>
        public record CameraIntrinsics
        {
            /// <summary>Resolution in pixels.</summary>
            public readonly Vector2 Resolution;

            /// <summary>Focal length in pixels.</summary>
            public readonly Vector2 FocalLength;

            /// <summary>Principal point in pixels from the image's top-left corner.</summary>
            public readonly Vector2 PrincipalPoint;

            /// <summary>Skew coefficient for axis misalignment.</summary>
            public readonly float Skew;

            public CameraIntrinsics(Vector2 resolution, Vector2 focalLength, Vector2 principalPoint, float skew)
            {
                Resolution = resolution;
                FocalLength = focalLength;
                PrincipalPoint = principalPoint;
                Skew = skew;
            }
        }

        /// <summary>
        /// The actual device ID of this camera.
        /// </summary>
        public readonly string CameraId;

        /// <summary>
        /// (Meta Quest) The source of the camera feed.
        /// </summary>
        public readonly CameraSource Source;

        /// <summary>
        /// (Meta Quest) The eye which the camera is closest to.
        /// </summary>
        public readonly CameraEye Eye;

        /// <summary>
        /// The position of the camera's optical center.
        /// </summary>
        public readonly Vector3? LensPoseTranslation;

        /// <summary>
        /// The orientation of the camera relative to the sensor coordinate system.
        /// </summary>
        public readonly Quaternion? LensPoseRotation;

        /// <summary>
        /// The resolutions supported by this camera.
        /// </summary>
        public readonly Resolution[] SupportedResolutions;

        /// <summary>
        /// The intrinsics for this camera.
        /// </summary>
        public readonly CameraIntrinsics? Intrinsics;

        /// <summary>
        /// The native CameraCharacteristics object.
        /// </summary>
        /// <remarks>
        /// The caller is responsible of disposing the returned <see cref="AndroidJavaObject"/>.
        /// </remarks>
        /// <exception cref="ObjectDisposedException">Thrown if the current <see cref="CameraInfo"/> object was disposed.</exception>
        public AndroidJavaObject NativeCameraCharacteristics => _cameraInfo?.Get<AndroidJavaObject>("characteristics") ?? throw new ObjectDisposedException(nameof(CameraInfo));

        private AndroidJavaObject? _cameraInfo;

        public CameraInfo(AndroidJavaObject cameraInfo)
        {
            _cameraInfo = cameraInfo;

            CameraId = cameraInfo.Get<string>("cameraId");
            Source = cameraInfo.GetNullableInt("metaQuestCameraSource") is int source ? (CameraSource)source : CameraSource.Unknown;
            Eye = cameraInfo.GetNullableInt("metaQuestCameraEye") is int eye ? (CameraEye)eye : CameraEye.Unknown;
            LensPoseTranslation = cameraInfo.Get<float[]>("lensPoseTranslation") is float[] translation
                ? new Vector3(translation[0], translation[1], -translation[2]) : null;
            LensPoseRotation = cameraInfo.Get<float[]>("lensPoseRotation") is float[] rotation
                ? new Quaternion(-rotation[0], -rotation[1], rotation[2], rotation[3]) : null;
            SupportedResolutions = Array.ConvertAll(cameraInfo.Get<AndroidJavaObject[]>("supportedResolutions"), size =>
            {
                int width = size.Call<int>("getWidth");
                int height = size.Call<int>("getHeight");
                size.Dispose();

                return new Resolution()
                {
                    width = width,
                    height = height
                };
            });

            if (cameraInfo.Call<int[]>("getIntrinsicsResolution") is int[] intrinsicsResolution
                && cameraInfo.Get<float[]>("intrinsicsFocalLength") is float[] intrinsicsFocalLength
                && cameraInfo.Get<float[]>("intrinsicsPrincipalPoint") is float[] intrinsicsPrincipalPoint
                && cameraInfo.GetNullableFloat("intrinsicsSkew") is float intrinsicsSkew)
            {
                Intrinsics = new CameraIntrinsics(
                    new Vector2(intrinsicsResolution[0], intrinsicsResolution[1]),
                    new Vector2(intrinsicsFocalLength[0], intrinsicsFocalLength[1]),
                    new Vector2(intrinsicsPrincipalPoint[0], intrinsicsPrincipalPoint[1]),
                    intrinsicsSkew
                );
            }
        }

        public static implicit operator string(CameraInfo camera) => camera.CameraId;

        private bool _disposed = false;

        /// <summary>
        /// Releases native plugin resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _cameraInfo?.Dispose();
            _cameraInfo = null;
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}