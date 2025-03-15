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

using static Uralstech.UXR.QuestCamera.CameraDevice;

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// A wrapper for a native Camera2 CaptureSession and ImageReader.
    /// </summary>
    /// <remarks>
    /// This is different from <see cref="CaptureSession"/> as it only returns a frame
    /// from the native plugin when required. This is recommended for single-image
    /// capturing or on-demand capturing where you don't need a continuous stream
    /// of images.
    /// </remarks>
    public class OnDemandCaptureSession : CaptureSession
    {
        /// <summary>
        /// Requests a new capture from the session.
        /// </summary>
        /// <param name="captureTemplate">The capture template to use for the capture</param>
        /// <returns>If the capture request was set successfully, <see langword="true"/>, otherwise, <see langword="false"/>.</returns>
        public bool RequestCapture(CaptureTemplate captureTemplate = CaptureTemplate.StillCapture)
        {
            return _captureSession?.Call<bool>("setSingleCaptureRequest", (int)captureTemplate) ?? false;
        }
    }
}