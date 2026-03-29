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

import android.content.Context
import android.hardware.camera2.CameraAccessException
import android.hardware.camera2.CameraManager
import android.util.Log

class CameraDeviceProvider private constructor(context: Context) {

    companion object {
        private const val TAG = "UXRQC.DeviceProvider"

        private var instance: CameraDeviceProvider? = null

        @JvmStatic
        fun getInstance(context: Context)  : CameraDeviceProvider {

            return instance ?: CameraDeviceProvider(context).also {
                instance = it
            }
        }
    }

    private val cameraManager = context.applicationContext.getSystemService(
        Context.CAMERA_SERVICE
    ) as CameraManager

    fun getDevices() : Array<CameraCharacteristicsProvider> {
        return try {
            Log.i(TAG, "Getting devices.")

            val cameraIds = cameraManager.cameraIdList
            val result = mutableListOf<CameraCharacteristicsProvider>()

            for (cameraId in cameraIds) {
                val characteristics = cameraManager.getCameraCharacteristics(cameraId)
                result.add(CameraCharacteristicsProvider(cameraId, characteristics))
            }

            result.toTypedArray()
        } catch (ex: CameraAccessException) {
            Log.e(TAG, "Could not get device details due to access error", ex)
            emptyArray<CameraCharacteristicsProvider>()
        }
    }

    fun openCamera(deviceManager: CameraDeviceManager, cameraId: String) {
        deviceManager.initialize(cameraId, cameraManager)
    }
}