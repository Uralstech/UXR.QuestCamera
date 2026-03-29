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
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>Information describing a camera device.</summary>
    public sealed record CameraInfo : IDisposable
    {
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

        /// <summary>The position of the camera's optical center.</summary>
        public readonly Vector3? LensPoseTranslation;

        /// <summary>The orientation of the camera relative to the sensor coordinate system.</summary>
        public readonly Quaternion? LensPoseRotation;

        /// <summary>The resolutions supported by this camera.</summary>
        public readonly Resolution[] SupportedResolutions;

        /// <summary>The stream use cases supported by this camera, if any.</summary>
        public readonly StreamUseCase[] SupportedStreamUseCases;

        /// <summary>The intrinsic data for this camera.</summary>
        public readonly CameraIntrinsics? Intrinsics;

        /// <summary>The native CameraCharacteristics object.</summary>
        public readonly AndroidJavaObject Native;

        private bool _disposed;

        public CameraInfo(AndroidJavaObject obj)
        {
            CameraId = obj.Get<string>("cameraId");
            Native = obj.Get<AndroidJavaObject>("characteristics");

            Source = TryGetInt(obj, "source", out int src) ? (CameraSource)src : CameraSource.Unknown;
            Eye = TryGetInt(obj, "eye", out int eye) ? (CameraEye)eye : CameraEye.Unknown;

            LensPoseTranslation = TryGetTransformedArray<float, Vector3>(obj, "lensPoseTranslation", static t => new(t[0], t[1], -t[2]));
            LensPoseRotation = TryGetTransformedArray<float, Quaternion>(obj, "lensPoseRotation", static r => new(-r[0], -r[1], r[2], r[3]));

            SupportedResolutions = Array.ConvertAll(
                obj.Get<AndroidJavaObject[]>("supportedResolutions"),
                static res =>
                {
                    int width = res.Call<int>("getWidth");
                    int height = res.Call<int>("getHeight");
                    res.Dispose();

                    return new Resolution() { width = width, height = height };
                }
            );
            
            SupportedStreamUseCases = Array.ConvertAll(
                obj.Get<long[]>("supportedStreamUseCases"),
                static useCase => (StreamUseCase)useCase
            );

            if (TryGet(obj, "intrinsicsResolution",     out int[]? iRes)
             && TryGet(obj, "intrinsicsFocalLength",    out float[]? iFocalLen)
             && TryGet(obj, "intrinsicsPrincipalPoint", out float[]? iPriPoint)
             && TryGetFloat(obj, "intrinsicsSkew",      out float skew))
            {
                Intrinsics = new CameraIntrinsics(
                    new(iRes[0], iRes[1]),
                    new(iFocalLen[0], iFocalLen[1]),
                    new(iPriPoint[0], iPriPoint[1]),
                    skew
                );
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            Native.Dispose();
            GC.SuppressFinalize(this);
        }

        #region JNI Utils

        private TOut? TryGetTransformedArray<TElement, TOut>(AndroidJavaObject obj, string name, Func<TElement[], TOut> func) =>
            obj.Get<TElement[]>(name) is TElement[] array ? func(array) : default;

        private bool TryGet<T>(AndroidJavaObject obj, string name, [NotNullWhen(true)] out T? val) where T : notnull =>
            (val = obj.Get<T>(name)) != null;

        private bool TryGetInt(AndroidJavaObject obj, string name, out int val) =>
            TryGetStruct(obj, name, "intValue", out val);

        private bool TryGetFloat(AndroidJavaObject obj, string name, out float val) =>
            TryGetStruct(obj, name, "floatValue", out val);

        private bool TryGetStruct<T>(AndroidJavaObject obj, string name, string valName, [NotNullWhen(true)] out T val)
            where T : struct
        {
            using AndroidJavaObject? nullable = obj.Get<AndroidJavaObject>(name);
            if (nullable is null)
            {
                val = default;
                return false;
            }

            val = nullable.Call<T>(valName)!;
            return true;
        }

        #endregion
    }
}
