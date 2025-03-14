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
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import androidx.annotation.RequiresPermission
import com.unity3d.player.UnityPlayer

/**
 * Wrapper class for [CameraDevice].
 */
class CameraDeviceWrapper(
    val id: String,
    private val unityListener: String) {

    companion object {
        /** Logcat tag. */
        private const val TAG = "CameraDeviceWrapper"

        private const val ON_DEVICE_OPENED          = "_onDeviceOpened"
        private const val ON_DEVICE_CLOSED          = "_onDeviceClosed"

        private const val ON_DEVICE_ERRED           = "_onDeviceErred"
        private const val ON_DEVICE_DISCONNECTED    = "_onDeviceDisconnected"
    }

    /** Is this object active and usable? */
    var isActiveAndUsable: Boolean = true

    /** [HandlerThread] where all camera operations run. */
    private val cameraThread = HandlerThread("CameraThread").apply { start() }

    /** [Handler] corresponding to [cameraThread]. */
    private val cameraHandler = Handler(cameraThread.looper)

    /** The camera device being wrapped by this object. */
    private var cameraDevice: CameraDevice? = null

    /**
     * Opens the camera with the required exception checking and callbacks.
     */
    @RequiresPermission(Manifest.permission.CAMERA)
    internal fun openDevice(cameraManager: CameraManager) {
        try {
            cameraManager.openCamera(id, object : CameraDevice.StateCallback() {
                override fun onOpened(camera: CameraDevice) {
                    Log.i(TAG, "Camera device with ID \"$id\" opened.")
                    UnityPlayer.UnitySendMessage(unityListener, ON_DEVICE_OPENED, "")

                    cameraDevice = camera
                }

                override fun onClosed(camera: CameraDevice) {
                    Log.i(TAG, "Camera device with ID \"$id\" closed.")
                    UnityPlayer.UnitySendMessage(unityListener, ON_DEVICE_CLOSED, "")
                }

                override fun onDisconnected(camera: CameraDevice) {
                    Log.i(TAG, "Camera device with ID \"$id\" disconnected.")
                    UnityPlayer.UnitySendMessage(unityListener, ON_DEVICE_DISCONNECTED, "")

                    close()
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

                    Log.i(TAG, "Camera device with ID \"$id\" erred out: $message")
                    UnityPlayer.UnitySendMessage(unityListener, ON_DEVICE_ERRED, error.toString())

                    close()
                }
            }, cameraHandler)
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Camera could not be opened due to a camera access exception.", exp)
            UnityPlayer.UnitySendMessage(unityListener, ON_DEVICE_ERRED, "1000")

            close()
        } catch (exp: SecurityException) {
            Log.e(TAG, "Camera could not be opened due to a security exception.", exp)
            UnityPlayer.UnitySendMessage(unityListener, ON_DEVICE_ERRED, "1001")

            close()
        }
    }

    /**
     * Creates a new capture session and a wrapper for it.
     */
    fun createCaptureSession(
        unityListener: String,
        frameCallback: CameraFrameCallback,
        width: Int, height: Int,
        captureTemplate: Int, isRepeating: Boolean): CaptureSessionWrapper? {

        val cameraDevice = this.cameraDevice
        if (!isActiveAndUsable || cameraDevice == null) {
            Log.e(TAG, "Tried to call createCaptureSession on unusable CameraDeviceWrapper!")
            return null
        }

        Log.i(TAG, "Creating new camera session for camera with ID \"$id\".")

        val wrapper = CaptureSessionWrapper(unityListener, frameCallback, width, height)
        wrapper.startCaptureSession(cameraDevice, captureTemplate, isRepeating)

        return wrapper
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

        cameraDevice?.close()
        cameraThread.quitSafely()
    }
}