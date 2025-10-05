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
import android.hardware.camera2.CaptureResult
import android.hardware.camera2.TotalCaptureResult
import android.hardware.camera2.params.OutputConfiguration
import android.hardware.camera2.params.SessionConfiguration
import android.util.Log
import android.view.Surface
import java.util.concurrent.Executors
import java.util.concurrent.Semaphore
import java.util.concurrent.TimeUnit

class STCaptureSessionWrapper(
    private val timestamp: Long,
    private val callbacks: Callbacks,
    private val cameraDevice: CameraDevice,
    private val width: Int, private val height: Int,
    private val captureTemplate: Int
) {

    interface Callbacks {
        fun onSessionConfigured()
        fun onSessionConfigurationFailed(isAccessOrSecurityError: Boolean)
        fun onSessionRequestSet()
        fun onSessionRequestFailed()
        fun onSessionRegistrationFailed()
        fun onSessionActive()
        fun onSessionClosed()
        fun onCaptureCompleted(timestamp: Long)
    }

    companion object {
        private const val TAG = "STCaptureSessionWrapper"

        init {
            System.loadLibrary("NativeTextureHelper")
        }
    }

    /** Is this object active and usable? */
    @Volatile
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

    private val executorSemaphore = Semaphore(1)
    private val requestSemaphore = Semaphore(1)

    fun tryRegister(): Boolean {
        return registerCaptureSessionNative(timestamp);
    }

    private fun startCaptureSession(textureId: Int): Boolean {
        if (captureSession != null || !isActiveAndUsable) {
            Log.e(TAG, "Tried to start capture session on wrapper which is already disposed/recording!");
            return false;
        }

        Log.i(TAG, "Starting capture session with texture: $textureId")
        try {
            val surfaceTexture = SurfaceTexture(textureId)
            surfaceTexture.setDefaultBufferSize(width, height)
            this.surfaceTexture = surfaceTexture

            val surface = Surface(surfaceTexture)
            this.surface = surface

            executorSemaphore.acquire()
            cameraDevice.createCaptureSession(SessionConfiguration(
                SessionConfiguration.SESSION_REGULAR,
                listOf(OutputConfiguration(surface)),
                captureSessionExecutor,
                object : CameraCaptureSession.StateCallback() {
                    override fun onConfigured(session: CameraCaptureSession) {
                        Log.i(TAG, "Capture session configured, camera: \"${cameraDevice.id}\".")

                        captureSession = session
                        setRepeatingCaptureRequest(session, surface)
                        callbacks.onSessionConfigured()
                    }

                    override fun onConfigureFailed(session: CameraCaptureSession) {
                        Log.e(TAG, "Configuration for capture session failed, camera: \"${cameraDevice.id}\".")
                        close()

                        callbacks.onSessionConfigurationFailed(false)
                    }

                    override fun onClosed(session: CameraCaptureSession) {
                        Log.i(TAG, "Capture session closed.")
                        deregisterSurfaceTextureForUpdates(textureId)
                        executorSemaphore.release()
                    }

                    override fun onActive(session: CameraCaptureSession) {
                        requestSemaphore.acquire()
                        if (!registerSurfaceTextureForUpdates(surfaceTexture, textureId)) {
                            close()
                            callbacks.onSessionRegistrationFailed()
                        } else {
                            Log.i(TAG, "Capture session is now active.")
                            callbacks.onSessionActive()
                        }
                    }

                    override fun onReady(session: CameraCaptureSession) {
                        Log.i(TAG, "Capture session is ready for more requests.")
                        requestSemaphore.release()
                    }
                }
            ))

            return true
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Capture session could not be started due to access exception, camera: \"${cameraDevice.id}\"", exp)
            close()

            callbacks.onSessionConfigurationFailed(true)
            return false
        } catch (exp: SecurityException) {
            Log.e(TAG, "Capture session could not be started due to security exception, camera: \"${cameraDevice.id}\"", exp)
            close()

            callbacks.onSessionConfigurationFailed(true)
            return false
        }
    }

    private fun setRepeatingCaptureRequest(captureSession: CameraCaptureSession, surface: Surface) {
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
                        callbacks.onCaptureCompleted(result.get(CaptureResult.SENSOR_TIMESTAMP) ?: -1)
                    } catch (ex: Exception) {
                        Log.e(TAG, "Client onCaptureCompleted callback threw an exception", ex)
                    }
                }
            })

            Log.i(TAG, "Capture session request set, camera: \"${cameraDevice.id}\"")
            callbacks.onSessionRequestSet()
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Capture session request could not be set due to access exception, camera: \"${cameraDevice.id}\"", exp)
            close()

            callbacks.onSessionRequestFailed()
        } catch (exp: SecurityException) {
            Log.e(TAG, "Capture session request could not be set due to security exception, camera: \"${cameraDevice.id}\"", exp)
            close()

            callbacks.onSessionRequestFailed()
        }
    }

    fun close() {
        if (!isActiveAndUsable) {
            return
        }

        isActiveAndUsable = false
        tryDeregisterCaptureSessionNative(timestamp)

        if (captureSession != null) {
            captureSession?.stopRepeating()
            requestSemaphore.acquire()
            requestSemaphore.release()
        }

        captureSession?.close()
        captureSession = null

        executorSemaphore.acquire()
        captureSessionExecutor.shutdown()
        try {
            captureSessionExecutor.awaitTermination(10, TimeUnit.SECONDS)
        } catch (e: InterruptedException) {
            Log.e(TAG, "Interrupted while trying to stop the background thread", e)
        } finally {
            executorSemaphore.release()
        }

        surface?.release()
        surface = null

        surfaceTexture?.release()
        surfaceTexture = null

        Log.i(TAG, "Capture session closed.")
        callbacks.onSessionClosed()
    }

    private external fun registerCaptureSessionNative(timestamp: Long): Boolean
    private external fun tryDeregisterCaptureSessionNative(timestamp: Long)
    private external fun registerSurfaceTextureForUpdates(texture: SurfaceTexture, textureId: Int): Boolean
    private external fun deregisterSurfaceTextureForUpdates(textureId: Int)
}