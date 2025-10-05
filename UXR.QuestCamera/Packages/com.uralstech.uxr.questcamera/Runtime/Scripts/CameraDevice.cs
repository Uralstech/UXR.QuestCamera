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
using System.Threading;
using UnityEngine;
using Uralstech.UXR.QuestCamera.SurfaceTextureCapture;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// A wrapper for a native Camera2 CameraDevice.
    /// </summary>
    public class CameraDevice : AndroidJavaProxy, IDisposable
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
        /// The ID of the camera being wrapped. This value is <b>not</b> cached - it is requested from the native plugin on every access.
        /// </summary>
        public string CameraId => _cameraDevice?.Get<string>("id") ?? throw new ObjectDisposedException(nameof(CameraDevice));

        /// <summary>
        /// Is the native CameraDevice wrapper active and usable?
        /// </summary>
        public bool IsActiveAndUsable => _cameraDevice?.Get<bool>("isActiveAndUsable") ?? throw new ObjectDisposedException(nameof(CameraDevice));

        /// <summary>
        /// Invoked when the CameraDevice is opened, along with the camera ID.
        /// </summary>
        public event Action<string>? OnDeviceOpened;

        /// <summary>
        /// Invoked when the CameraDevice is closed, along with the camera ID.
        /// </summary>
        public event Action<string>? OnDeviceClosed;

        /// <summary>
        /// Invoked when the CameraDevice encounters an error, along with the camera ID.
        /// </summary>
        public event Action<string, ErrorCode>? OnDeviceErred;

        /// <summary>
        /// Invoked when the CameraDevice is disconnected, along with the camera ID.
        /// </summary>
        public event Action<string>? OnDeviceDisconnected;

        internal protected AndroidJavaObject? _cameraDevice;

        public CameraDevice() : base("com.uralstech.ucamera.CameraDeviceWrapper$Callbacks") { }

        /// <inheritdoc/>
        public override IntPtr Invoke(string methodName, IntPtr javaArgs)
        {
            string cameraId;
            switch (methodName)
            {
                case "onDeviceOpened":
                    CurrentState = NativeWrapperState.Opened;

                    cameraId = JNIExtensions.UnboxStringElement(javaArgs, 0);
                    OnDeviceOpened.InvokeOnMainThread(cameraId);
                    return IntPtr.Zero;

                case "onDeviceClosed":
                    CurrentState = NativeWrapperState.Closed;

                    cameraId = JNIExtensions.UnboxStringElement(javaArgs, 0);
                    OnDeviceClosed.InvokeOnMainThread(cameraId);
                    return IntPtr.Zero;

                case "onDeviceDisconnected":
                    cameraId = JNIExtensions.UnboxStringElement(javaArgs, 0);
                    OnDeviceDisconnected.InvokeOnMainThread(cameraId);
                    return IntPtr.Zero;

                case "onDeviceErred":
                    cameraId = JNIExtensions.UnboxStringElement(javaArgs, 0);
                    int errorCode = JNIExtensions.UnboxIntElement(javaArgs, 1);
                    OnDeviceErred.InvokeOnMainThread(cameraId, (ErrorCode)errorCode);
                    return IntPtr.Zero;
            }

            return base.Invoke(methodName, javaArgs);
        }

        /// <summary>
        /// Waits until the CameraDevice is open or erred out.
        /// </summary>
        public WaitUntil WaitForInitialization() => new(() => CurrentState != NativeWrapperState.Initializing);

        /// <summary>
        /// Closes the camera device.
        /// </summary>
        public WaitUntil Close()
        {
            _cameraDevice?.Call("close");
            return new WaitUntil(() => CurrentState != NativeWrapperState.Closed);
        }

#if UNITY_6000_0_OR_NEWER
        /// <summary>
        /// Waits until the CameraDevice is open or erred out.
        /// </summary>
        /// <returns>The current state of the CameraDevice.</returns>
        public async Awaitable<NativeWrapperState> WaitForInitializationAsync(CancellationToken token = default)
        {
            await Awaitable.MainThreadAsync();
            while (CurrentState == NativeWrapperState.Initializing && !token.IsCancellationRequested)
                await Awaitable.NextFrameAsync(token);

            return CurrentState;
        }

        /// <summary>
        /// Closes the camera device.
        /// </summary>
        /// <returns><see langword="true"/> if the device was closed successfully, <see langword="false"/> if the operation was cancelled.</returns>
        public async Awaitable<bool> CloseAsync(CancellationToken token = default)
        {
            await Awaitable.MainThreadAsync();

            _cameraDevice?.Call("close");
            while (CurrentState != NativeWrapperState.Closed && !token.IsCancellationRequested)
                await Awaitable.NextFrameAsync(token);

            return CurrentState == NativeWrapperState.Closed;
        }
