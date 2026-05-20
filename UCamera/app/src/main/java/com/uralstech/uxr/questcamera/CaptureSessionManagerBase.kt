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

import android.hardware.camera2.CameraAccessException
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.CaptureFailure
import android.hardware.camera2.CaptureRequest
import android.hardware.camera2.TotalCaptureResult
import android.hardware.camera2.params.OutputConfiguration
import android.hardware.camera2.params.SessionConfiguration
import android.util.Log
import android.view.Surface
import java.util.concurrent.CountDownLatch
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

abstract class CaptureSessionManagerBase(
    private val callbacks: CallbacksBase,
    protected val logPrefix: String) {

    interface CallbacksBase {
        fun modifyRequestBuilder(builder: CaptureRequest.Builder, isRepeatingRequest: Boolean)
        fun shouldRegisterCaptureEvents(request: CaptureRequest, isRepeatingRequest: Boolean) : Boolean

        fun onConfigured()
        fun onConfigureFailed(code: Int)

        fun onRequestSet(sequenceId: Int)
        fun onRequestFailed(code: Int)

        fun onCaptureCompleted(request: CaptureRequest, result: TotalCaptureResult)
        fun onCaptureFailed(request: CaptureRequest, failure: CaptureFailure)

        fun onCaptureSequenceCompleted(sequenceId: Int, frameNumber: Long)
        fun onCaptureSequenceAborted(sequenceId: Int)

        fun onClosed()
    }

    companion object {
        protected const val TAG = "UXRQC.SessionManager"
    }

    @Volatile
    protected var isDisposed = false
        private set

    @Volatile
    private var willInvokeCloseCallback = true

    @Volatile
    protected var captureSession: CameraCaptureSession? = null
        private set

    protected val executor: ExecutorService = Executors.newSingleThreadExecutor()
    private val repeatingRequestLatch = CountDownLatch(2)

    protected fun startSession(camera: CameraDevice, outputs: List<OutputConfiguration>, onConfigured: (CameraCaptureSession) -> Unit) {
        if (captureSession != null || isDisposed) {
            Log.e(TAG, "($logPrefix) TRIED TO START SESSION TWICE. THIS IS A FATAL ERROR AND SHOULD NEVER HAPPEN. OPEN A BUG REPORT AT (https://github.com/Uralstech/UXR.QuestCamera) WITH LOGS.")
            return
        }

        executor.submit {
            try {
                camera.createCaptureSession(
                    SessionConfiguration(SessionConfiguration.SESSION_REGULAR, outputs, executor, object : CameraCaptureSession.StateCallback() {

                        override fun onConfigured(session: CameraCaptureSession) {
                            captureSession = session
                            Log.i(TAG, "($logPrefix) Session configured.")

                            onConfigured.invoke(session)
                            callbacks.onConfigured()
                        }

                        override fun onConfigureFailed(session: CameraCaptureSession) {
                            captureSession = null
                            close()

                            Log.i(TAG, "($logPrefix) Session configuration failed.")
                            callbacks.onConfigureFailed(CustomErrorCodes.CAPTURE_SESSION_CONFIG_FAILED)
                        }

                        override fun onClosed(session: CameraCaptureSession) {
                            captureSession = null
                            willInvokeCloseCallback = false

                            Log.i(TAG, "($logPrefix) Session closed.")
                            callbacks.onClosed()
                        }

                        override fun onReady(session: CameraCaptureSession) {
                            repeatingRequestLatch.countDown()
                            Log.i(TAG, "($logPrefix) Session ready for requests.")
                        }
                    })
                )
            } catch (ex: CameraAccessException) {
                close()

                Log.e(TAG, "($logPrefix) Could not create session due to access error", ex)
                callbacks.onConfigureFailed(CustomErrorCodes.CAMERA_ACCESS)
            } catch (ex: IllegalArgumentException) {
                close()

                Log.e(TAG, "($logPrefix) Could not create session due to illegal argument", ex)
                callbacks.onConfigureFailed(CustomErrorCodes.ILLEGAL_ARGUMENT)
            }
        }
    }

    // FAILURE OF THIS METHOD IS UNRECOVERABLE
    protected fun setRepeatingRequest(session: CameraCaptureSession, surface: Surface, captureTemplate: Int) : Boolean {
        try {
            val request = session.device.createCaptureRequest(captureTemplate).apply {
                addTarget(surface)
                callbacks.modifyRequestBuilder(this, true)
            }.build()

            val sequenceId = session.setSingleRepeatingRequest(
                request,
                executor,
                setupCaptureEvents(request, true)
            )

            Log.i(TAG, "($logPrefix) Repeating request set.")
            callbacks.onRequestSet(sequenceId)

            return true
        } catch (ex: CameraAccessException) {
            Log.e(TAG, "($logPrefix) Could not set repeating request due to access error", ex)
            callbacks.onRequestFailed(CustomErrorCodes.CAMERA_ACCESS)
        } catch (ex: IllegalStateException) {
            Log.e(TAG, "($logPrefix) Could not set repeating request due to illegal state error", ex)
            callbacks.onRequestFailed(CustomErrorCodes.ILLEGAL_STATE)
        } catch (ex: IllegalArgumentException) {
            Log.e(TAG, "($logPrefix) Could not set repeating request due to illegal argument", ex)
            callbacks.onRequestFailed(CustomErrorCodes.ILLEGAL_ARGUMENT)
        }

        while (repeatingRequestLatch.count > 0) {
            repeatingRequestLatch.countDown()
        }

        return false
    }

    fun abortCaptures() : Int {
        val session = captureSession
        if (isDisposed || session == null) {
            return CustomErrorCodes.OBJECT_DISPOSED
        }

        try {
            session.abortCaptures()
            Log.i(TAG, "($logPrefix) Captures aborted.")
            return 0
        } catch (ex: CameraAccessException) {
            Log.e(TAG, "($logPrefix) Could not abort captures due to camera access error", ex)
            return CustomErrorCodes.CAMERA_ACCESS
        } catch (ex: IllegalStateException) {
            Log.e(TAG, "($logPrefix) Could not abort captures due to illegal state error", ex)
            return CustomErrorCodes.ILLEGAL_STATE
        }
    }

    protected fun setupCaptureEvents(captureRequest: CaptureRequest, isRepeatingRequest: Boolean)
        : CameraCaptureSession.CaptureCallback {
        if (!callbacks.shouldRegisterCaptureEvents(captureRequest, isRepeatingRequest)) {
            return object : CameraCaptureSession.CaptureCallback() { }
        }

        Log.i(TAG, "Registering callbacks for capture request.")
        return object : CameraCaptureSession.CaptureCallback() {
            override fun onCaptureCompleted(
                session: CameraCaptureSession,
                request: CaptureRequest,
                result: TotalCaptureResult
            ) {
                callbacks.onCaptureCompleted(request, result)
            }

            override fun onCaptureFailed(
                session: CameraCaptureSession,
                request: CaptureRequest,
                failure: CaptureFailure
            ) {
                callbacks.onCaptureFailed(request, failure)
            }

            override fun onCaptureSequenceCompleted(
                session: CameraCaptureSession,
                sequenceId: Int,
                frameNumber: Long
            ) {
                callbacks.onCaptureSequenceCompleted(sequenceId, frameNumber)
            }

            override fun onCaptureSequenceAborted(session: CameraCaptureSession, sequenceId: Int) {
                callbacks.onCaptureSequenceAborted(sequenceId)
            }
        }
    }

    fun close() : Boolean {
        if (isDisposed) {
            return willInvokeCloseCallback
        }

        isDisposed = true
        Log.i(TAG, "($logPrefix) Closing session.")

        val session = captureSession
        disposeCleanup(session)

        if (session == null) {
            willInvokeCloseCallback = false
        }

        val closureExecutor = Executors.newSingleThreadExecutor()
        closureExecutor.submit {
            try {
                if (session != null) {
                    try {
                        session.stopRepeating()
                        Log.i(TAG, "($logPrefix) Repeating capture stopped for closure.")

                        if (!repeatingRequestLatch.await(5, TimeUnit.SECONDS)) {
                            Log.w(TAG, "($logPrefix) Could not wait for total request completion due to timeout.")
                        }
                    } catch (ex: CameraAccessException) {
                        Log.e(TAG, "($logPrefix) Could not stop repeating request due to camera access error", ex)
                    } catch (ex: IllegalStateException) {
                        Log.e(TAG, "($logPrefix) Could not stop repeating request due to illegal state error", ex)
                    } catch (ex: InterruptedException) {
                        Log.e(TAG, "($logPrefix) Interruption during request completion wait", ex)
                    }

                    session.close()
                    Log.i(TAG, "($logPrefix) Session close invoked.")
                }

                Log.i(TAG, "($logPrefix) Core close work completed.")
                additionalCloseWork()
            } finally {
                closureExecutor.shutdown()
            }
        }

        return willInvokeCloseCallback
    }

    protected open fun disposeCleanup(session: CameraCaptureSession?) { }

    protected open fun additionalCloseWork() { }
}