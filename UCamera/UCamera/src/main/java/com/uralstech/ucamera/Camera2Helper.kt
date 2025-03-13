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

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.ImageFormat
import android.hardware.camera2.CameraAccessException
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.CameraManager
import android.hardware.camera2.params.OutputConfiguration
import android.hardware.camera2.params.SessionConfiguration
import android.media.ImageReader
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import androidx.lifecycle.ProcessLifecycleOwner
import androidx.lifecycle.lifecycleScope
import com.unity3d.player.UnityPlayer
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import java.lang.ref.WeakReference
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.coroutines.resume
import kotlin.coroutines.suspendCoroutine

class Camera2Helper(private var frameCallback: CameraFrameCallback) {

    companion object {
        private const val TAG = "Camera2Helper"
        private const val DEFAULT_CALLBACK_LISTENER = "UCameraManager"

        private const val ON_CAMERA_DEVICE_CONNECTED        = "_onCameraConnected"
        private const val ON_CAMERA_DEVICE_DISCONNECTED     = "_onCameraDisconnected"
        private const val ON_CAMERA_DEVICE_ERRED            = "_onCameraErred"
        private const val ON_CAMERA_DEVICE_CONFIGURED       = "_onCameraConfigured"
        private const val ON_CAMERA_DEVICE_CONFIGURE_ERRED  = "_onCameraConfigureErred"
        private const val ON_CAMERA_DEVICE_ACCESS_ERROR     = "_onCameraAccessError"
        private const val ON_CAMERA_DEVICE_CAPTURE_STARTED  = "_onCameraCaptureStarted"

        private var Instance: WeakReference<Camera2Helper> = WeakReference(null)

        @JvmStatic
        fun getInstance(listener: String, frameCallback: CameraFrameCallback): Camera2Helper {
            val instance = Instance.get()
            return if (instance != null) {
                instance.unityCallbackListener = listener
                instance.frameCallback = frameCallback
                instance
            } else {
                val newInstance = Camera2Helper(frameCallback)
                newInstance.unityCallbackListener = listener

                Instance = WeakReference(newInstance)
                newInstance
            }
        }
    }

    /** The application context. */
    private val appContext = UnityPlayer.currentContext.applicationContext

    /** Detects, characterizes, and connects to a CameraDevice (used for all camera operations). */
    private val cameraManager: CameraManager by lazy {
        appContext.getSystemService(Context.CAMERA_SERVICE) as CameraManager
    }

    /** The callback listener in Unity. */
    private var unityCallbackListener = DEFAULT_CALLBACK_LISTENER

    /** Readers used as buffers for camera still shots. */
    private var imageReader: ImageReader? = null

    /** [HandlerThread] where all buffer reading operations run. */
    private var imageReaderThread: HandlerThread? = null

    /** [Handler] corresponding to [imageReaderThread]. */
    private var imageReaderHandler: Handler? = null

    /** [HandlerThread] where all camera operations run. */
    private var cameraThread: HandlerThread? = null

    /** [Handler] corresponding to [cameraThread]. */
    private var cameraHandler: Handler? = null

    /** The [CameraDevice] that will be opened in this object. */
    private var camera: CameraDevice? = null

    /** Internal reference to the ongoing [CameraCaptureSession] configured with our parameters. */
    private var captureSession: CameraCaptureSession? = null

    /** Executor for [captureSession]. */
    private var captureSessionExecutor: ExecutorService? = null

    /** Is the [captureSession] currently open and recording? */
    private val isCaptureSessionOpen = AtomicBoolean(false)

    fun changeCallbackListener(newListener: String, frameCallback: CameraFrameCallback) {
        unityCallbackListener = newListener
        this.frameCallback = frameCallback
    }

    fun getDevices(): Array<String>? {
        return try {
            cameraManager.cameraIdList
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Could not get camera IDs due to error.", exp)
            null
        } catch (exp: SecurityException) {
            Log.e(TAG, "Could not get camera IDs due to security exception.", exp)
            null
        }
    }

