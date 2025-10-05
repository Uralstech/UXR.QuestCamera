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

import android.Manifest
import android.hardware.camera2.CameraAccessException
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.CameraManager
import android.util.Log
import androidx.annotation.RequiresPermission
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit

/**
 * Wrapper class for [CameraDevice].
 */
class CameraDeviceWrapper private constructor(val id: String, private val callbacks: Callbacks) {
    interface Callbacks {
        fun onDeviceOpened(id: String)
        fun onDeviceClosed(id: String)
        fun onDeviceErred(id: String?, errorCode: Int)
        fun onDeviceDisconnected(id: String)
    }

    companion object {
        /** Logcat tag. */
        private const val TAG = "CameraDeviceWrapper"

        private const val ERROR_CODE_CAMERA_ACCESS_EXCEPTION = 1000
        private const val ERROR_CODE_SECURITY_EXCEPTION = 1001
    }

    /** Is this object active and usable? */
    @Volatile
    var isActiveAndUsable: Boolean = true
        private set

    /** [java.util.concurrent.ExecutorService] for [cameraDevice]. */
    private val cameraExecutor = Executors.newSingleThreadExecutor()

    /** The camera device being wrapped by this object. */
    private var cameraDevice: CameraDevice? = null

    @RequiresPermission(Manifest.permission.CAMERA)
    constructor(id: String, callbacks: Callbacks, cameraManager: CameraManager) : this(id, callbacks) {
        try {
            cameraManager.openCamera(id, cameraExecutor, object : CameraDevice.StateCallback() {
                override fun onOpened(camera: CameraDevice) {
                    Log.i(TAG, "Camera device with ID \"$id\" opened.")
                    cameraDevice = camera

                    callbacks.onDeviceOpened(camera.id)
                }

                override fun onClosed(camera: CameraDevice) {
                    Log.i(TAG, "Camera device with ID \"$id\" closed.")
                    callbacks.onDeviceClosed(camera.id)
                }

                override fun onDisconnected(camera: CameraDevice) {
                    Log.i(TAG, "Camera device with ID \"$id\" disconnected.")
                    close()

                    callbacks.onDeviceDisconnected(camera.id)
                }

                override fun onError(camera: CameraDevice, error: Int) {
                    Log.i(TAG, "Camera device with ID \"$id\" erred out, code: $error")
                    close()

                    callbacks.onDeviceErred(camera.id, error)
                }
            })
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Camera could not be opened due to a camera access exception.", exp)
            close()

            callbacks.onDeviceErred(null, ERROR_CODE_CAMERA_ACCESS_EXCEPTION)
        } catch (exp: SecurityException) {
            Log.e(TAG, "Camera could not be opened due to a security exception.", exp)
            close()

            callbacks.onDeviceErred(null, ERROR_CODE_SECURITY_EXCEPTION)
        }
    }

    /**
     * Gets the [CameraDevice] this object is wrapping, or logs an error and returns null if it was not found.
     */
    private fun getActiveDevice(): CameraDevice? {
        if (!isActiveAndUsable || cameraDevice == null) {
            Log.e(TAG, "Tried to use an unusable CameraDeviceWrapper for camera ID \"$id\"!")
            return null
        }
        return cameraDevice
    }

    /**
     * Creates a new capture session with a repeating capture request and a wrapper for it.
     */
    fun createContinuousCaptureSession(
        callbacks: CaptureSessionWrapper.Callbacks,
        width: Int, height: Int,
        captureTemplate: Int): RepeatingCaptureSessionWrapper? {

        val cameraDevice = getActiveDevice() ?: return null
        Log.i(TAG, "Creating new repeating camera session for camera with ID \"$id\".")
        return RepeatingCaptureSessionWrapper(cameraDevice, captureTemplate, callbacks, width, height)
    }

    /**
     * Creates a new on-demand capture session and a wrapper for it.
     */
    fun createOnDemandCaptureSession(
        callbacks: CaptureSessionWrapper.Callbacks,
        width: Int, height: Int): OnDemandCaptureSessionWrapper? {

        val cameraDevice = getActiveDevice() ?: return null
        Log.i(TAG, "Creating new on-demand camera session for camera with ID \"$id\".")
        return OnDemandCaptureSessionWrapper(cameraDevice, CameraDevice.TEMPLATE_PREVIEW, callbacks, width, height)
    }

    /**
     * Creates a new SurfaceTexture-based capture session and a wrapper for it.
     */
    fun createSurfaceTextureCaptureSession(
        timeStamp: Long, callbacks: STCaptureSessionWrapper.Callbacks,
        width: Int, height: Int,
        captureTemplate: Int): STCaptureSessionWrapper? {

        val cameraDevice = getActiveDevice() ?: return null
        Log.i(TAG, "Creating new SurfaceTexture-based camera session for camera with ID \"$id\".")

        val session = STCaptureSessionWrapper(timeStamp, callbacks, cameraDevice, width, height, captureTemplate)
        if (!session.tryRegister()) {
            session.close()
            return null
        }

        return session
    }

    /**
     * Releases associated resources and closes the camera device.
     * This results in [isActiveAndUsable] being set to false.
     */
    fun close() {
        if (!isActiveAndUsable) {
            return
        }

        Log.i(TAG, "Closing camera device wrapper for camera with ID \"$id\".")
        isActiveAndUsable = false

        if (cameraDevice != null) {
            cameraDevice?.close()
            cameraDevice = null
        } else {
            callbacks.onDeviceClosed("")
        }

        cameraExecutor.shutdown()
        try {
            cameraExecutor.awaitTermination(10, TimeUnit.SECONDS)
        } catch (e: InterruptedException) {
            Log.e(TAG, "Interrupted while trying to stop the background thread", e)
        }

        Log.i(TAG, "Camera device closed.")
    }
}