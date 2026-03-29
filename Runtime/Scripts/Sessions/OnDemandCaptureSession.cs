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
    /// <summary>Manages an on-demand, explicit capture session.</summary>
    /// <inheritdoc/>
    public sealed class OnDemandCaptureSession : ContinuousCaptureSession
    {
        private const string ClassName = "com.uralstech.uxr.questcamera.OnDemandCaptureSessionManager";

        public OnDemandCaptureSession(Resolution resolution) : base(resolution, ClassName) { }

        /// <summary>Requests a new capture from the session.</summary>
        /// <param name="errorCode">Error code if the operation was unsuccessful.</param>
        /// <param name="template">The capture template to use for the capture.</param>
        /// <returns><see langword="true"/> if successful; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ObjectDisposedException"/>
        public bool TryRequestCapture(out ErrorCode errorCode, CaptureTemplate template = CaptureTemplate.StillCapture)
        {
            ThrowIfDisposed();

            int result = _native.Call<int>("setSingleRequest", (int)template);
            errorCode = (ErrorCode)result;
            return result == 0;
        }
    }
}