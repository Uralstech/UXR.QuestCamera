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
import android.view.Surface
import java.nio.ByteBuffer
import java.util.concurrent.CountDownLatch
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.Semaphore
import java.util.concurrent.TimeUnit

/**
 * Wrapper class for [CameraCaptureSession].
 */
abstract class CaptureSessionWrapper private constructor(private val callbacks: Callbacks, width: Int, height: Int) {
    interface Callbacks
    {
        fun onSessionConfigured()
        fun onSessionConfigurationFailed(isAccessOrSecurityError: Boolean)
        fun onSessionRequestSet()
        fun onSessionRequestFailed()
        fun onSessionActive()
        fun onSessionClosed()
        fun disposeCompleted()

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

    companion object {
        private const val TAG = "CaptureSessionWrapper"
    }

    /** Is this object active and usable? */
    @Volatile
    protected var isDisposed = false

    /** Does the session executor need additional closure? */
    @Volatile
    protected var partialExecutorClosure = false

    /** Readers used as buffers for camera still shots. */
    protected val imageReader = ImageReader.newInstance(width, height, ImageFormat.YUV_420_888, 3)

    /** [HandlerThread] where all buffer reading operations run. */
    private val imageReaderThread = HandlerThread("ImageReaderThread").apply { start() }

    /** [Handler] corresponding to [imageReaderThread]. */
    protected val imageReaderHandler = Handler(imageReaderThread.looper)

    /** The capture session being wrapped by this object. */
    protected var captureSession: CameraCaptureSession? = null

    /** Executor for the current capture session. */
    protected val captureSessionExecutor: ExecutorService = Executors.newSingleThreadExecutor()

    private val executorSemaphore = Semaphore(1)
    private val requestCompletionLatch = CountDownLatch(2)

    constructor(cameraDevice: CameraDevice, captureTemplate: Int,
                callbacks: Callbacks, width: Int, height: Int) : this(callbacks, width, height) {
        imageReader.setOnImageAvailableListener({
            val image = imageReader.acquireLatestImage() ?: return@setOnImageAvailableListener

            val yPlane  = image.planes[0]
            val uPlane  = image.planes[1]

            val yBuffer = yPlane.buffer
            val uBuffer = uPlane.buffer
            val vBuffer = image.planes[2].buffer

            val timestamp = image.timestamp

            callbacks.onFrameReady(
                yBuffer,
                uBuffer,
                vBuffer,
                yPlane.rowStride,
                uPlane.rowStride,
                uPlane.pixelStride,
                timestamp
            )

            image.close()
        }, imageReaderHandler)

        startCaptureSession(cameraDevice, captureTemplate)
    }

    /**
     * Starts a new capture session.
     */
    protected abstract fun startCaptureSession(camera: CameraDevice, captureTemplate: Int)

    /**
     * Creates a new capture session and sets a repeating request.
     */
    protected fun startRepeatingCaptureSession(camera: CameraDevice, captureTemplate: Int, outputs: List<OutputConfiguration>, surface: Surface) {
        captureSessionExecutor.submit {
            try {
                executorSemaphore.acquire()
                camera.createCaptureSession(SessionConfiguration(
                    SessionConfiguration.SESSION_REGULAR,
                    outputs,
                    captureSessionExecutor,
                    object : CameraCaptureSession.StateCallback() {
                        override fun onConfigured(session: CameraCaptureSession) {
                            Log.i(TAG, "New capture session configured for camera with ID \"${camera.id}\".")
                            captureSession = session

                            setRepeatingCaptureRequest(session, captureTemplate, surface)
                            callbacks.onSessionConfigured()
                        }

                        override fun onConfigureFailed(session: CameraCaptureSession) {
                            Log.e(TAG, "Could not create new capture session as it could not be configured for camera with ID \"${camera.id}\".")
                            closeFromExecutor()

                            callbacks.onSessionConfigurationFailed(false)
                        }

                        override fun onClosed(session: CameraCaptureSession) {
                            Log.i(TAG, "Capture session closed.")
                            callbacks.onSessionClosed()
                            executorSemaphore.release()
                        }

                        override fun onActive(session: CameraCaptureSession) {
                            Log.i(TAG, "Capture session is now active.")
                            callbacks.onSessionActive()
                        }

                        override fun onReady(session: CameraCaptureSession) {
                            Log.i(TAG, "Capture session is ready for more requests.")
                            requestCompletionLatch.countDown()
                        }
                    }
                ))
            } catch (exp: CameraAccessException) {
                Log.e(TAG, "Capture session for camera with ID \"${camera.id}\" could not be created due to a camera access exception.", exp)
                executorSemaphore.release()
                close()

                callbacks.onSessionConfigurationFailed(true)
            } catch (exp: SecurityException) {
                Log.e(TAG, "Capture session for camera with ID \"${camera.id}\" could not be created due to a security exception.", exp)
                executorSemaphore.release()
                close()

                callbacks.onSessionConfigurationFailed(true)
            }
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

            captureSession.setSingleRepeatingRequest(captureRequest, captureSessionExecutor, object : CameraCaptureSession.CaptureCallback() { })

            Log.i(TAG, "Session request set for camera session of camera with ID \"${captureSession.device.id}\".")
            callbacks.onSessionRequestSet()
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Camera device with ID \"${captureSession.device.id}\" erred out with a camera access exception.", exp)
            closeFromExecutor()

            callbacks.onSessionRequestFailed()
        } catch (exp: SecurityException) {
            Log.e(TAG, "Camera device with ID \"${captureSession.device.id}\" erred out with a security exception.", exp)
            closeFromExecutor()

            callbacks.onSessionRequestFailed()
        }
    }

    fun closeFromExecutor() {
        if (isDisposed) {
            return
        }

        isDisposed = true
        partialExecutorClosure = true

        Log.i(TAG, "Closing camera capture session wrapper from executor.")
        captureSession?.stopRepeating()
        captureSession?.close()
    }

    /**
     * Releases associated resources and closes the session.
     */
    fun close(): Boolean {
        if (isDisposed && !partialExecutorClosure) {
            return false
        }

        Log.i(TAG, "Closing camera capture session wrapper.")
        isDisposed = true
        partialExecutorClosure = false

        val closureExecutor = Executors.newSingleThreadExecutor()
        closureExecutor.submit {
            try {
                closeWork()
            } finally {
                closureExecutor.shutdown()
                callbacks.disposeCompleted()
            }
        }

        return true
    }

    protected open fun closeWork() {
        if (captureSession != null) {
            captureSession?.stopRepeating()

            try {
                if (!requestCompletionLatch.await(1, TimeUnit.SECONDS)) {
                    Log.w(TAG, "Could not wait for total request completion due to timeout.")
                }
            } catch (e: InterruptedException) {
                Log.e(TAG, "Interrupted while trying to stop captureSession", e)
            }
        }

        captureSession?.close()
        captureSession = null

        if (executorSemaphore.tryAcquire(1, TimeUnit.SECONDS))
        {
            captureSessionExecutor.shutdown()
            executorSemaphore.release()
        } else {
            Log.w(TAG, "Closing background thread forcefully due to acquire timeout.")
            captureSessionExecutor.shutdownNow()
        }

        imageReader.setOnImageAvailableListener(null, null)
        imageReaderThread.quitSafely()
        try {
            imageReaderThread.join(1000)
        } catch (e: InterruptedException) {
            Log.e(TAG, "Interrupted while trying to stop the background thread", e)
        }

        imageReader.close()
        Log.i(TAG, "Camera capture session wrapper closed, executor will be shut down soon.")
    }
}