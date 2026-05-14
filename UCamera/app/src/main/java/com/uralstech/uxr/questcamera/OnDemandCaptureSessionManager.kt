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

import android.graphics.SurfaceTexture
import android.hardware.camera2.CameraAccessException
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.params.OutputConfiguration
import android.os.Build
import android.util.Log
import android.view.Surface

class OnDemandCaptureSessionManager(width: Int, height: Int, callbacks: Callbacks)
    : ContinuousCaptureSessionManager(width, height, callbacks, "OnDemandSession") {

    private var dummySurfaceTexture: SurfaceTexture? = null
    private var dummySurface: Surface? = null

    override fun initialize(
        camera: CameraDevice, captureTemplate: Int, streamUseCases: LongArray
    ) {
        Log.i(TAG, "($logPrefix) Initializing session.")

        try {
            val dummySurfaceTexture = SurfaceTexture(0)
            val dummySurface = Surface(dummySurfaceTexture)

            this.dummySurfaceTexture = dummySurfaceTexture
            this.dummySurface = dummySurface

            val dummyOutputConfiguration = OutputConfiguration(dummySurface)
            val outputConfiguration = OutputConfiguration(imageReader.surface)

            if (streamUseCases.isNotEmpty() && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                dummyOutputConfiguration.streamUseCase = streamUseCases[0]
                outputConfiguration.streamUseCase = if (streamUseCases.size > 1) {
                    streamUseCases[1]
                } else {
                    streamUseCases[0]
                }
            }

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

    fun setSingleRequest(captureTemplate: Int) : Int {
        val session = captureSession
        if (isDisposed || session == null) {
            Log.e(TAG, "($logPrefix) Tried to use closed/failed session.")
            return CustomErrorCodes.OBJECT_DISPOSED
        }

        Log.i(TAG, "($logPrefix) Setting single-capture request.")

        try {
            val request = session.device.createCaptureRequest(captureTemplate).apply {
                addTarget(imageReader.surface)
                callbacks.modifyRequest(this, false)
            }.build()

            session.captureSingleRequest(request, executor, object : CameraCaptureSession.CaptureCallback() { })
            Log.i(TAG, "($logPrefix) Request set.")
            return 0

        } catch (ex: CameraAccessException) {
            Log.e(TAG, "($logPrefix) Could not set request due to access error", ex)
            return CustomErrorCodes.CAMERA_ACCESS
        } catch (ex: IllegalStateException) {
            Log.e(TAG, "($logPrefix) Could not set request due to illegal state error", ex)
            return CustomErrorCodes.ILLEGAL_STATE
        } catch (ex: IllegalArgumentException) {
            Log.e(TAG, "($logPrefix) Could not set request due to illegal argument", ex)
            return CustomErrorCodes.ILLEGAL_ARGUMENT
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