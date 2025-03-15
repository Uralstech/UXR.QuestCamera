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
        /// Capture template to use when recording.
        /// </summary>
        public enum CaptureTemplate
        {
            /// <summary>Default value, do not use.</summary>
            Default = 0,

            /// <summary>Creates a request suitable for a camera preview window.</summary>
            /// <remarks><a href="https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_PREVIEW"/></remarks>
            Preview = 1,

            /// <summary>Creates a request suitable for still image capture.</summary>
            /// <remarks><a href="https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_STILL_CAPTURE"/></remarks>
            StillCapture = 2,

            /// <summary>Creates a request suitable for video recording.</summary>
            /// <remarks><a href="https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_RECORD"/></remarks>
            Record = 3,

            /// <summary>Creates a request suitable for still image capture while recording video.</summary>
            /// <remarks><a href="https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_VIDEO_SNAPSHOT"/></remarks>
            VideoSnapshot = 4,

            /// <summary>Creates a request suitable for zero shutter lag still capture.</summary>
            /// <remarks><a href="https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_ZERO_SHUTTER_LAG"/></remarks>
            ZeroShutterLag = 5,
        }

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
        /// Simple class for grouping capture session related GameObjects.
        /// </summary>
        public class CaptureSessionObject
        {
            /// <summary>
            /// The GameObject containing the <see cref="CaptureSession"/> and <see cref="TextureConverter"/> components.
            /// </summary>
            public readonly GameObject GameObject;

            /// <summary>
            /// The capture session wrapper.
            /// </summary>
            public readonly CaptureSession CaptureSession;

            /// <summary>
            /// The YUV to RGBA texture converter.
            /// </summary>
            public readonly YUVToRGBAConverter TextureConverter;

            internal CaptureSessionObject(GameObject gameObject, CaptureSession captureSession, YUVToRGBAConverter textureConverter)
            {
                GameObject = gameObject;
                CaptureSession = captureSession;
                TextureConverter = textureConverter;
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
        /// Creates a new capture session for use.
        /// </summary>
        /// <remarks>
        /// Once you have finished using the capture session, either destroy its GameObject or call <see cref="CaptureSession.Release"/>
        /// and <see cref="YUVToRGBAConverter.Release"/> to close the session and free up native and compute shader resources.
        /// </remarks>
        /// <param name="resolution">The resolution of the capture.</param>
        /// <param name="captureTemplate">The capture template to use for the capture</param>
        /// <param name="isContinuous">Is this capture continuous (repeating) or for a single frame?</param>
        /// <returns>A new capture session wrapper. May be null if the current camera device is not usable.</returns>
        public CaptureSessionObject CreateCaptureSession(Resolution resolution, CaptureTemplate captureTemplate = CaptureTemplate.Preview, bool isContinuous = true)
        {
            if (!IsActiveAndUsable)
                return null;

            CameraFrameForwarder cameraFrameForwarder = new();
            GameObject wrapperGO = new($"{nameof(CaptureSession)} ({CameraId}, {DateTime.UtcNow.Ticks})");

            AndroidJavaObject nativeObject = _cameraDevice?.Call<AndroidJavaObject>("createCaptureSession",
                wrapperGO.name, cameraFrameForwarder, resolution.width, resolution.height, (int)captureTemplate, isContinuous);
            if (nativeObject is null)
            {
                Destroy(wrapperGO);
                return null;
            }

            CaptureSession wrapper = wrapperGO.AddComponent<CaptureSession>();
            wrapper.SetCaptureSession(nativeObject);

            YUVToRGBAConverter converter = wrapper.gameObject.AddComponent<YUVToRGBAConverter>();
            converter.SetupCameraFrameForwarder(cameraFrameForwarder, resolution);

            return new CaptureSessionObject(wrapperGO, wrapper, converter);
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