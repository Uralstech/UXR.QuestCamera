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

package com.uralstech.uxr.questcamera.sessions

import android.media.Image
import android.media.ImageReader
import android.os.Handler
import android.os.HandlerThread
import android.util.Log
import android.view.Surface

abstract class ImageReaderCaptureSessionManagerBase
    protected constructor(width: Int, height: Int, format: Int, maxImages: Int, usage: Long, callbacks: CallbacksBase, logPrefix: String)
        : CaptureSessionManagerBase(callbacks, logPrefix) {

    private val imageThread = HandlerThread("ImageReaderThread").apply { start() }
    private val imageHandler = Handler(imageThread.looper)
    private val imageReader = ImageReader.newInstance(width, height, format, maxImages, usage).apply {
        setOnImageAvailableListener({
            val image = try {
                it.acquireLatestImage() ?: return@setOnImageAvailableListener
            } catch (ex: IllegalStateException) {
                Log.e(TAG, "Could not acquire new image due to exception.", ex)
                return@setOnImageAvailableListener
            }

            if (isDisposed) {
                image.close()
                return@setOnImageAvailableListener
            }

            imageHandover(image)
        }, imageHandler)
    }

    protected val imageSurface: Surface
        get() = imageReader.surface

    /** Implementation *MUST* handle closure of [image]. */
    protected abstract fun imageHandover(image: Image)

    override fun additionalCloseWork() {

        imageReader.setOnImageAvailableListener(null, imageHandler)
        imageThread.quitSafely()

        try {
            imageThread.join(5000)
            if (imageThread.isAlive) {
                Log.w(TAG, "($logPrefix) ImageReader thread still alive due to timeout, ignoring thread.")
            }
        } catch (ex: InterruptedException) {
            Log.e(TAG, "($logPrefix) Interrupted while trying to stop imageReader thread", ex)
        }

        imageReader.close()
        Log.i(TAG, "($logPrefix) ImageReader closed.")
    }
}