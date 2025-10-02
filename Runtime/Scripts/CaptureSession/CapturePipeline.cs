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

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// Simple class for grouping a capture session and its texture converter.
    /// </summary>
    public class CapturePipeline<T> where T : ContinuousCaptureSession, IDisposable
    {
        /// <summary>
        /// The capture session wrapper.
        /// </summary>
        public readonly T CaptureSession;

        /// <summary>
        /// The YUV to RGBA texture converter.
        /// </summary>
        public readonly YUVToRGBAConverter TextureConverter;

        public CapturePipeline(T captureSession, YUVToRGBAConverter textureConverter)
        {
            CaptureSession = captureSession;
            TextureConverter = textureConverter;
        }

        private bool _disposed = false;
        public void Dispose()
        {
            if (_disposed)
                return;

            CaptureSession.Dispose();
            TextureConverter.Dispose();
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}