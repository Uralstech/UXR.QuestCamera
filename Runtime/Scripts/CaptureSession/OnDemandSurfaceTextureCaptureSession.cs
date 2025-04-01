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

using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// On-demand version of <see cref="SurfaceTextureCaptureSession"/>.
    /// </summary>
    /// <remarks>
    /// The results of this capture session may be more noisy compared to <see cref="OnDemandCaptureSession"/>.
    /// Requires OpenGL ES 3.0 or higher as the project's Graphics API. Works with single and multi-threaded rendering.
    /// </remarks>
    public class OnDemandSurfaceTextureCaptureSession : SurfaceTextureCaptureSession
    {
        /// <summary>
        /// The ID of the OpenGL camera texture.
        /// </summary>
        private int _cameraTextureId;

        /// <summary>
        /// Updates the texture with the latest image from the camera.
        /// </summary>
        /// <param name="onDone">Callback for when the operation is completed.</param>
        public void RequestCapture(Action<Texture2D> onDone)
        {
            if (_cameraTextureId == 0)
                return;

            TextureUpdateData data = new()
            {
                CameraTextureId = _cameraTextureId,
                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<Action>(NativeTextureCallback)
            };

            CallNativeEvent(data, UpdateSurfaceTextureEvent, () =>
            {
                GL.InvalidateState();
                onDone?.Invoke(Texture);
            });
        }

        /// <summary>
        /// Updates the texture with the latest image from the camera.
        /// </summary>
        public IEnumerator RequestCapture()
        {
            bool isDone = false;
            RequestCapture(_ => isDone = true);

            yield return new WaitUntil(() => isDone);
        }

#if UNITY_6000_0_OR_NEWER
        /// <summary>
        /// Updates the texture with the latest image from the camera.
        /// </summary>
        /// <returns>The updated texture.</returns>
        public async Awaitable<Texture2D> RequestCaptureAsync()
        {
            bool isDone = false;
            Texture2D texture = null;
            RequestCapture(t => (texture, isDone) = (t, true));

            await Awaitable.MainThreadAsync();
            while (!isDone)
                await Awaitable.NextFrameAsync();

            return texture;
        }
#endif

        /// <inheritdoc/>
        public override void _onCaptureCompleted(string textureId)
        {
            if (!int.TryParse(textureId, out _cameraTextureId))
                Debug.LogError($"Could not get texture ID for {nameof(OnDemandSurfaceTextureCaptureSession)}.{nameof(_onCaptureCompleted)}.");
        }
    }
}