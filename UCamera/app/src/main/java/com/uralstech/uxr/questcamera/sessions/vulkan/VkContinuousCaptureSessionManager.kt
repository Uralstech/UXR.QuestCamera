package com.uralstech.uxr.questcamera.sessions.vulkan

import android.graphics.ImageFormat
import android.hardware.HardwareBuffer
import android.hardware.camera2.CameraDevice
import android.media.Image
import android.util.Log
import com.uralstech.uxr.questcamera.CustomErrorCodes
import com.uralstech.uxr.questcamera.sessions.ImageReaderCaptureSessionManagerBase

open class VkContinuousCaptureSessionManager protected constructor(width: Int, height: Int, protected val callbacks: Callbacks, logPrefix: String)
    : ImageReaderCaptureSessionManagerBase(width, height, ImageFormat.PRIVATE, 3,
        HardwareBuffer.USAGE_GPU_SAMPLED_IMAGE, callbacks, logPrefix)  {

    interface Callbacks : CallbacksBase {

        fun canTakeImage() : Boolean

        fun onFrameReady(image: Image)
    }

    constructor(width: Int, height: Int, callbacks: Callbacks) : this(width, height, callbacks, "VkContinuousSession")

    override fun imageHandover(image: Image) {
        if (!callbacks.canTakeImage()) {
            image.close()
            return
        }

        callbacks.onFrameReady(image)
    }

    internal open fun initialize(
        camera: CameraDevice, captureTemplate: Int, streamUseCases: LongArray
    ) {
        Log.i(TAG, "($logPrefix) Initializing session.")

        try {
            val outputConfiguration = outputConfigWith(imageSurface, streamUseCases, 0)
            startSession(camera, listOf(outputConfiguration)) { session ->
                setRepeatingRequest(session, imageSurface, captureTemplate)
            }
        } catch (ex: IllegalArgumentException) {
            close()

            Log.e(TAG, "($logPrefix) Could initialize due to illegal argument (likely streamUseCases)", ex)
            callbacks.onConfigureFailed(CustomErrorCodes.ILLEGAL_ARGUMENT)
        }
    }
}