    fun getSupportedResolutions(cameraId: String): Array<String>? {
        try {
            val characteristics = cameraManager.getCameraCharacteristics(cameraId)
            val outputResolutions = characteristics.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP)!!
                .getOutputSizes(ImageFormat.YUV_420_888)

            val resolutions = mutableListOf<String>()
            for (size in outputResolutions) {
                resolutions.add("${size.width}x${size.height}")
            }

            return resolutions.toTypedArray()
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Could not get characteristics of camera with ID \"${cameraId}\" due to error.", exp)
            return null
        } catch (exp: SecurityException) {
            Log.e(TAG, "Could not get characteristics of camera with ID \"${cameraId}\" due to security exception.", exp)
            return null
        }
    }

    fun startCaptureSession(cameraId: String, width: Int, height: Int) = ProcessLifecycleOwner.get().lifecycleScope.launch(Dispatchers.Main) {
        Log.i(TAG, "Initializing camera for new capture session.")

        camera = openCamera(cameraId)
        if (camera == null) {
            stopThread(cameraThread)
            return@launch
        }

        imageReaderThread = HandlerThread("ImageReaderThread").apply { start() }
        imageReaderHandler = Handler(imageReaderThread!!.looper)

        imageReader = ImageReader.newInstance(width, height, ImageFormat.YUV_420_888, 3).apply {
            setOnImageAvailableListener({
                if (!isCaptureSessionOpen.get()) {
                    return@setOnImageAvailableListener
                }

                val image = imageReader!!.acquireLatestImage()
                if (image != null) {
                    val yPlane  = image.planes[0]
                    val uPlane  = image.planes[1]

                    val yBuffer = yPlane.buffer
                    val uBuffer = uPlane.buffer
                    val vBuffer = image.planes[2].buffer

                    frameCallback.onFrameReady(
                        yBuffer,
                        uBuffer,
                        vBuffer,
                        yBuffer.capacity(),
                        uBuffer.capacity(),
                        vBuffer.capacity(),
                        yPlane.rowStride,
                        uPlane.rowStride,
                        uPlane.pixelStride
                    )

                    image.close()
                }
            }, imageReaderHandler)
        }

        captureSession = createCaptureSession()
        if (captureSession == null) {
            imageReader?.close()
            camera?.close()

            captureSessionExecutor?.shutdown()
            stopThread(imageReaderThread)
            stopThread(cameraThread)
            return@launch
        }

        val result = startCapture()
        if (result == null) {
            captureSession?.close()
            imageReader?.close()
            camera?.close()

            captureSessionExecutor?.shutdown()
            stopThread(imageReaderThread)
            stopThread(cameraThread)
            return@launch
        }

        isCaptureSessionOpen.set(true)

        Log.i(TAG, "Capture session started.")
        UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_CAPTURE_STARTED, result.toString())
    }

    fun stopCaptureSession() {
        isCaptureSessionOpen.set(false)

        captureSession?.close()
        imageReader?.close()
        camera?.close()

        captureSessionExecutor?.shutdown()
        stopThread(imageReaderThread)
        stopThread(cameraThread)

        Log.i(TAG, "Capture session ended.")
    }

    private fun startCapture(): Int? {
        try {
            val captureRequestBuilder = camera!!.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW).apply {
                addTarget(imageReader!!.surface)
            }

            return captureSession!!.setRepeatingRequest(captureRequestBuilder.build(), null, cameraHandler)
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Camera device with ID \"${camera!!.id}\" erred out with camera access exception.", exp)
            UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_ACCESS_ERROR, exp.message)
            return null
        } catch (exp: SecurityException) {
            Log.e(TAG, "Camera device with ID \"${camera!!.id}\" erred out with security exception.", exp)
            UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_ACCESS_ERROR, exp.message)
            return null
        }
    }

    private suspend fun createCaptureSession(): CameraCaptureSession? = suspendCoroutine { cont ->
        captureSessionExecutor = Executors.newSingleThreadExecutor()

        try {
            camera!!.createCaptureSession(SessionConfiguration(
                SessionConfiguration.SESSION_REGULAR,
                listOf(OutputConfiguration(imageReader!!.surface)),
                captureSessionExecutor!!,
                object : CameraCaptureSession.StateCallback() {
                    override fun onConfigured(session: CameraCaptureSession) {
                        Log.i(TAG, "Camera device with ID \"${camera!!.id}\" has been configured.")
                        UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_CONFIGURED, camera!!.id)

                        cont.resume(session)
                    }

                    override fun onConfigureFailed(session: CameraCaptureSession) {
                        val exc = RuntimeException("Camera ${camera!!.id} session configuration failed")
                        Log.e(TAG, "Camera device with ID \"${camera!!.id}\" could not be configured.", exc)
                        UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_CONFIGURE_ERRED, camera!!.id)

                        cont.resume(null)
                    }
                }
            ))
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Capture session for camera with ID \"${camera!!.id}\" could not be created due to error.", exp)
            UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_CONFIGURE_ERRED, camera!!.id)

            cont.resume(null)
        } catch (exp: SecurityException) {
            Log.e(TAG, "Capture session for camera with ID \"${camera!!.id}\" could not be created due to security exception.", exp)
            UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_CONFIGURE_ERRED, camera!!.id)

            cont.resume(null)
        }
    }

    @SuppressLint("MissingPermission")
    private suspend fun openCamera(cameraId: String): CameraDevice? = suspendCoroutine { cont ->
        cameraThread = HandlerThread("CameraThread").apply { start() }
        cameraHandler = Handler(cameraThread!!.looper)

        try {
            cameraManager.openCamera(cameraId, object : CameraDevice.StateCallback() {
                override fun onOpened(camera: CameraDevice) {
                    Log.i(TAG, "Camera device with ID \"${camera.id}\" has been opened.")
                    UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_CONNECTED, camera.id)

                    cont.resume(camera)
                }

                override fun onDisconnected(camera: CameraDevice) {
                    Log.i(TAG, "Camera device with ID \"${camera.id}\" disconnected.")
                    UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_DISCONNECTED, camera.id)
                }

                override fun onError(camera: CameraDevice, error: Int) {
                    val message = when (error) {
                        ERROR_CAMERA_DEVICE -> "Fatal (device)"
                        ERROR_CAMERA_DISABLED -> "Device policy"
                        ERROR_CAMERA_IN_USE -> "Camera in use"
                        ERROR_CAMERA_SERVICE -> "Fatal (service)"
                        ERROR_MAX_CAMERAS_IN_USE -> "Maximum cameras in use"
                        else -> "Unknown"
                    }

                    Log.e(TAG, "Camera device with ID \"${camera.id}\" erred out with: $error: $message")
                    UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_ERRED, "{\"device\":\"${camera.id}\",\"errorCode\":$error}")

                    cont.resume(null)
                }
            }, cameraHandler)
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Camera could not be opened due to error.", exp)
            UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_ERRED, exp.message)

            cont.resume(null)
        } catch (exp: SecurityException) {
            Log.e(TAG, "Camera could not be opened due to security exception.", exp)
            UnityPlayer.UnitySendMessage(unityCallbackListener, ON_CAMERA_DEVICE_ERRED, exp.message)

            cont.resume(null)
        }
    }

    private fun stopThread(thread: HandlerThread?) {
        thread?.quitSafely()
        try {
            thread?.join()
        } catch (e: InterruptedException) {
            Log.e(TAG, "stopThread: ${e.message}")
        }
    }
}