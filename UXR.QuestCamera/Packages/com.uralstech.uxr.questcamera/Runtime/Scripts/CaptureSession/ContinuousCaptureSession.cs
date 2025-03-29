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

using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// A wrapper for a native Camera2 CaptureSession and ImageReader.
    /// </summary>
    /// <remarks>
    /// This is different from <see cref="OnDemandCaptureSession"/> as it returns a
    /// continuous stream of images.
    /// </remarks>
    public class ContinuousCaptureSession : MonoBehaviour
    {
        /// <summary>
        /// The current assumed state of the native CaptureSession wrapper.
        /// </summary>
        public NativeWrapperState CurrentState { get; private set; }

        /// <summary>
        /// Is the native CaptureSession wrapper active and usable?
        /// </summary>
        public bool IsActiveAndUsable => _captureSession?.Get<bool>("isActiveAndUsable") ?? false;

        /// <summary>
        /// Called when the session has been configured.
        /// </summary>
        public UnityEvent OnSessionConfigured = new();

        /// <summary>
        /// Called when the session could not be configured.
        /// </summary>
        public UnityEvent<string> OnSessionConfigurationFailed = new();

        /// <summary>
        /// Called when the session request has been set.
        /// </summary>
        public UnityEvent OnSessionRequestSet = new();

        /// <summary>
        /// Called when the session request could not be set.
        /// </summary>
        public UnityEvent<string> OnSessionRequestFailed = new();

        /// <summary>
        /// The native capture session object.
        /// </summary>
        protected AndroidJavaObject _captureSession;

        protected virtual void OnDestroy()
        {
            Release();
        }

        /// <summary>
        /// Sets the native CaptureSession wrapper.
        /// </summary>
        internal void SetCaptureSession(AndroidJavaObject nativeObject)
        {
            _captureSession = nativeObject;
        }

        /// <summary>
        /// Waits until the CaptureSession is open or erred out.
        /// </summary>
        public IEnumerator WaitForInitialization()
        {
            yield return new WaitUntil(() => CurrentState != NativeWrapperState.Initializing);
        }

#if UNITY_6000_0_OR_NEWER
        /// <summary>
        /// Waits until the CaptureSession is open or erred out.
        /// </summary>
        /// <remarks>
        /// Requires Unity 6.0 or higher.
        /// </remarks>
        /// <returns>The current state of the CaptureSession.</returns>
        public async Awaitable<NativeWrapperState> WaitForInitializationAsync()
        {
            if (CurrentState != NativeWrapperState.Initializing)
                return CurrentState;

            await Awaitable.MainThreadAsync();
            while (CurrentState == NativeWrapperState.Initializing)
                await Awaitable.NextFrameAsync();

            return CurrentState;
        }
#endif

        /// <summary>
        /// Releases the CaptureSession's native resources, and makes it unusable.
        /// </summary>
        public void Release()
        {
            _captureSession?.Call("close");
            _captureSession?.Dispose();
            _captureSession = null;
        }

        #region Native Callbacks
#pragma warning disable IDE1006 // Naming Styles
        public void _onSessionConfigured(string _)
        {
            OnSessionConfigured?.Invoke();
        }

        public void _onSessionConfigurationFailed(string reason)
        {
            CurrentState = NativeWrapperState.Closed;
            OnSessionConfigurationFailed?.Invoke(reason);
        }

        public void _onSessionRequestSet(string _)
        {
            CurrentState = NativeWrapperState.Opened;
            OnSessionRequestSet?.Invoke();
        }

        public void _onSessionRequestFailed(string reason)
        {
            CurrentState = NativeWrapperState.Closed;
            OnSessionRequestFailed?.Invoke(reason);
        }
#pragma warning restore IDE1006 // Naming Styles
        #endregion
    }
}