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
using System.Text;
using UnityEngine;

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// Wrapper for Camera2's CameraCharacteristics.
    /// </summary>
    public class CameraInfo
    {
        /// <summary>
        /// The camera eye.
        /// </summary>
        public enum CameraEye
        {
            /// <summary>Unknown.</summary>
            Unknown = 0,

            /// <summary>The leftmost camera.</summary>
            Left = 1,

            /// <summary>The rightmost camera.</summary>
            Right = 2,
        }

        /// <summary>
        /// The source of the camera feed.
        /// </summary>
        public enum CameraSource
        {
            /// <summary>Unknown.</summary>
            Unknown = 0,

            /// <summary>Meta Quest Passthrough RGB cameras.</summary>
            PassthroughRGB = 1,
        }

        /// <summary>
        /// Defines the camera's intrinsic properties. All values are in pixels.
        /// </summary>
        public readonly struct CameraIntrinsics
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

            /// <inheritdoc/>
            public readonly override string ToString()
            {
                return new StringBuilder()
                    .Append('{')
                    .Append($"{nameof(Resolution)}: {Resolution}, ")
                    .Append($"{nameof(FocalLength)}: {FocalLength}, ")
                    .Append($"{nameof(PrincipalPoint)}: {PrincipalPoint}, ")
                    .Append($"{nameof(Skew)}: {Skew}")
                    .Append('}')
                    .ToString();
            }
        }

        /// <summary>
        /// The actual device ID of this camera.
        /// </summary>
        public string CameraId => _cameraInfo?.Get<string>("cameraId") ?? throw new ObjectDisposedException(nameof(CameraInfo));

        /// <summary>
        /// The native CameraCharacteristics object.
        /// </summary>
        public AndroidJavaObject NativeCameraCharacteristics => _cameraInfo?.Get<AndroidJavaObject>("characteristics") ?? throw new ObjectDisposedException(nameof(CameraInfo));

        /// <summary>
        /// (Meta Quest) The source of the camera feed.
        /// </summary>
        public CameraSource Source =>
            _cameraInfo?.Get<int>("metaQuestCameraSource") is int value ? (CameraSource)(value + 1) : throw new ObjectDisposedException(nameof(CameraInfo));

        /// <summary>
        /// (Meta Quest) The eye which the camera is closest to.
        /// </summary>
        public CameraEye Eye =>
            _cameraInfo?.Get<int>("metaQuestCameraEye") is int value ? (CameraEye)(value + 1) : throw new ObjectDisposedException(nameof(CameraInfo));

        /// <summary>
        /// The position of the camera optical center.
        /// </summary>
        public Vector3 LensPoseTranslation =>
            _cameraInfo?.Get<float[]>("lensPoseTranslation") is float[] value ? new Vector3(value[0], value[1], -value[2]) : Vector3.zero;

        /// <summary>
        /// The orientation of the camera relative to the sensor coordinate system.
        /// </summary>
        public Quaternion LensPoseRotation =>
            _cameraInfo?.Get<float[]>("lensPoseRotation") is float[] value ? new Quaternion(-value[0], -value[1], value[2], value[3]) : Quaternion.identity;

        /// <summary>
        /// The resolutions supported by this camera.
        /// </summary>
        public Resolution[] SupportedResolutions
        {
            get
            {
                if (_cameraInfo?.Get<AndroidJavaObject[]>("supportedResolutions") is not AndroidJavaObject[] value)
                    throw new ObjectDisposedException(nameof(CameraInfo));

                int resolutionsCount = value.Length;

                Resolution[] resolutions = new Resolution[resolutionsCount];
                for (int i = 0; i < resolutionsCount; i++)
                {
                    AndroidJavaObject nativeSize = value[i];
                    resolutions[i] = new Resolution()
                    {
                        width = nativeSize.Call<int>("getWidth"),
                        height = nativeSize.Call<int>("getHeight")
                    };

                    nativeSize.Dispose();
                }

                return resolutions;
            }
        }

        /// <summary>
        /// The intrinsics for this camera.
        /// </summary>
        public CameraIntrinsics Intrinsics
        {
            get
            {
                int[] resolution = _cameraInfo?.Get<int[]>("intrinsicsResolution");
                float[] focalLength = _cameraInfo?.Get<float[]>("intrinsicsFocalLength");
                float[] principalPoint = _cameraInfo?.Get<float[]>("intrinsicsPrincipalPoint");
                float? skew = _cameraInfo?.Get<float>("intrinsicsSkew");

                return resolution is not null && focalLength is not null && principalPoint is not null && skew is not null && skew != float.NegativeInfinity
                    ? new CameraIntrinsics(
                        new Vector2(resolution[0], resolution[1]),
                        new Vector2(focalLength[0], focalLength[1]),
                        new Vector2(principalPoint[0], principalPoint[1]),
                        skew.Value
                    ) : default;
            }
        }

        private AndroidJavaObject _cameraInfo;

        public CameraInfo(AndroidJavaObject cameraInfo)
        {
            _cameraInfo = cameraInfo;
        }

        public static implicit operator string(CameraInfo camera)
        {
            return camera.CameraId;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            using AndroidJavaObject nativeObject = NativeCameraCharacteristics;
            return new StringBuilder()
                .Append('{')
                .Append($"{nameof(CameraId)}: {CameraId}, ")
                .Append($"{nameof(NativeCameraCharacteristics)}: {NativeCameraCharacteristics}, ")
                .Append($"{nameof(Source)}: {Source}, ")
                .Append($"{nameof(Eye)}: {Eye}, ")
                .Append($"{nameof(LensPoseTranslation)}: {LensPoseTranslation}, ")
                .Append($"{nameof(LensPoseRotation)}: {LensPoseRotation}, ")
                .Append($"{nameof(SupportedResolutions)}: [{string.Join(", ", SupportedResolutions)}], ")
                .Append($"{nameof(Intrinsics)}: {Intrinsics}")
                .Append('}')
                .ToString();
        }

        /// <inheritdoc/>
        internal void Dispose()
        {
            _cameraInfo?.Dispose();
            _cameraInfo = null;

            GC.SuppressFinalize(this);
        }
    }
}