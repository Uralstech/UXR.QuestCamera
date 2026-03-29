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

using System;
using System.Threading.Tasks;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>A wrapper for a complete capture-conversion pipeline.</summary>
    /// <typeparam name="T">The capture session type.</typeparam>
    public sealed class CapturePipeline<T> : IAsyncDisposable
        where T : ContinuousCaptureSession
    {
        /// <summary>The capture session.</summary>
        public readonly T Session;

        /// <summary>The YUV to RGBA texture converter.</summary>
        public readonly YUVConverter Converter;

        private bool _disposed;

        public CapturePipeline(T session, YUVConverter converter)
        {
            Session = session;
            Converter = converter;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;
            
            await Session.DisposeAsync();
            Converter.Dispose();

            GC.SuppressFinalize(this);
        }
    }
}
