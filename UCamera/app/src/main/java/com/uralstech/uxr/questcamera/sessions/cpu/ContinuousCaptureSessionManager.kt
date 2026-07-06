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

package com.uralstech.uxr.questcamera.sessions.cpu

import android.graphics.ImageFormat
import android.hardware.camera2.CameraDevice
import android.media.Image
import android.util.Log
import com.uralstech.uxr.questcamera.CustomErrorCodes
import com.uralstech.uxr.questcamera.sessions.ImageReaderCaptureSessionManagerBase
import java.nio.ByteBuffer

open class ContinuousCaptureSessionManager protected constructor(width: Int, height: Int, protected val callbacks: Callbacks, logPrefix: String)
    : ImageReaderCaptureSessionManagerBase(width, height, ImageFormat.YUV_420_888, 3, 0, callbacks, logPrefix)  {

    interface Callbacks : CallbacksBase {

        // Buffers MUST be processed synchronously
        fun onFrameReady(
            yBuffer: ByteBuffer,
            uBuffer: ByteBuffer,
            vBuffer: ByteBuffer,
            yRowStride: Int,
            uvRowStride: Int,
            uvPixelStride: Int,
            timestamp: Long
        )
    }

    constructor(width: Int, height: Int, callbacks: Callbacks) : this(width, height, callbacks, "ContinuousSession")

    override fun imageHandover(image: Image) {
        val yPlane  = image.planes[0]
        val uPlane  = image.planes[1]

        val yBuffer = yPlane.buffer
        val uBuffer = uPlane.buffer
        val vBuffer = image.planes[2].buffer

        val timestamp = image.timestamp

        try {
            callbacks.onFrameReady(
                yBuffer,
                uBuffer,
                vBuffer,
                yPlane.rowStride,
                uPlane.rowStride,
                uPlane.pixelStride,
                timestamp
            )
        } catch (ex: Exception) {
            Log.e(TAG, "($logPrefix) Error during frame callback", ex)
        } finally {
            image.close()
        }
    }

    internal open fun initialize(
        camera: CameraDevice, captureTemplate: Int, streamUseCases: LongArray
    ) {
        Log.i(TAG, "($logPrefix) Initializing session.")

        try {
            val outputConfiguration = outputConfigWith(imageSurface, streamUseCases, 0)
            startSession(camera, listOf(outputConfiguration)) { session ->
                setRepeatingRequest(session, imageSurface, captureTemplate)
            }
        } catch (ex: IllegalArgumentException) {
            close()

            Log.e(TAG, "($logPrefix) Could initialize due to illegal argument (likely streamUseCases)", ex)
            callbacks.onConfigureFailed(CustomErrorCodes.ILLEGAL_ARGUMENT)
        }
    }
}