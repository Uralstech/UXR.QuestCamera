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
    /// <summary>A report of failed capture for a single image capture from the image sensor.</summary>
    public sealed class CaptureFailure : IDisposable
    {
        /// <summary>Error codes from Camera2.</summary>
        public enum ErrorCode
        {
            /// <summary>The CaptureResult has been dropped this frame only due to an error in the framework.</summary>
            FrameworkError  = 0,

            /// <summary>The capture has failed due to an abort call from the application.</summary>
            FlushedError = 1
        }

        /// <summary>The frame number associated with this failed capture.</summary>
        public readonly long FrameNumber;

        /// <summary>The reason for the capture result being dropped.</summary>
        public readonly ErrorCode Reason;

        /// <summary>The sequence ID of the capture request originally returned in <see cref="CaptureSessionBase{TProxy}.ProxyBase.OnRequestSetWithId"/>.</summary>
        public readonly int SequenceId;

        /// <summary>If the image was captured from the camera.</summary>
        public readonly bool WasImageCaptured;

        /// <summary>The native object.</summary>
        public readonly AndroidJavaObject Native;

        private bool _disposed;

        public CaptureFailure(AndroidJavaObject native)
        {
            Native = native;

            FrameNumber = Native.Call<long>("getFrameNumber");
            Reason = (ErrorCode)Native.Call<int>("getReason");
            SequenceId = Native.Call<int>("getSequenceId");
            WasImageCaptured = Native.Call<bool>("wasImageCaptured");
        }

        /// <summary>Returns the capture request associated with this failure.</summary>
        public CaptureRequest GetRequest()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CaptureFailure));

            AndroidJavaObject nativeRequest = Native.Call<AndroidJavaObject>("getRequest");
            return new CaptureRequest(nativeRequest);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Native.Dispose();
        }
    }
}
