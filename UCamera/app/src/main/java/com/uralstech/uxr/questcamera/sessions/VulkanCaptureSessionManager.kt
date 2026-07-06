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

package com.uralstech.uxr.questcamera.sessions

import android.graphics.ImageFormat
import android.hardware.HardwareBuffer
import android.hardware.camera2.CameraDevice
import android.media.Image
import android.os.Build
import android.util.Log
import androidx.annotation.RequiresApi
import com.uralstech.uxr.questcamera.CustomErrorCodes

class VulkanCaptureSessionManager(width: Int, height: Int, private val callbacks: Callbacks)
    : ImageReaderCaptureSessionManagerBase(width, height, ImageFormat.PRIVATE, 2,
        HardwareBuffer.USAGE_GPU_SAMPLED_IMAGE, callbacks, "VulkanSession")  {

    companion object {
        init {
            System.loadLibrary("UXRQC_VulkanGluePlugin")
        }
    }

    interface Callbacks : CallbacksBase {

        /**
         * Gives ownership of the acquired AHardwareBuffer.
         *
         * If this method returns normally, the caller relinquishes ownership.
         * If it throws, it must not have retained the buffer pointer.
         */
        fun onFrameReady(acquiredBufferPtr: Long, bufferDataSpace: Int, bufferId: Long, timestamp: Long)
    }

    @RequiresApi(Build.VERSION_CODES.TIRAMISU)
    override fun imageHandover(image: Image) {

        val buffer = image.hardwareBuffer
        if (buffer == null) {
            Log.e(TAG, "Could not get hardware buffer from image.")
            image.close()
            return
        }

        var acquiredBufferPtr = 0L

        try {
            val timestamp = image.timestamp

            acquiredBufferPtr = acquireHardwareBuffer(buffer)
            val bufferId = getHardwareBufferId(acquiredBufferPtr)
            val bufferDataSpace = image.dataSpace // Seems to be consistently DATASPACE_UNKNOWN on Quest, as of HzOS v2.5

            if (acquiredBufferPtr != 0L && bufferId != 0L) {
                callbacks.onFrameReady(acquiredBufferPtr, bufferDataSpace, bufferId, timestamp)
                acquiredBufferPtr = 0L
            }
        } catch (ex: Exception) {
            Log.e(TAG, "Could not complete image handover due to exception.", ex)
        } finally {
            if (acquiredBufferPtr != 0L) {
                releaseHardwareBuffer(acquiredBufferPtr)
            }

            buffer.close()
            image.close()
        }
    }

    internal fun initialize(
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

    private external fun acquireHardwareBuffer(buffer: HardwareBuffer) : Long
    private external fun getHardwareBufferId(acquiredBufferPtr: Long) : Long
    private external fun releaseHardwareBuffer(acquiredBufferPtr: Long)
}