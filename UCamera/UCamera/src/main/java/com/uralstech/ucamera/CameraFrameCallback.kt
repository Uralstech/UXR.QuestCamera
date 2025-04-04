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

import java.nio.ByteBuffer

/**
 * Callback receiver interface for Unity objects that
 * want to receive the individual frames from the camera.
 */
interface CameraFrameCallback {
    /**
     * Called when a new YUV 4:2:0 encoded frame is ready.
     */
    fun onFrameReady(
        yBuffer: ByteBuffer,
        uBuffer: ByteBuffer,
        vBuffer: ByteBuffer,
        ySize: Int,
        uSize: Int,
        vSize: Int,
        yRowStride: Int,
        uvRowStride: Int,
        uvPixelStride: Int
    )
}