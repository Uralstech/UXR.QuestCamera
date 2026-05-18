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
    /// <summary>The subset of the results of a single image capture from the image sensor.</summary>
    public class CaptureResult : CameraMetadata
    {
        public CaptureResult(AndroidJavaObject native) : base(native, "android.hardware.camera2.CaptureResult") { }

        /// <summary>Returns the camera ID of the camera that produced this capture result.</summary>
        public string GetCameraId()
        {
            ThrowIfDisposed();
            return Native.Call<string>("getCameraId");
        }

        /// <summary>Returns the frame number associated with this result.</summary>
        public long GetFrameNumber()
        {
            ThrowIfDisposed();
            return Native.Call<long>("getFrameNumber");
        }

        /// <summary>Returns the capture request associated with this result.</summary>
        public CaptureRequest GetRequest()
        {
            ThrowIfDisposed();

            AndroidJavaObject nativeRequest = Native.Call<AndroidJavaObject>("getRequest");
            return new CaptureRequest(nativeRequest);
        }

        /// <summary>Returns the sequence ID of the capture request originally returned in <see cref="CaptureSessionBase{TProxy}.ProxyBase.OnRequestSetWithId"/>.</summary>
        public int GetSequenceId()
        {
            ThrowIfDisposed();
            return Native.Call<int>("getSequenceId");
        }
    }

    /// <summary>The total assembled results of a single image capture from the image sensor.</summary>
    public sealed class TotalCaptureResult : CaptureResult
    {
        public TotalCaptureResult(AndroidJavaObject native) : base(native) { }

        /// <summary>Returns the list of partial results that compose this total result.</summary>
        public CaptureResult[] GetPartialResults()
        {
            ThrowIfDisposed();

            using AndroidJavaObject nativeResultsList = Native.Call<AndroidJavaObject>("getPartialResults");
            AndroidJavaObject[] nativeResults = nativeResultsList.ConvertList<AndroidJavaObject>();
            return Array.ConvertAll(nativeResults, static nativeResult => new CaptureResult(nativeResult));
        }
    }
}
