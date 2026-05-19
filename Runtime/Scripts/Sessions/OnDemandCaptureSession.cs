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

        /// <summary>Status for on-demand capture requests.</summary>
        public readonly struct RequestStatus
        {
            /// <summary>Error code for failures, only valid if <see cref="Success"/> is <see langword="false"/>.</summary>
            public readonly ErrorCode ErrorCode;
            
            /// <summary>Sequence ID of capture request, only valid if <see cref="Success"/> is <see langword="true"/>.</summary>
            public readonly int SequenceId;

            /// <summary>If the request was successful.</summary>
            public readonly bool Success;

            public RequestStatus(ErrorCode errorCode, int sequenceId, bool success)
            {
                ErrorCode = errorCode;
                SequenceId = sequenceId;
                Success = success;
            }
        }

        public OnDemandCaptureSession(Resolution resolution) : base(resolution, ClassName) { }

        /// <summary>Requests a new capture from the session.</summary>
        /// <param name="errorCode">Error code if the operation was unsuccessful.</param>
        /// <param name="template">The capture template to use for the capture.</param>
        /// <returns><see langword="true"/> if successful; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ObjectDisposedException"/>
        public bool TryRequestCapture(out ErrorCode errorCode, CaptureTemplate template = CaptureTemplate.StillCapture)
        {
            ThrowIfDisposed();

            using AndroidJavaObject result = _native.Call<AndroidJavaObject>("setSingleRequest", (int)template);
            int status = result.Call<int>("getStatus");
            errorCode = (ErrorCode)status;
            return status == 0;
        }

        /// <summary>Requests a new capture from the session.</summary>
        /// <exception cref="ObjectDisposedException"/>
        public RequestStatus RequestCapture(CaptureTemplate template = CaptureTemplate.StillCapture)
        {
            ThrowIfDisposed();
            
            using AndroidJavaObject result = _native.Call<AndroidJavaObject>("setSingleRequest", (int)template);
            int sequenceId = result.Call<int>("getSequenceId");
            int status = result.Call<int>("getStatus");

            return new RequestStatus(
                (ErrorCode)status,
                sequenceId,
                status == 0
            );
        }
    }
}