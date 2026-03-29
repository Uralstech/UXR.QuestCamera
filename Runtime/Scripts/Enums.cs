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

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>Capture template to use when recording.</summary>
    public enum CaptureTemplate : int
    {
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
    }

    /// <summary>
    /// Stream Use Cases are a way to improve the performance of Camera2 capture sessions.
    /// They give the hardware device more information to tune parameters, which provides
    /// a better camera experience for your specific task.
    /// </summary>
    public enum StreamUseCase : long
    {
        /// <summary>Does not set any stream use case. Use this if the device does not support stream use cases or you're unsure of support.</summary>
        None                = -1,

        /// <summary>Default stream use case.</summary>
        Default             = 0,

        /// <summary>Live stream shown to the user.</summary>
        Preview             = 1,

        /// <summary>Still photo capture.</summary>
        StillCapture        = 2,

        /// <summary>Recording video clips.</summary>
        VideoRecord         = 3,

        /// <summary>One single stream used for combined purposes of preview, video, and still capture.</summary>
        PreviewVideoStill   = 4,

        /// <summary>Long-running video call optimized for both power efficiency and video quality.</summary>
        VideoCall           = 5
    }

    public enum PCASupport
    {
        /// <summary>Support status cannot be determined.</summary>
        /// <remarks>Returned when the Meta XR Core SDK is not available.</remarks>
        Unknown,

        /// <summary>PCA API is supported.</summary>
        /// <remarks>Meta XR Core SDK on Quest 3/3S with Horizon OS v74+.</remarks>
        Supported,

        /// <summary>PCA API is not supported.</summary>
        /// <remarks>Non-Android device, or Meta XR Core SDK on unsupported devices or OS versions.</remarks>
        Unsupported,
    }

    /// <summary>State of a Camera2 resource.</summary>
    public enum ResourceState
    {
        /// <summary>Resource is initializing.</summary>
        Initializing,

        /// <summary>Resource is open and ready.</summary>
        Valid,

        /// <summary>Resource failed with an error, was disconnected or is being/was closed normally.</summary>
        Invalid,
    }
}