#endif

        private bool _disposed = false;

        /// <summary>
        /// Releases native plugin resources.
        /// Make sure to call <see cref="Close()"/> or <see cref="CloseAsync(CancellationToken)"/> before disposing this object.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _cameraDevice?.Dispose();
            _cameraDevice = null;
            _disposed = true;

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Creates a new repeating/continuous capture session for use.
        /// </summary>
        /// <remarks>
        /// Once you have finished using the capture session, call <see cref="CapturePipeline{T}.CloseAndDispose()"/>
        /// or <see cref="CapturePipeline{T}.CloseAndDisposeAsync(CancellationToken)"/> to close and dispose the
        /// session to free up native and compute shader resources.
        /// </remarks>
        /// <param name="resolution">The resolution of the capture.</param>
        /// <param name="captureTemplate">The capture template to use for the capture</param>
        /// <returns>A new capture session wrapper, or <see langword="null"/> if any errors occurred.</returns>
        public CapturePipeline<ContinuousCaptureSession>? CreateContinuousCaptureSession(Resolution resolution, CaptureTemplate captureTemplate = CaptureTemplate.Preview)
        {
            if (!IsActiveAndUsable)
                return null;

            ContinuousCaptureSession captureSession = new();
            AndroidJavaObject? nativeObject = _cameraDevice?.Call<AndroidJavaObject>("createContinuousCaptureSession", captureSession, resolution.width, resolution.height, (int)captureTemplate);
            if (nativeObject is null)
            {
                captureSession.Dispose();
                return null;
            }

            YUVToRGBAConverter converter = new(resolution);
            captureSession.OnFrameReady += converter.OnFrameReady;
            captureSession._captureSession = nativeObject;

            return new CapturePipeline<ContinuousCaptureSession>(captureSession, converter);
        }

        /// <summary>
        /// Creates a new on-demand capture session for use.
        /// </summary>
        /// <inheritdoc cref="CreateContinuousCaptureSession(Resolution, CaptureTemplate)"/>
        public CapturePipeline<OnDemandCaptureSession>? CreateOnDemandCaptureSession(Resolution resolution)
        {
            if (!IsActiveAndUsable)
                return null;

            OnDemandCaptureSession captureSession = new();
            AndroidJavaObject? nativeObject = _cameraDevice?.Call<AndroidJavaObject>("createOnDemandCaptureSession", captureSession, resolution.width, resolution.height);
            if (nativeObject is null)
            {
                captureSession.Dispose();
                return null;
            }

            YUVToRGBAConverter converter = new(resolution);
            captureSession.OnFrameReady += converter.OnFrameReady;
            captureSession._captureSession = nativeObject;

            return new CapturePipeline<OnDemandCaptureSession>(captureSession, converter);
        }

        /// <summary>
        /// Creates a new OpenGL SurfaceTexture based capture session for use. Equivalent to <see cref="ContinuousCaptureSession"/>.
        /// </summary>
        /// <remarks>
        /// This is an experimental capture session type that uses a native OpenGL texture to capture images for better performance.
        /// The results of this capture session may be more noisy compared to <see cref="ContinuousCaptureSession"/>.
        /// Requires OpenGL ES 3.0 as the project's Graphics API. Works with single and multi-threaded rendering.
        /// 
        /// Once you have finished using the capture session, call <see cref="SurfaceTextureCaptureSession.Close()"/>
        /// or <see cref="SurfaceTextureCaptureSession.CloseAsync(CancellationToken)"/> to close and
        /// <see cref="SurfaceTextureCaptureSession.Dispose()"/> to dispose the session to free up native resources.
        /// </remarks>
        /// <param name="resolution">The resolution of the capture.</param>
        /// <param name="captureTemplate">The capture template to use for the capture</param>
        /// <returns>A new capture session wrapper, or <see langword="null"/> if any errors occurred.</returns>
        public SurfaceTextureCaptureSession? CreateSurfaceTextureCaptureSession(Resolution resolution, CaptureTemplate captureTemplate = CaptureTemplate.Preview)
        {
            if (!IsActiveAndUsable)
                return null;

            long timestamp = DateTime.Now.Ticks;
            SurfaceTextureCaptureSession session = new(resolution);

            AndroidJavaObject? nativeObject = _cameraDevice?.Call<AndroidJavaObject>("createSurfaceTextureCaptureSession", timestamp, session, resolution.width, resolution.height, (int)captureTemplate);
            if (nativeObject is null)
            {
                session.Dispose();
                return null;
            }

            session._captureSession = nativeObject;
            session.InitializeNative(timestamp);
            return session;
        }

        /// <summary>
        /// Creates a new on-demand OpenGL SurfaceTexture based capture session for use. Equivalent to <see cref="OnDemandCaptureSession"/>.
        /// </summary>
        /// <inheritdoc cref="CreateSurfaceTextureCaptureSession(Resolution, CaptureTemplate)"/>
        public OnDemandSurfaceTextureCaptureSession? CreateOnDemandSurfaceTextureCaptureSession(Resolution resolution, CaptureTemplate captureTemplate = CaptureTemplate.Preview)
        {
            if (!IsActiveAndUsable)
                return null;

            long timestamp = DateTime.Now.Ticks;
            OnDemandSurfaceTextureCaptureSession session = new(resolution);

            AndroidJavaObject? nativeObject = _cameraDevice?.Call<AndroidJavaObject>("createSurfaceTextureCaptureSession", timestamp, session, resolution.width, resolution.height, (int)captureTemplate);
            if (nativeObject is null)
            {
                session.Dispose();
                return null;
            }

            session._captureSession = nativeObject;
            session.InitializeNative(timestamp);
            return session;
        }
    }
}