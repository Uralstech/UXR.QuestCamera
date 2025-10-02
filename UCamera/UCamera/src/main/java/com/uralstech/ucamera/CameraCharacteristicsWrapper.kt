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

import android.graphics.ImageFormat
import android.hardware.camera2.CameraCharacteristics

/**
 * Wrapper for [CameraCharacteristics].
 */
class CameraCharacteristicsWrapper(val cameraId: String, val characteristics: CameraCharacteristics) {
    companion object {
        private const val META_CAMERA_SOURCE_METADATA = "com.meta.extra_metadata.camera_source"
        private const val META_CAMERA_POSITION_METADATA = "com.meta.extra_metadata.position"

        private val metaCameraSourceMetadata = CameraCharacteristics.Key(META_CAMERA_SOURCE_METADATA, Int::class.java)
        private val metaCameraPositionMetadata = CameraCharacteristics.Key(META_CAMERA_POSITION_METADATA, Int::class.java)
    }

    /**
     * (Meta Quest) The source of the camera feed.
     */
    val metaQuestCameraSource = characteristics.get(metaCameraSourceMetadata)

    /**
     * (Meta Quest) The eye which the camera is closest to.
     */
    val metaQuestCameraEye = characteristics.get(metaCameraPositionMetadata)

    /**
     * The position of the camera optical center.
     */
    val lensPoseTranslation = characteristics.get(CameraCharacteristics.LENS_POSE_TRANSLATION)

    /**
     * The orientation of the camera relative to the sensor coordinate system.
     */
    val lensPoseRotation = characteristics.get(CameraCharacteristics.LENS_POSE_ROTATION)

    /**
     * The resolutions supported by this device.
     */
    val supportedResolutions = characteristics.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP)?.getOutputSizes(ImageFormat.YUV_420_888) ?: emptyArray()

    /**
     * The resolution, in pixels, for which intrinsics are provided.
     */
    val intrinsicsResolution = characteristics.get(CameraCharacteristics.SENSOR_INFO_PRE_CORRECTION_ACTIVE_ARRAY_SIZE)?.let { sensorSize ->
        intArrayOf(sensorSize.right, sensorSize.bottom)
    }

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
        intrinsicsFocalLength = lensCalibration?.let { floatArrayOf(it[0], it[1]) }
        intrinsicsPrincipalPoint = lensCalibration?.let { floatArrayOf(it[2], it[3]) }
        intrinsicsSkew = lensCalibration?.get(4)
    }
}