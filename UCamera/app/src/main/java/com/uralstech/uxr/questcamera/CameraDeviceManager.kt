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

import android.annotation.SuppressLint
import android.hardware.camera2.CameraAccessException
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.CameraManager
import android.util.Log
import java.util.concurrent.Executors

@SuppressLint("MissingPermission")
class CameraDeviceManager(private val callbacks: Callbacks) {

    companion object {
        private const val TAG = "UXRQC.DeviceManager"
    }

    interface Callbacks {
        fun onOpened()
        fun onClosed()
        fun onErred(code: Int)
        fun onDisconnected()
    }

    @Volatile
    private var isDisposed = false

    @Volatile
    private var willInvokeCloseCallback = true

    @Volatile
    private var device: CameraDevice? = null

    private val executor = Executors.newSingleThreadExecutor()

    private val stateCallbacks = object : CameraDevice.StateCallback() {

        override fun onOpened(camera: CameraDevice) {
            device = camera

            Log.i(TAG, "${camera.id} opened!")
            callbacks.onOpened()
        }

        override fun onClosed(camera: CameraDevice) {
            willInvokeCloseCallback = false

            Log.i(TAG, "${camera.id} closed.")
            callbacks.onClosed()
        }

        override fun onDisconnected(camera: CameraDevice) {
            device = device ?: camera
            close()

            Log.i(TAG, "${camera.id} disconnected.")
            callbacks.onDisconnected()
        }

        override fun onError(camera: CameraDevice, error: Int) {
            device = device ?: camera
            close()

            Log.e(TAG, "${camera.id} erred with: $error.")
            callbacks.onErred(error)
        }
    }

    internal fun initialize(cameraId: String, cameraManager: CameraManager) {
        executor.submit {
            try {
                cameraManager.openCamera(cameraId, executor, stateCallbacks)
            } catch (ex: CameraAccessException) {
                Log.e(TAG, "Couldn't open $cameraId due to access error", ex)
                close()

                callbacks.onErred(CustomErrorCodes.CAMERA_ACCESS)
            } catch (ex: SecurityException) {
                Log.e(TAG, "Couldn't open $cameraId due to security error", ex)
                close()

                callbacks.onErred(CustomErrorCodes.SECURITY)
            } catch (ex: IllegalArgumentException) {
                Log.e(TAG, "Couldn't open $cameraId due to illegal arguments", ex)
                close()

                callbacks.onErred(CustomErrorCodes.ILLEGAL_ARGUMENT)
            }
        }
    }

    private fun getDeviceLogged() : CameraDevice? {
        val device = device
        if (isDisposed || device == null) {
            Log.e(TAG, "Tried to use closed/faulted device!")
        }

        return device
    }

    fun initializeSession(
        session: ContinuousCaptureSessionManager,
        captureTemplate: Int, streamUseCases: LongArray
    ) : Boolean {

        val device = getDeviceLogged() ?: return false
        session.initialize(device, captureTemplate, streamUseCases)
        return true
    }

    fun initializeGLESSession(
        session: GLESCaptureSessionManager,
        captureTemplate: Int, streamUseCases: LongArray,
        width: Int, height: Int, sourceTextureId: Int
    ) : Boolean {

        val device = getDeviceLogged() ?: return false
        session.initialize(device, captureTemplate, streamUseCases, width, height, sourceTextureId)
        return true
    }

    // Returns true if caller should wait for onClosed callback
    fun close() : Boolean {
        if (isDisposed) {
            return willInvokeCloseCallback
        }

        isDisposed = true
        Log.i(TAG, "Closing device.")

        val device = device
        if (device == null) {
            executor.shutdown()
            willInvokeCloseCallback = false

            Log.i(TAG, "No device to close, executor shut down.")
            return false
        }

        executor.submit {
            device.close()
            executor.shutdown()

            Log.i(TAG, "Device closed, executor shut down.")
        }

        return willInvokeCloseCallback
    }
}