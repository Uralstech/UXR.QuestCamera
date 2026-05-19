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
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>Information describing a camera device.</summary>
    public sealed class CameraInfo : CameraMetadata, IEquatable<CameraInfo>
    {
        private const int ImageFormatYUV420888 = 35;

        /// <summary>The camera eye.</summary>
        public enum CameraEye { Unknown = -1, Left = 0, Right = 1, }

        /// <summary>The source of the camera feed.</summary>
        public enum CameraSource { Unknown = -1, PassthroughRGB = 0 }

        /// <summary>
        /// Defines the camera's intrinsic properties. All values are in pixels.
        /// </summary>
        /// <param name="Resolution">Resolution in pixels.</param>
        /// <param name="FocalLength">Focal length in pixels.</param>
        /// <param name="PrincipalPoint">Principal point in pixels from the image's top-left corner.</param>
        /// <param name="Skew">Skew coefficient for axis misalignment.</param>
        public sealed record CameraIntrinsics(
            Vector2 Resolution,
            Vector2 FocalLength,
            Vector2 PrincipalPoint,
            float Skew
        );
        
        /// <summary>The device ID of the camera.</summary>
        public readonly string CameraId;

        /// <summary>The source of the camera feed.</summary>
        public readonly CameraSource Source;

        /// <summary>The eye the camera is closest to.</summary>
        public readonly CameraEye Eye;

        /// <summary>The position of the camera's optical center, converted from Android sensor space to Unity space.</summary>
        public readonly Vector3? LensPoseTranslation;

        /// <summary>The orientation of the camera relative to the sensor coordinate system, converted from Android sensor rotation space to Unity rotation space.</summary>
        public readonly Quaternion? LensPoseRotation;

        /// <summary>The resolutions supported by this camera.</summary>
        public readonly Resolution[] SupportedResolutions;

        /// <summary>The stream use cases supported by this camera, if any.</summary>
        /// <remarks>Guaranteed to an empty array on devices &lt; API Level 33.</remarks>
        public readonly StreamUseCase[] SupportedStreamUseCases;

        /// <summary>The intrinsic data for this camera.</summary>
        public readonly CameraIntrinsics? Intrinsics;

        public CameraInfo(string cameraId, AndroidJavaObject native,
            Key? metaQuestSourceKey = null, Key? metaQuestPositionKey = null) : base(native, "android.hardware.camera2.CameraCharacteristics")
        {
            CameraId = cameraId;

            Source = metaQuestSourceKey != null && TryGet(metaQuestSourceKey, out int src) ? (CameraSource)src : CameraSource.Unknown;
            Eye = metaQuestPositionKey != null && TryGet(metaQuestPositionKey, out int eye) ? (CameraEye)eye : CameraEye.Unknown;

            LensPoseTranslation = TryGet<float[]>("LENS_POSE_TRANSLATION", out float[]? t) ? new(t[0], t[1], -t[2]) : default;
            LensPoseRotation = TryGet<float[]>("LENS_POSE_ROTATION", out float[]? r) ? new(-r[0], -r[1], r[2], r[3]) : default;

            if (!TryGet<AndroidJavaObject>("SCALER_STREAM_CONFIGURATION_MAP", out AndroidJavaObject? nativeStreamConfigMap))
                throw new ArgumentException("Could not get required value of 'SCALER_STREAM_CONFIGURATION_MAP' from CameraCharacteristics.");

            using (nativeStreamConfigMap)
            {
                using AndroidJavaObject outputSizes = nativeStreamConfigMap.Call<AndroidJavaObject>("getOutputSizes", ImageFormatYUV420888);
                SupportedResolutions = outputSizes.ToManaged<Resolution[]>();
            }

            SupportedStreamUseCases = AndroidAPILevel.Current >= AndroidAPILevel.Tiramisu
             && TryGet<long[]>("SCALER_AVAILABLE_STREAM_USE_CASES", out long[]? useCases)
                ? Array.ConvertAll(useCases, static useCase => (StreamUseCase)useCase)
                : Array.Empty<StreamUseCase>();

            if (TryGet("SENSOR_INFO_PRE_CORRECTION_ACTIVE_ARRAY_SIZE", out RectInt iRes)
             && TryGet<float[]>("LENS_INTRINSIC_CALIBRATION", out float[]? iCal))
            {
                Intrinsics = new CameraIntrinsics(
                    Resolution:     new(iRes.width, iRes.height),
                    FocalLength:    new(iCal[0], iCal[1]),
                    PrincipalPoint: new(iCal[2], iCal[3]),
                    Skew:           iCal[4]
                );
            }
        }

        /// <summary>Returns the keys supported by this CameraDevice for querying with a CaptureRequest.</summary>
        public Key[] GetAvailableCaptureRequestKeys()
        {
            ThrowIfDisposed();
            return GetKeysFromListNative("getAvailableCaptureRequestKeys");
        }

        /// <summary>Returns the keys supported by this CameraDevice for querying with a CaptureResult.</summary>
        public Key[] GetAvailableCaptureResultKeys()
        {
            ThrowIfDisposed();
            return GetKeysFromListNative("getAvailableCaptureResultKeys");
        }

        /// <summary>Returns the keys for this CameraDevice whose values are capture session specific.</summary>
        /// <remarks>Returns an empty array on devices &lt; API Level 35.</remarks>
        public Key[] GetAvailableSessionCharacteristicsKeys()
        {
            ThrowIfDisposed();

            return AndroidAPILevel.Current >= AndroidAPILevel.VanillaIceCream
                ? GetKeysFromListNative("getAvailableSessionCharacteristicsKeys")
                : Array.Empty<Key>();
        }

        /// <summary>Returns a subset of the keys returned by <see cref="CameraMetadata.GetKeys"/> with all keys that require camera clients to obtain the Manifest.permission.CAMERA permission.</summary>
        public Key[] GetKeysNeedingPermission()
        {
            ThrowIfDisposed();
            return GetKeysFromListNative("getKeysNeedingPermission");
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{nameof(CameraInfo)} {{ " +
            $"{nameof(CameraId)} = {CameraId}, " +
            $"{nameof(Source)} = {Source}, " +
            $"{nameof(Eye)} = {Eye}, " +
            $"{nameof(LensPoseTranslation)} = {LensPoseTranslation}, " +
            $"{nameof(LensPoseRotation)} = {LensPoseRotation}, " +
            $"{nameof(SupportedResolutions)} = [{string.Join(", ", SupportedResolutions)}], " +
            $"{nameof(SupportedStreamUseCases)} = [{string.Join(", ", SupportedStreamUseCases)}], " +
            $"{nameof(Intrinsics)} = {Intrinsics} " +
            $"}}";
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as CameraInfo);

        /// <inheritdoc/>
        public bool Equals(CameraInfo? other) => other != null && string.Compare(other.CameraId, CameraId) == 0;

        /// <inheritdoc/>
        public override int GetHashCode() => CameraId.GetHashCode();

        /// <inheritdoc/>
        public static bool operator ==(CameraInfo? left, CameraInfo? right)
            => string.Compare(left?.CameraId, right?.CameraId, StringComparison.Ordinal) == 0;

        /// <inheritdoc/>
        public static bool operator !=(CameraInfo? left, CameraInfo? right)
            => string.Compare(left?.CameraId, right?.CameraId, StringComparison.Ordinal) != 0;
    }
}
