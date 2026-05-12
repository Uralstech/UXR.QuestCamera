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

package com.uralstech.uxr.questcamera

import android.graphics.ImageFormat
import android.hardware.camera2.CameraCharacteristics
import android.os.Build

class CameraCharacteristicsProvider(val cameraId: String, val characteristics: CameraCharacteristics) {

    companion object {
        private const val CAMERA_SOURCE = "com.meta.extra_metadata.camera_source"
        private const val CAMERA_EYE    = "com.meta.extra_metadata.position"

        private val cameraSourceKey = CameraCharacteristics.Key(CAMERA_SOURCE, Int::class.java)
        private val cameraEyeKey    = CameraCharacteristics.Key(CAMERA_EYE, Int::class.java)
    }

    /**
     * (Meta Quest) The source of the camera feed.
     */
    val source = characteristics.get(cameraSourceKey)

    /**
     * (Meta Quest) The eye which the camera is closest to.
     */
    val eye = characteristics.get(cameraEyeKey)

    /**
     * The position of the camera device's lens optical center.
     */
    val lensPoseTranslation = characteristics.get(CameraCharacteristics.LENS_POSE_TRANSLATION)

    /**
     * The orientation of the camera relative to the sensor coordinate system.
     */
    val lensPoseRotation = characteristics.get(CameraCharacteristics.LENS_POSE_ROTATION)

    /**
     * The resolutions supported by this device.
     */
    val supportedResolutions = characteristics.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP)
        ?.getOutputSizes(ImageFormat.YUV_420_888) ?: emptyArray()

    /**
     * The stream use cases supported by this device.
     */
    val supportedStreamUseCases: LongArray = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
        characteristics.get(CameraCharacteristics.SCALER_AVAILABLE_STREAM_USE_CASES) ?: longArrayOf()
    } else {
        longArrayOf()
    }

    /**
     * The area of the image sensor which corresponds to active pixels prior to the application of any geometric distortion correction.
     */
    val intrinsicsResolution: IntArray?

    /**
     * The horizontal and vertical focal lengths, in pixels.
     */
    val intrinsicsFocalLength: FloatArray?

    /**
     * Principal point in pixels from the image's top-left corner.
     */
    val intrinsicsPrincipalPoint: FloatArray?

    /**
     * Skew coefficient for axis misalignment.
     */
    val intrinsicsSkew: Float?

    init {
        val lensCalibration = characteristics.get(CameraCharacteristics.LENS_INTRINSIC_CALIBRATION)
        intrinsicsResolution = characteristics.get(CameraCharacteristics.SENSOR_INFO_PRE_CORRECTION_ACTIVE_ARRAY_SIZE)?.let { intArrayOf(it.width(), it.height()) }
        intrinsicsFocalLength = lensCalibration?.let { floatArrayOf(it[0], it[1]) }
        intrinsicsPrincipalPoint = lensCalibration?.let { floatArrayOf(it[2], it[3]) }
        intrinsicsSkew = lensCalibration?.get(4)
    }
}