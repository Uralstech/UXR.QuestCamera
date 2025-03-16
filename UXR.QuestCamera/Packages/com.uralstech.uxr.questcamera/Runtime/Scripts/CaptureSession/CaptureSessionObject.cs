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

using UnityEngine;

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// Simple class for grouping capture session related components to their GameObject.
    /// </summary>
    public class CaptureSessionObject<T> where T : ContinuousCaptureSession
    {
        /// <summary>
        /// The GameObject containing the <see cref="CaptureSession"/> and <see cref="TextureConverter"/> components.
        /// </summary>
        public readonly GameObject GameObject;

        /// <summary>
        /// The capture session wrapper.
        /// </summary>
        public readonly T CaptureSession;

        /// <summary>
        /// The YUV to RGBA texture converter.
        /// </summary>
        public readonly YUVToRGBAConverter TextureConverter;

        /// <summary>
        /// The camera frame forwarder.
        /// </summary>
        /// <remarks>
        /// You can add additional <see cref="YUVToRGBAConverter"/>s to this
        /// to have multiple streams of the same capture session.
        /// </remarks>
        public readonly CameraFrameForwarder CameraFrameForwarder;

        internal CaptureSessionObject(GameObject gameObject, T captureSession, YUVToRGBAConverter textureConverter, CameraFrameForwarder cameraFrameForwarder)
        {
            GameObject = gameObject;
            CaptureSession = captureSession;
            TextureConverter = textureConverter;
            CameraFrameForwarder = cameraFrameForwarder;
        }

        /// <summary>
        /// Destroys the GameObject to release all native resources.
        /// </summary>
        public void Destroy()
        {
            CaptureSession.Release();
            TextureConverter.Release();

            UnityEngine.Object.Destroy(GameObject);
        }
    }
}