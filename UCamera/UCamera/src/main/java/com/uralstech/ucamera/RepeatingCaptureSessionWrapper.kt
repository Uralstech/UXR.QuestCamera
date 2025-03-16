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

package com.uralstech.ucamera

import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.params.OutputConfiguration

/**
 * Wrapper class for [CameraCaptureSession] with a repeating capture request.
 */
class RepeatingCaptureSessionWrapper(
    unityListener: String,
    frameCallback: CameraFrameCallback,
    width: Int, height: Int) : CaptureSessionWrapper(unityListener, frameCallback, width, height, 3) {

    /**
     * Creates a new capture session and sets the repeating capture request.
     */
    override fun startCaptureSession(camera: CameraDevice, captureTemplate: Int) {
        super.startRepeatingCaptureSession(camera, captureTemplate,
            listOf(OutputConfiguration(imageReader.surface)), imageReader.surface)
    }
}