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
using UnityEngine;
using UnityEngine.Events;

namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// A wrapper for a native Camera2 CameraDevice.
    /// </summary>
    public class CameraDevice : MonoBehaviour
    {
        /// <summary>
        /// Error codes that can be returned by the native CameraDevice wrapper.
        /// </summary>
        public enum ErrorCode
        {
            /// <summary>Unknown error.</summary>
            Unknown = 0,

            /// <summary>The camera device is in use already.</summary>
            CameraInUse = 1,

            /// <summary>The camera device could not be opened because there are too many other open camera devices.</summary>
            MaxCamerasInUse = 2,

            /// <summary>The camera device could not be opened due to a device policy.</summary>
            CameraDisabled = 3,

            /// <summary>The camera device has encountered a fatal error.</summary>
            CameraDeviceError = 4,

            /// <summary>The camera service has encountered a fatal error.</summary>
            CameraServiceError = 5,

            /// <summary>The native code encountered a CameraAccessException.</summary>
            CameraAccessException = 1000,

            /// <summary>The native code encountered a SecurityException.</summary>
            SecurityException = 1001,
        }

        /// <summary>
        /// The current assumed state of the native CameraDevice wrapper.
        /// </summary>
        public NativeWrapperState CurrentState { get; private set; }

        /// <summary>
        /// The ID of the camera being wrapped.
        /// </summary>
        public string CameraId => _cameraDevice?.Get<string>("id");

        /// <summary>
        /// Is the native CameraDevice wrapper active and usable?
        /// </summary>
        public bool IsActiveAndUsable => _cameraDevice?.Get<bool>("isActiveAndUsable") ?? false;

        /// <summary>
        /// Invoked when the CameraDevice is opened.
        /// </summary>
        public UnityEvent OnDeviceOpened = new();

        /// <summary>
        /// Invoked when the CameraDevice is closed.
        /// </summary>
        public UnityEvent OnDeviceClosed = new();

        /// <summary>
        /// Invoked when the CameraDevice encounters an error.
        /// </summary>
        public UnityEvent<ErrorCode> OnDeviceErred = new();

        /// <summary>
        /// Invoked when the CameraDevice is disconnected.
        /// </summary>
        public UnityEvent OnDeviceDisconnected = new();

        private AndroidJavaObject _cameraDevice;

        protected void OnDestroy()
        {
            Release();
        }

        /// <summary>
        /// Sets the native CameraDevice wrapper.
        /// </summary>
        internal void SetCameraDevice(AndroidJavaObject cameraDevice)
        {
            _cameraDevice = cameraDevice;
        }

        /// <summary>
        /// Waits until the CameraDevice is open or erred out.
        /// </summary>
        public IEnumerator WaitForInitialization()
        {
            yield return new WaitUntil(() => CurrentState != NativeWrapperState.Initializing);
        }

#if UNITY_6000_0_OR_NEWER
        /// <summary>
        /// Waits until the CameraDevice is open or erred out.
        /// </summary>
        /// <remarks>
        /// Requires Unity 6.0 or higher.
        /// </remarks>
        /// <returns>The current state of the CameraDevice.</returns>
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
        /// Creates a new repeating/continuous capture session for use.
        /// </summary>
        /// <remarks>
        /// Once you have finished using the capture session, call <see cref="CaptureSessionObject{T}.Destroy"/>
        /// to close the session and free up native and compute shader resources.
        /// </remarks>
        /// <param name="resolution">The resolution of the capture.</param>
        /// <param name="captureTemplate">The capture template to use for the capture</param>
        /// <returns>A new capture session wrapper. May be null if the current camera device is not usable.</returns>
        public CaptureSessionObject<ContinuousCaptureSession> CreateContinuousCaptureSession(Resolution resolution, CaptureTemplate captureTemplate = CaptureTemplate.Preview)
        {
            if (!IsActiveAndUsable)
                return null;

            CameraFrameForwarder cameraFrameForwarder = new();
            GameObject wrapperGO = new($"{nameof(ContinuousCaptureSession)} ({CameraId}, {DateTime.UtcNow.Ticks})");

            AndroidJavaObject nativeObject = _cameraDevice?.Call<AndroidJavaObject>("createContinuousCaptureSession",
                wrapperGO.name, cameraFrameForwarder, resolution.width, resolution.height, (int)captureTemplate);
            if (nativeObject is null)
            {
                Destroy(wrapperGO);
                return null;
            }

            ContinuousCaptureSession wrapper = wrapperGO.AddComponent<ContinuousCaptureSession>();
            wrapper.SetCaptureSession(nativeObject);

            YUVToRGBAConverter converter = wrapper.gameObject.AddComponent<YUVToRGBAConverter>();
            converter.SetupCameraFrameForwarder(cameraFrameForwarder, resolution);

            return new CaptureSessionObject<ContinuousCaptureSession>(wrapperGO, wrapper, converter, cameraFrameForwarder);
        }

        /// <summary>
        /// Creates a new on-demand capture session for use.
        /// </summary>
        /// <remarks>
        /// Once you have finished using the capture session, call <see cref="CaptureSessionObject{T}.Destroy"/>
        /// to close the session and free up native and compute shader resources.
        /// </remarks>
        /// <param name="resolution">The resolution of the capture.</param>
        /// <returns>A new capture session wrapper. May be null if the current camera device is not usable.</returns>
        public CaptureSessionObject<OnDemandCaptureSession> CreateOnDemandCaptureSession(Resolution resolution)
        {
            if (!IsActiveAndUsable)
                return null;

            CameraFrameForwarder cameraFrameForwarder = new();
            GameObject wrapperGO = new($"{nameof(OnDemandCaptureSession)} ({CameraId}, {DateTime.UtcNow.Ticks})");

            AndroidJavaObject nativeObject = _cameraDevice?.Call<AndroidJavaObject>("createOnDemandCaptureSession",
                wrapperGO.name, cameraFrameForwarder, resolution.width, resolution.height);
            if (nativeObject is null)
            {
                Destroy(wrapperGO);
                return null;
            }

            OnDemandCaptureSession wrapper = wrapperGO.AddComponent<OnDemandCaptureSession>();
            wrapper.SetCaptureSession(nativeObject);

            YUVToRGBAConverter converter = wrapper.gameObject.AddComponent<YUVToRGBAConverter>();
            converter.SetupCameraFrameForwarder(cameraFrameForwarder, resolution);

            return new CaptureSessionObject<OnDemandCaptureSession>(wrapperGO, wrapper, converter, cameraFrameForwarder);
        }

        /// <summary>
        /// Releases the CameraDevice's native resources, and makes it unusable.
        /// </summary>
        public void Release()
        {
            _cameraDevice?.Call("close");
            _cameraDevice?.Dispose();
            _cameraDevice = null;
        }

        /// <summary>
        /// Releases the CameraDevice's native resources, and destroys its GameObject.
        /// </summary>
        public void Destroy()
        {
            Release();
            Destroy(gameObject);
        }

        #region Native Callbacks
#pragma warning disable IDE1006 // Naming Styles
        public void _onDeviceOpened(string _)
        {
            CurrentState = NativeWrapperState.Opened;
            OnDeviceOpened?.Invoke();
        }

        public void _onDeviceClosed(string _)
        {
            CurrentState = NativeWrapperState.Closed;
            OnDeviceClosed?.Invoke();
        }

        public void _onDeviceErred(string errorCodeStr)
        {
            CurrentState = NativeWrapperState.Closed;

            ErrorCode errorCode = ErrorCode.Unknown;
            if (int.TryParse(errorCodeStr, out int errorCodeInt))
                errorCode = (ErrorCode)errorCodeInt;

            OnDeviceErred?.Invoke(errorCode);
        }

        public void _onDeviceDisconnected(string _)
        {
            CurrentState = NativeWrapperState.Closed;
            OnDeviceDisconnected?.Invoke();
        }
#pragma warning restore IDE1006 // Naming Styles
        #endregion
    }
}