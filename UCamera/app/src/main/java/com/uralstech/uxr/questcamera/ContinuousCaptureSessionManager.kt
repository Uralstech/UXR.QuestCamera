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
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.params.OutputConfiguration
import android.media.ImageReader
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import java.nio.ByteBuffer

open class ContinuousCaptureSessionManager protected constructor(width: Int, height: Int, protected val callbacks: Callbacks, logPrefix: String)
    : CaptureSessionManagerBase(callbacks, logPrefix)  {

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

    private val imageThread = HandlerThread("ImageReaderThread").apply { start() }
    private val imageHandler = Handler(imageThread.looper)

    protected val imageReader = ImageReader.newInstance(width, height, ImageFormat.YUV_420_888, 3).apply {
        setOnImageAvailableListener({
            val image = it.acquireLatestImage() ?: return@setOnImageAvailableListener

            if (isDisposed) {
                image.close()
                return@setOnImageAvailableListener
            }

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
        }, imageHandler)
    }

    constructor(width: Int, height: Int, callbacks: Callbacks) : this(width, height, callbacks, "ContinuousSession")

    internal open fun initialize(
        camera: CameraDevice, captureTemplate: Int, streamUseCases: LongArray
    ) {
        Log.i(TAG, "($logPrefix) Initializing session.")

        try {
            val outputConfiguration = OutputConfiguration(imageReader.surface).apply {
                if (streamUseCases.isNotEmpty() && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                    this.streamUseCase = streamUseCases[0]
                }
            }

            startSession(camera, listOf(outputConfiguration)) { session ->
                setRepeatingRequest(session, imageReader.surface, captureTemplate)
            }
        } catch (ex: IllegalArgumentException) {
            close()

            Log.e(TAG, "($logPrefix) Could initialize due to illegal argument (likely streamUseCases)", ex)
            callbacks.onConfigureFailed(CustomErrorCodes.ILLEGAL_ARGUMENT)
        }
    }

    override fun additionalCloseWork() {

        imageReader.setOnImageAvailableListener(null, imageHandler)
        imageThread.quitSafely()

        try {
            imageThread.join(5000)
            if (imageThread.isAlive) {
                Log.w(TAG, "($logPrefix) ImageReader thread still alive due to timeout, ignoring thread.")
            }
        } catch (ex: InterruptedException) {
            Log.e(TAG, "($logPrefix) Interrupted while trying to stop imageReader thread", ex)
        }

        imageReader.close()
        Log.i(TAG, "($logPrefix) ImageReader closed.")
    }
}