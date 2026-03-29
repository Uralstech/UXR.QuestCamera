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
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    public abstract class StatefulResource
    {
        /// <summary>The state of this resource.</summary>
        /// <remarks>This does NOT indicate disposal status, and is just an indicator for if you can use this resource.</remarks>
        public ResourceState State { get; protected set; }

        /// <summary>Waits until the resource opens or errs out.</summary>
        /// <exception cref="ObjectDisposedException"/>
        public WaitUntil WaitForInitialization()
        {
            ThrowIfDisposed();
            return new WaitUntil(() => State != ResourceState.Initializing);
        }

#if UNITY_6000_0_OR_NEWER
        /// <summary>Waits until the resource opens or errs out.</summary>
        /// <param name="timeout">Maximum time to wait.</param>
        /// <param name="onTimeout">The action to perform when the <paramref name="timeout"/> is reached.</param>
        /// <param name="timeoutMode">Mode in which to measure time to determine <paramref name="timeout"/>.</param>
        /// <exception cref="ObjectDisposedException"/>
        public WaitUntil WaitForInitialization(TimeSpan timeout, Action onTimeout, WaitTimeoutMode timeoutMode = WaitTimeoutMode.Realtime)
        {
            ThrowIfDisposed();
            return new(() => State != ResourceState.Initializing, timeout, onTimeout, timeoutMode);
        }
#endif

        /// <summary>Waits until the resource opens or errs out.</summary>
        /// <returns><see langword="true"/> if opened successfully, <see langword="false"/> otherwise.</returns>
        /// <exception cref="ObjectDisposedException"/>
        public async Task<bool> WaitForInitializationAsync(CancellationToken token = default)
        {
            ThrowIfDisposed();

            while (State == ResourceState.Initializing)
            {
#if !UNITY_6000_0_OR_NEWER
                await Task.Delay(10, token);
#else
                await Awaitable.NextFrameAsync(token);
#endif
            }

            return State == ResourceState.Valid;
        }

        protected abstract void ThrowIfDisposed();
    }
}