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

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// Capture template to use when recording.
    /// </summary>
    public enum CaptureTemplate
    {
        /// <summary>Default value, do not use.</summary>
        Default = 0,

        /// <summary>Creates a request suitable for a camera preview window.</summary>
        /// <remarks><a href="https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_PREVIEW"/></remarks>
        Preview = 1,

        /// <summary>Creates a request suitable for still image capture.</summary>
        /// <remarks><a href="https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_STILL_CAPTURE"/></remarks>
        StillCapture = 2,

        /// <summary>Creates a request suitable for video recording.</summary>
        /// <remarks><a href="https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_RECORD"/></remarks>
        Record = 3,

        /// <summary>Creates a request suitable for still image capture while recording video.</summary>
        /// <remarks><a href="https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_VIDEO_SNAPSHOT"/></remarks>
        VideoSnapshot = 4,

        /// <summary>Creates a request suitable for zero shutter lag still capture.</summary>
        /// <remarks><a href="https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_ZERO_SHUTTER_LAG"/></remarks>
        ZeroShutterLag = 5,
    }
}