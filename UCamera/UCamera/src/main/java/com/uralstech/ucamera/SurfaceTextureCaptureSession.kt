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
import android.hardware.camera2.CaptureRequest
import android.hardware.camera2.TotalCaptureResult
import android.hardware.camera2.params.OutputConfiguration
import android.hardware.camera2.params.SessionConfiguration
import android.util.Log
import android.view.Surface
import com.unity3d.player.UnityPlayer
import java.util.concurrent.Executors

/**
 * Wrapper class for [CameraCaptureSession], exclusively using a [SurfaceTexture] for capture.
 */
class SurfaceTextureCaptureSession(
    timeStamp: Long,
    private val callbacks: Callbacks,
    private val cameraDevice: CameraDevice,
    private val width: Int,
    private val height: Int,
    private val captureTemplate: Int) {

    interface Callbacks {
        fun destroyNativeTexture(textureId: Int)
        fun onSessionConfigured()
        fun onSessionConfigurationFailed(isAccessOrSecurityError: Boolean)
        fun onSessionRequestSet()
        fun onSessionRequestFailed()
        fun onCaptureCompleted(textureId: Int)
    }

    companion object {
        private const val TAG = "STCaptureSessionWrapper"

        init {
            System.loadLibrary("NativeTextureHelper")
        }
    }

    /** Is this object active and usable? */
    var isActiveAndUsable: Boolean = true
        private set

    /** The capture session being wrapped by this object. */
    private var captureSession: CameraCaptureSession? = null

    /** Executor for the current capture session. */
    private val captureSessionExecutor = Executors.newSingleThreadExecutor()

    /** [Surface] for [captureSession]. */
    private var surface: Surface? = null

    /** [SurfaceTexture] for [surface]. */
    private var surfaceTexture: SurfaceTexture? = null

    /** OpenGL ES 3.0 texture ID for [surfaceTexture]. */
    private var surfaceTextureId: Int = 0

    init {
        queueSurfaceTextureCaptureSession(timeStamp)
    }

    /**
     * Starts a new capture session.
     */
    fun startCaptureSession(textureId: Int) {
        Log.i(TAG, "startCaptureSession was called with textureId: $textureId.")
        if (captureSession != null || !isActiveAndUsable) {
            return
        }

        try {
            Log.i(TAG, "Starting capture session.")
            surfaceTextureId = textureId

            val surfaceTexture = SurfaceTexture(textureId)
            surfaceTexture.setDefaultBufferSize(width, height)

            this.surfaceTexture = surfaceTexture

            registerSurfaceTextureForUpdates(surfaceTexture, textureId)

            val surface = Surface(surfaceTexture)
            this.surface = surface

            cameraDevice.createCaptureSession(SessionConfiguration(
                SessionConfiguration.SESSION_REGULAR,
                listOf(OutputConfiguration(surface)),
                captureSessionExecutor,
                object : CameraCaptureSession.StateCallback() {
                    override fun onConfigured(session: CameraCaptureSession) {
                        Log.i(TAG, "New capture session configured for camera with ID \"${cameraDevice.id}\".")

                        captureSession = session
                        setRepeatingCaptureRequest(session, captureTemplate, surface)
                        callbacks.onSessionConfigured()
                    }

                    override fun onConfigureFailed(session: CameraCaptureSession) {
                        Log.e(TAG, "Could not create new capture session as it could not be configured for camera with ID \"${cameraDevice.id}\".")
                        close()

                        callbacks.onSessionConfigurationFailed(false)
                    }

                    override fun onClosed(session: CameraCaptureSession) {
                        deregisterSurfaceTextureForUpdates(textureId)

                        surface.release()
                        surfaceTexture.release()
                        captureSessionExecutor.shutdown()
                        Log.i(TAG, "Capture session executor shut down.")

                        callbacks.destroyNativeTexture(textureId)
                    }
                }
            ))
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Capture session for camera with ID \"${cameraDevice.id}\" could not be created due to a camera access exception.", exp)
            close()

            callbacks.onSessionConfigurationFailed(true)
        } catch (exp: SecurityException) {
            Log.e(TAG, "Capture session for camera with ID \"${cameraDevice.id}\" could not be created due to a security exception.", exp)
            close()

            callbacks.onSessionConfigurationFailed(true)
        }
    }

    /**
     * Sets a repeating capture request.
     */
    private fun setRepeatingCaptureRequest(captureSession: CameraCaptureSession, captureTemplate: Int, surface: Surface) {
        try {
            val captureRequest = captureSession.device.createCaptureRequest(captureTemplate).apply {
                addTarget(surface)
            }.build()

            captureSession.setSingleRepeatingRequest(captureRequest, captureSessionExecutor, object : CameraCaptureSession.CaptureCallback() {
                override fun onCaptureCompleted(
                    session: CameraCaptureSession,
                    request: CaptureRequest,
                    result: TotalCaptureResult
                ) {
                    try {
                        callbacks.onCaptureCompleted(surfaceTextureId)
                    } catch (ex: Exception) {
                        Log.e(TAG, "Client onCaptureCompleted callback threw an exception", ex)
                    }
                }
            })

            Log.i(TAG, "Session request set for camera session of camera with ID \"${captureSession.device.id}\".")
            callbacks.onSessionRequestSet()
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Camera device with ID \"${captureSession.device.id}\" erred out with a camera access exception.", exp)
            close()

            callbacks.onSessionRequestFailed()
        } catch (exp: SecurityException) {
            Log.e(TAG, "Camera device with ID \"${captureSession.device.id}\" erred out with a security exception.", exp)
            close()

            callbacks.onSessionRequestFailed()
        }
    }

    /**
     * Releases associated resources and closes the session.
     * This results in [isActiveAndUsable] being set to false.
     */
    fun close() {
        if (!isActiveAndUsable) {
            return
        }

        Log.i(TAG, "Closing camera capture session wrapper.")
        isActiveAndUsable = false

        if (captureSession == null) {
            captureSessionExecutor.shutdown()
        } else {
            captureSession?.close()
            captureSession = null
        }

        Log.i(TAG, "Camera capture session wrapper closed, executor will be shut down soon.")
    }

    private external fun queueSurfaceTextureCaptureSession(timeStamp: Long)
    private external fun registerSurfaceTextureForUpdates(surfaceTexture: SurfaceTexture, textureId: Int)
    private external fun deregisterSurfaceTextureForUpdates(textureId: Int)
}