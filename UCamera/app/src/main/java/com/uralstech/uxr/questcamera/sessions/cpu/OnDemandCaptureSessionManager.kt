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

import android.graphics.SurfaceTexture
import android.hardware.camera2.CameraAccessException
import android.hardware.camera2.CameraDevice
import android.util.Log
import android.view.Surface
import com.uralstech.uxr.questcamera.CustomErrorCodes

class OnDemandCaptureSessionManager(width: Int, height: Int, callbacks: Callbacks)
    : ContinuousCaptureSessionManager(width, height, callbacks, "OnDemandSession") {

    private var dummySurfaceTexture: SurfaceTexture? = null
    private var dummySurface: Surface? = null

    data class SingleRequestSetResult(
        val status: Int,
        val sequenceId: Int
    )

    override fun initialize(
        camera: CameraDevice, captureTemplate: Int, streamUseCases: LongArray
    ) {
        Log.i(TAG, "($logPrefix) Initializing session.")

        try {
            val dummySurfaceTexture = SurfaceTexture(0)
            val dummySurface = Surface(dummySurfaceTexture)

            this.dummySurfaceTexture = dummySurfaceTexture
            this.dummySurface = dummySurface

            val dummyOutputConfiguration = outputConfigWith(dummySurface, streamUseCases, 0)
            val outputConfiguration = outputConfigWith(imageSurface, streamUseCases, if (streamUseCases.size > 1) 1 else 0)
            startSession(camera, listOf(dummyOutputConfiguration, outputConfiguration)) { session ->
                setRepeatingRequest(session, dummySurface, captureTemplate)
            }
        } catch (ex: IllegalArgumentException) {
            close()

            Log.e(TAG, "($logPrefix) Could initialize due to illegal argument (likely streamUseCases)", ex)
            callbacks.onConfigureFailed(CustomErrorCodes.ILLEGAL_ARGUMENT)
        } catch (ex: Surface.OutOfResourcesException) {
            close()

            Log.e(TAG, "($logPrefix) Could not create dummy surface due to out-of-resources error", ex)
            callbacks.onConfigureFailed(CustomErrorCodes.OUT_OF_RESOURCES)
        }
    }

    fun setSingleRequest(captureTemplate: Int) : SingleRequestSetResult {
        val session = captureSession
        if (isDisposed || session == null) {
            Log.e(TAG, "($logPrefix) Tried to use closed/failed session.")
            return SingleRequestSetResult(CustomErrorCodes.OBJECT_DISPOSED, 0)
        }

        Log.i(TAG, "($logPrefix) Setting single-capture request.")

        try {
            val request = session.device.createCaptureRequest(captureTemplate).apply {
                addTarget(imageSurface)
                callbacks.modifyRequestBuilder(this, false)
            }.build()

            val sequenceId = session.captureSingleRequest(
                request,
                executor,
                setupCaptureEvents(request, false)
            )

            Log.i(TAG, "($logPrefix) Request set.")
            return SingleRequestSetResult(0, sequenceId)

        } catch (ex: CameraAccessException) {
            Log.e(TAG, "($logPrefix) Could not set request due to access error", ex)
            return SingleRequestSetResult(CustomErrorCodes.CAMERA_ACCESS, 0)
        } catch (ex: IllegalStateException) {
            Log.e(TAG, "($logPrefix) Could not set request due to illegal state error", ex)
            return SingleRequestSetResult(CustomErrorCodes.ILLEGAL_STATE, 0)
        } catch (ex: IllegalArgumentException) {
            Log.e(TAG, "($logPrefix) Could not set request due to illegal argument", ex)
            return SingleRequestSetResult(CustomErrorCodes.ILLEGAL_ARGUMENT, 0)
        }
    }

    override fun additionalCloseWork() {
        super.additionalCloseWork()

        dummySurface?.release()
        dummySurface = null

        dummySurfaceTexture?.release()
        dummySurfaceTexture = null

        Log.i(TAG, "($logPrefix) Dummy textures released.")
    }
}