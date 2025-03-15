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
import android.content.Context
import android.graphics.ImageFormat
import android.hardware.camera2.CameraAccessException
import android.hardware.camera2.CameraCharacteristics
import android.hardware.camera2.CameraManager
import android.util.Log
import androidx.annotation.RequiresPermission
import com.unity3d.player.UnityPlayer
import java.lang.ref.WeakReference

/**
 * Script to manage Camera2 resources.
 */
class Camera2Wrapper {

    companion object {
        /** Logcat tag. */
        private const val TAG = "Camera2Wrapper"

        private var Instance: WeakReference<Camera2Wrapper> = WeakReference(null)

        @JvmStatic
        fun getInstance(): Camera2Wrapper {
            val instance = Instance.get()
            return if (instance != null) {
                instance
            } else {
                val newInstance = Camera2Wrapper()
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

    /**
     * Gets the IDs of the available camera devices.
     */
    fun getCameraDevices(): Array<String>? {
        return try {
            Log.i(TAG, "Getting list of cameras.")
            cameraManager.cameraIdList
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Could not get camera IDs due to a camera access exception.", exp)
            null
        } catch (exp: SecurityException) {
            Log.e(TAG, "Could not get camera IDs due to a security exception.", exp)
            null
        }
    }

    /**
     * Gets the resolutions supported by the camera for the YAV 4:2:0 format.
     */
    fun getSupportedResolutionsForCamera(camera: String): Array<String>? {
        try {
            Log.i(TAG, "Getting supported resolutions for camera with ID \"$camera\".")

            val characteristics = cameraManager.getCameraCharacteristics(camera)
            val outputResolutions = characteristics.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP)!!
                .getOutputSizes(ImageFormat.YUV_420_888)

            val resolutions = mutableListOf<String>()
            for (size in outputResolutions) {
                resolutions.add("${size.width}x${size.height}")
            }

            Log.i(TAG, "Got supported resolutions.")
            return resolutions.toTypedArray()
        } catch (exp: CameraAccessException) {
            Log.e(TAG, "Could not get characteristics of camera with ID \"${camera}\" due to a camera access exception.", exp)
            return null
        } catch (exp: SecurityException) {
            Log.e(TAG, "Could not get characteristics of camera with ID \"${camera}\" due to a security exception.", exp)
            return null
        }
    }

    /**
     * Creates a wrapper for a camera device, and tries to open it.
     * The returned wrapper may or may not have an open device, the
     * exact status of the device will be sent through the wrapper's
     * callbacks.
     */
    @RequiresPermission(Manifest.permission.CAMERA)
    fun openCameraDevice(camera: String, unityListener: String): CameraDeviceWrapper {
        Log.i(TAG, "Opening camera device with ID \"$camera\"")

        val wrapper = CameraDeviceWrapper(camera, unityListener)
        wrapper.openDevice(cameraManager)

        return wrapper
    }
}