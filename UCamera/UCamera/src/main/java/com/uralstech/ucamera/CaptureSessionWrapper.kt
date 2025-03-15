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
import android.hardware.camera2.CameraAccessException
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.params.OutputConfiguration
import android.hardware.camera2.params.SessionConfiguration
import android.media.ImageReader
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import com.unity3d.player.UnityPlayer
import java.util.concurrent.Executors

/**
 * Wrapper class for [CameraCaptureSession].
 */
class CaptureSessionWrapper(
    private val unityListener: String,
    private val frameCallback: CameraFrameCallback,
    width: Int, height: Int) {

    companion object {
        private const val TAG = "CaptureSessionWrapper"

        private const val ON_SESSION_CONFIGURED     = "_onSessionConfigured"
        private const val ON_SESSION_CONFIG_FAILED  = "_onSessionConfigurationFailed"

        private const val ON_SESSION_REQUEST_SET    = "_onSessionRequestSet"
        private const val ON_SESSION_REQUEST_FAILED = "_onSessionRequestFailed"
    }

    /** Is this object active and usable? */
    var isActiveAndUsable: Boolean = true

    /** Readers used as buffers for camera still shots. */
    private val imageReader = ImageReader.newInstance(width, height, ImageFormat.YUV_420_888, 3)

    /** [HandlerThread] where all buffer reading operations run. */
    private val imageReaderThread = HandlerThread("ImageReaderThread").apply { start() }

    /** [Handler] corresponding to [imageReaderThread]. */
    private val imageReaderHandler = Handler(imageReaderThread.looper)

    /** The capture session being wrapped by this object. */
    private var captureSession: CameraCaptureSession? = null

    /** Executor for the current capture session. */
    private val captureSessionExecutor = Executors.newSingleThreadExecutor()

    init {
        imageReader.setOnImageAvailableListener({
            val image = imageReader.acquireLatestImage() ?: return@setOnImageAvailableListener

            val yPlane  = image.planes[0]
            val uPlane  = image.planes[1]

            val yBuffer = yPlane.buffer
            val uBuffer = uPlane.buffer
            val vBuffer = image.planes[2].buffer

            frameCallback.onFrameReady(
                yBuffer,
                uBuffer,
                vBuffer,
                yBuffer.remaining(),
                uBuffer.remaining(),
                vBuffer.remaining(),
                yPlane.rowStride,
                uPlane.rowStride,
                uPlane.pixelStride
            )

            image.close()
        }, imageReaderHandler)
    }

    /**
     * Creates a new capture session and sets the capture request.
     */
    internal fun startCaptureSession(camera: CameraDevice, captureTemplate: Int, isRepeating: Boolean) {
        try {
            camera.createCaptureSession(SessionConfiguration(
                SessionConfiguration.SESSION_REGULAR,
                listOf(OutputConfiguration(imageReader.surface)),
                captureSessionExecutor,
                object : CameraCaptureSession.StateCallback() {
                    override fun onConfigured(session: CameraCaptureSession) {
                        Log.i(TAG, "New capture session configured for camera with ID \"${camera.id}\".")
                        UnityPlayer.UnitySendMessage(unityListener, ON_SESSION_CONFIGURED, "")

                        captureSession = session
                        setCaptureRequests(session, captureTemplate, isRepeating)
                    }

                    override fun onConfigureFailed(session: CameraCaptureSession) {
                        Log.e(TAG, "Could not create new capture session as it could not be configured for camera with ID \"${camera.id}\".")
                        UnityPlayer.UnitySendMessage(unityListener, ON_SESSION_CONFIG_FAILED, "")

                        close()
                    }
                }
            ))
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Capture session for camera with ID \"${camera.id}\" could not be created due to a camera access exception.", exp)
            UnityPlayer.UnitySendMessage(unityListener, ON_SESSION_CONFIG_FAILED, exp.message)

            close()
        } catch (exp: SecurityException) {
            Log.e(TAG, "Capture session for camera with ID \"${camera.id}\" could not be created due to a security exception.", exp)
            UnityPlayer.UnitySendMessage(unityListener, ON_SESSION_CONFIG_FAILED, exp.message)

            close()
        }
    }

    /**
     * Sets the capture request.
     */
    private fun setCaptureRequests(captureSession: CameraCaptureSession, captureTemplate: Int, isRepeating: Boolean) {
        try {
            val captureRequest = captureSession.device.createCaptureRequest(captureTemplate).apply {
                addTarget(imageReader.surface)
            }.build()

            if (isRepeating) {
                captureSession.setRepeatingRequest(captureRequest, null, imageReaderHandler)
            } else {
                captureSession.capture(captureRequest, null, imageReaderHandler)
            }

            Log.i(TAG, "Session request set for camera session of camera with ID \"${captureSession.device.id}\".")
            UnityPlayer.UnitySendMessage(unityListener, ON_SESSION_REQUEST_SET, "")
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Camera device with ID \"${captureSession.device.id}\" erred out with a camera access exception.", exp)
            UnityPlayer.UnitySendMessage(unityListener, ON_SESSION_REQUEST_FAILED, exp.message)

            close()
        } catch (exp: SecurityException) {
            Log.e(TAG, "Camera device with ID \"${captureSession.device.id}\" erred out with a security exception.", exp)
            UnityPlayer.UnitySendMessage(unityListener, ON_SESSION_REQUEST_FAILED, exp.message)

            close()
        }
    }

    /**
     * Releases associated resources and closes the camera device.
     * This results in [isActiveAndUsable] being set to false.
     */
    fun close() {
        if (!isActiveAndUsable) {
            return
        }

        Log.i(TAG, "Closing camera capture session wrapper.")
        isActiveAndUsable = false

        captureSession?.close()
        captureSession = null

        imageReader.setOnImageAvailableListener(null, null)
        imageReaderThread.quitSafely()

        captureSessionExecutor.shutdown()
    }
}