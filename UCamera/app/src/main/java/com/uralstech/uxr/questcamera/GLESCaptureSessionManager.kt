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
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.params.OutputConfiguration
import android.os.Build
import android.util.Log
import android.view.Surface

class GLESCaptureSessionManager(private val jobTexId: Int, private val callbacks: CallbacksBase)
    : CaptureSessionManagerBase(callbacks, "GLESSession") {

    companion object {
        init {
            System.loadLibrary("UXRQC_NativeConverters")
        }
    }

    private var surface: Surface? = null
    private var surfaceTexture: SurfaceTexture? = null
    private var isBoundToJob = false

    internal fun initialize(
        camera: CameraDevice, captureTemplate: Int, streamUseCases: LongArray,
        width: Int, height: Int, sourceTextureId: Int
    ) {
        Log.i(TAG, "($logPrefix) Initializing session.")

        try {
            val surfaceTexture = SurfaceTexture(sourceTextureId)
            this.surfaceTexture = surfaceTexture

            val surface = Surface(surfaceTexture)
            this.surface = surface

            surfaceTexture.setDefaultBufferSize(width, height)
            if (!bindJob(jobTexId, surfaceTexture)) {
                close()

                Log.e(TAG, "($logPrefix) Failed to bind to native job.")
                callbacks.onConfigureFailed(CustomErrorCodes.NATIVE_FAILED_JOB_BINDING)
                return
            }

            isBoundToJob = true
            val outputConfiguration = OutputConfiguration(surface).apply {
                if (streamUseCases.isNotEmpty() && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                    this.streamUseCase = streamUseCases[0]
                }
            }

            startSession(camera, listOf(outputConfiguration)) { session ->
                setRepeatingRequest(session, surface, captureTemplate)
            }
        } catch (ex: IllegalArgumentException) {
            close()

            Log.e(TAG, "($logPrefix) Could initialize due to illegal argument (likely streamUseCases)", ex)
            callbacks.onConfigureFailed(CustomErrorCodes.ILLEGAL_ARGUMENT)
        } catch (ex: Surface.OutOfResourcesException) {
            close()

            Log.e(TAG, "($logPrefix) Could not create surface due to out-of-resources error", ex)
            callbacks.onConfigureFailed(CustomErrorCodes.OUT_OF_RESOURCES)
        }
    }

    override fun disposeCleanup(session: CameraCaptureSession?) {
        if (isBoundToJob) {
            unbindJob(jobTexId)
            isBoundToJob = false
        }
    }

    override fun additionalCloseWork() {

        surface?.release()
        surface = null

        surfaceTexture?.release()
        surfaceTexture = null

        Log.i(TAG, "($logPrefix) Textures released.")
    }

    private external fun bindJob(jobTexId: Int, surfaceTexture: SurfaceTexture): Boolean
    private external fun unbindJob(jobTexId: Int)
}