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

import android.graphics.SurfaceTexture
import android.hardware.camera2.CameraAccessException
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.params.OutputConfiguration
import android.util.Log
import android.view.Surface

/**
 * Wrapper class for [CameraCaptureSession] meant for taking on-demand captures.
 *
 * It still runs a repeating preview stream in the background, so that when
 * a capture is requested, it is not pitch black due to it being the first
 * frame being captured.
 */
class OnDemandCaptureSessionWrapper(
    cameraDevice: CameraDevice, captureTemplate: Int,
    callbacks: Callbacks, width: Int, height: Int) : CaptureSessionWrapper(cameraDevice, captureTemplate, callbacks, width, height) {
    companion object {
        const val TAG = "ODCaptureSessionWrapper"
    }

    /** Dummy surface texture for preview capture request. */
    private var dummySurfaceTexture: SurfaceTexture? = null

    /** Dummy surface for preview capture request. */
    private var dummySurface: Surface? = null

    /**
     * Creates a new capture session and sets the repeating capture request.
     */
    override fun startCaptureSession(camera: CameraDevice, captureTemplate: Int) {
        Log.i(TAG, "Setting up capture session for single-capture request.")

        val dummySurfaceTexture = SurfaceTexture(0)
        val dummySurface = Surface(dummySurfaceTexture)
        this.dummySurfaceTexture = dummySurfaceTexture
        this.dummySurface = dummySurface

        super.startRepeatingCaptureSession(camera, captureTemplate,
            listOf(OutputConfiguration(dummySurface), OutputConfiguration(imageReader.surface)), dummySurface)
    }

    /**
     * Sets a new non-repeating capture request.
     */
    fun setSingleCaptureRequest(captureTemplate: Int): Boolean {
        val captureSession = this.captureSession
        if (!isActiveAndUsable || captureSession == null) {
            Log.e(TAG, "Tried to set non-repeating capture request for unusable capture session.")
            return false
        }

        Log.i(TAG, "Setting non-repeating capture request.")

        try {
            val captureRequest = captureSession.device.createCaptureRequest(captureTemplate).apply {
                addTarget(imageReader.surface)
            }.build()

            captureSession.capture(captureRequest, null, imageReaderHandler)

            Log.i(TAG, "Non-repeating capture request set for camera session of camera with ID \"${captureSession.device.id}\".")
            return true
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Camera device with ID \"${captureSession.device.id}\" erred out with a camera access exception.", exp)
            return false
        } catch (exp: SecurityException) {
            Log.e(TAG, "Camera device with ID \"${captureSession.device.id}\" erred out with a security exception.", exp)
            return false
        }
    }

    /**
     * Same as [CaptureSessionWrapper.close], but also releases [dummySurfaceTexture].
     */
    override fun close() {
        if (!isActiveAndUsable) {
            return
        }

        super.close()

        dummySurface?.release()
        dummySurface = null

        dummySurfaceTexture?.release()
        dummySurfaceTexture = null

        Log.i(TAG, "Dummy texture released.")
    }
}