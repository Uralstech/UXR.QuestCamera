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
import android.util.Size

/**
 * Wrapper for [CameraCharacteristics].
 */
class CameraCharacteristicsWrapper(val cameraId: String, val characteristics: CameraCharacteristics) {
    companion object {
        private const val META_CAMERA_SOURCE_METADATA = "com.meta.extra_metadata.camera_source"
        private const val META_CAMERA_POSITION_METADATA = "com.meta.extra_metadata.position"

        private val metaCameraSourceMetadata = CameraCharacteristics.Key(META_CAMERA_SOURCE_METADATA, IntArray::class.java)
        private val metaCameraPositionMetadata = CameraCharacteristics.Key(META_CAMERA_POSITION_METADATA, IntArray::class.java)
    }

    /**
     * (Meta Quest) The source of the camera feed.
     */
    val metaQuestCameraSource: Int

    /**
     * (Meta Quest) The eye which the camera is closest to.
     */
    val metaQuestCameraEye: Int

    /**
     * The position of the camera optical center.
     */
    val lensPoseTranslation = characteristics.get(CameraCharacteristics.LENS_POSE_TRANSLATION)!!

    /**
     * The orientation of the camera relative to the sensor coordinate system.
     */
    val lensPoseRotation = characteristics.get(CameraCharacteristics.LENS_POSE_ROTATION)!!

    /**
     * The resolutions supported by this device.
     */
    val supportedResolutions: Array<Size>

    /**
     * The resolution, in pixels, for which intrinsics are provided.
     */
    val intrinsicsResolution: IntArray

    /**
     * The horizontal and vertical focal lengths, in pixels.
     */
    val intrinsicsFocalLength: FloatArray

    /**
     * Principal point in pixels from the image's top-left corner.
     */
    val intrinsicsPrincipalPoint: FloatArray

    /**
     * Skew coefficient for axis misalignment.
     */
    val intrinsicsSkew: Float

    init {
        var metaQuestCameraSource = -1
        var metaQuestCameraEye = -1

        for (key in characteristics.keys) {
            if (key.name == META_CAMERA_SOURCE_METADATA) {
                metaQuestCameraSource = characteristics.get(metaCameraSourceMetadata)!![0]
            } else if (key.name == META_CAMERA_POSITION_METADATA) {
                metaQuestCameraEye = characteristics.get(metaCameraPositionMetadata)!![0]
            }
        }

        this.metaQuestCameraSource = metaQuestCameraSource
        this.metaQuestCameraEye = metaQuestCameraEye

        val configMap = characteristics.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP)!!
        supportedResolutions = configMap.getOutputSizes(ImageFormat.YUV_420_888)!!

        val sensorSize = characteristics.get(CameraCharacteristics.SENSOR_INFO_PRE_CORRECTION_ACTIVE_ARRAY_SIZE)!!
        intrinsicsResolution = intArrayOf(sensorSize.right, sensorSize.bottom)

        val lensCalibration = characteristics.get(CameraCharacteristics.LENS_INTRINSIC_CALIBRATION)!!
        intrinsicsFocalLength = floatArrayOf(lensCalibration[0], lensCalibration[1])
        intrinsicsPrincipalPoint = floatArrayOf(lensCalibration[2], lensCalibration[3])
        intrinsicsSkew = lensCalibration[4]
    }
}