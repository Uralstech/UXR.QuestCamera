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
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Uralstech.UXR.QuestCamera.GLES;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>Manages a camera device.</summary>
    public sealed class CameraDevice : StatefulResource, IAsyncDisposable
    {
        /// <summary>Error codes from the native plugin.</summary>
        public enum ErrorCode
        {
            Unknown                 = 0,

            /// <summary>The camera device is in use already.</summary>
            CameraInUse             = 1,

            /// <summary>The camera device could not be opened because there are too many other open camera devices.</summary>
            MaxCamerasInUse         = 2,

            /// <summary>The camera device could not be opened due to a device policy.</summary>
            CameraDisabled          = 3,

            /// <summary>The camera device has encountered a fatal error.</summary>
            CameraDeviceError       = 4,

            /// <summary>The camera service has encountered a fatal error.</summary>
            CameraServiceError      = 5,

            /// <summary>
            /// cameraId was null, or the cameraId does not match any currently or previously available camera device.
            /// </summary>
            IllegalArgumentError    = 1000,

            /// <summary>The camera is disabled by device policy, has been disconnected, or is being used by a higher-priority camera API client.</summary>
            CameraAccessError       = 1001,

            /// <summary>The application does not have permission to access the camera.</summary>
            SecurityError           = 1002,
        }

        /// <summary>Java proxy to handle native callbacks.</summary>
        /// <remarks>All event callbacks will be on a Java thread, and are performance sensitive.</remarks>
        public sealed class Proxy : AndroidJavaProxy
        {
            private const string ClassName = "com.uralstech.uxr.questcamera.CameraDeviceManager$Callbacks";

            /// <summary>Invoked when the camera is opened.</summary>
            public event Action? OnOpened;

            /// <summary>Invoked when the camera is closed.</summary>
            public event Action? OnClosed;

            /// <summary>Invoked when the camera encounters an error.</summary>
            public event Action<ErrorCode>? OnErred;
            
            /// <summary>Invoked when the camera is disconnected.</summary>
            public event Action? OnDisconnected;

            public Proxy() : base(ClassName) { }

            /// <inheritdoc/>
            /// <exclude />
            public override IntPtr Invoke(string methodName, IntPtr javaArgs)
            {
                switch (methodName)
                {
                    case "onOpened":
                        OnOpened?.Invoke(); break;
                    
                    case "onClosed":
                        OnClosed?.Invoke(); break;

                    case "onErred":
                        int errorCode = JNIExtensions.UnboxIntElement(javaArgs, 0);
                        OnErred?.Invoke((ErrorCode)errorCode); break;

                    case "onDisconnected":
                        OnDisconnected?.Invoke(); break;

                    default:
                        return base.Invoke(methodName, javaArgs);
                }

                return IntPtr.Zero;
            }
        }
        
        /// <summary>Invoked when the camera is opened, along with the camera ID.</summary>
        public event Action<string>? OnDeviceOpened;

        /// <summary>Invoked when the camera is closed, along with the camera ID.</summary>
        public event Action<string>? OnDeviceClosed;

        /// <summary>Invoked when the camera encounters an error, along with the camera ID.</summary>
        public event Action<string, ErrorCode>? OnDeviceErred;

        /// <summary>Invoked when the camera is disconnected, along with the camera ID.</summary>
        public event Action<string>? OnDeviceDisconnected;

        /// <summary>The ID of the camera.</summary>
        public readonly string CameraId;

        /// <summary>Native callback handler.</summary>
        public readonly Proxy NativeProxy;

        internal readonly AndroidJavaObject _native;
        private bool _disposed;

        public CameraDevice(string cameraId)
        {
            const string ClassName = "com.uralstech.uxr.questcamera.CameraDeviceManager";

            CameraId = cameraId;

            NativeProxy = new Proxy();
            NativeProxy.OnOpened        += OnOpenedNative;
            NativeProxy.OnClosed        += OnClosedNative;
            NativeProxy.OnErred         += OnErredNative;
            NativeProxy.OnDisconnected  += OnDisconnectedNative;

            _native = new AndroidJavaObject(ClassName, NativeProxy);
        }

        /// <summary>Creates a continuous capture pipeline (session + linked YUV to RGBA converter) for use.</summary>
        /// <param name="textureFormat">See <see cref="YUVConverter(Resolution, ComputeShaderKernel, GraphicsFormat)"/> for default texture format.</param>
        /// <returns>The created pipeline, or <see langword="null"/> if creation failed.</returns>
        /// <inheritdoc cref="CreateContinuousSession"/>
        public CapturePipeline<ContinuousCaptureSession>? CreateContinuousPipeline(Resolution resolution,
            CaptureTemplate template = CaptureTemplate.Preview, StreamUseCase streamUseCase = StreamUseCase.None, GraphicsFormat textureFormat = GraphicsFormat.None)
        {
            ThrowIfDisposed();
            ContinuousCaptureSession session = CreateContinuousSession(resolution, template, streamUseCase);
            if (session.State == ResourceState.Invalid)
                return null;

            YUVConverter converter = new(resolution, textureFormat);
            session.NativeProxy.OnFrameReady += converter.OnFrameReady;

            return new CapturePipeline<ContinuousCaptureSession>(session, converter);
        }

        /// <summary>Creates an on-demand capture pipeline (session + linked YUV to RGBA converter) for use.</summary>
        /// <param name="textureFormat">See <see cref="YUVConverter(Resolution, ComputeShaderKernel, GraphicsFormat)"/> for default texture format.</param>
        /// <returns>The created pipeline, or <see langword="null"/> if creation failed.</returns>
        /// <inheritdoc cref="CreateOnDemandSession"/>
        public CapturePipeline<OnDemandCaptureSession>? CreateOnDemandPipeline(Resolution resolution,
            StreamUseCase streamUseCase = StreamUseCase.None, GraphicsFormat textureFormat = GraphicsFormat.None)
        {
            ThrowIfDisposed();
            OnDemandCaptureSession session = CreateOnDemandSession(resolution, streamUseCase);
            if (session.State == ResourceState.Invalid)
                return null;

            YUVConverter converter = new(resolution, textureFormat);
            session.NativeProxy.OnFrameReady += converter.OnFrameReady;

            return new CapturePipeline<OnDemandCaptureSession>(session, converter);
        }

        /// <summary>Creates a continuous capture session for use.</summary>
        /// <remarks>To shut down and dispose the session, use <see cref="CaptureSessionBase{T}.DisposeAsync()"/> (inherited by <see cref="ContinuousCaptureSession"/>).</remarks>
        /// <param name="resolution">The capture resolution. Must be from <see cref="CameraInfo.SupportedResolutions"/>.</param>
        /// <param name="template">The template to use for the captures.</param>
        /// <param name="streamUseCase">The stream use case for this session. Must be from <see cref="CameraInfo.SupportedStreamUseCases"/> or <see cref="StreamUseCase.None"/>.</param>
        /// <returns>Returns the session. Check <see cref="StatefulResource.State"/> (inherited by <see cref="ContinuousCaptureSession"/>) for the state of the session.</returns>
        /// <exception cref="ObjectDisposedException"/>
        public ContinuousCaptureSession CreateContinuousSession(Resolution resolution, CaptureTemplate template = CaptureTemplate.Preview, StreamUseCase streamUseCase = StreamUseCase.None)
        {
            ThrowIfDisposed();
            long[] streamUseCases = streamUseCase is not StreamUseCase.None
                ? new long[] { (long)streamUseCase }
                : Array.Empty<long>();

            ContinuousCaptureSession session = new(resolution);

            bool initResult = _native.Call<bool>("initializeSession", session._native, (int)template, streamUseCases);
            if (!initResult)
            {
                // Invalidates the session immediately.
                _ = session.DisposeAsync();
            }

            return session;
        }

        /// <summary>Creates an on-demand capture session for use.</summary>
        /// <remarks>To shut down and dispose the session, use <see cref="CaptureSessionBase{T}.DisposeAsync()"/> (inherited by <see cref="OnDemandCaptureSession"/>).</remarks>
        /// <param name="resolution">The capture resolution. Must be from <see cref="CameraInfo.SupportedResolutions"/>.</param>
        /// <param name="streamUseCase">The stream use case for this session. Must be from <see cref="CameraInfo.SupportedStreamUseCases"/> or <see cref="StreamUseCase.None"/>.</param>
        /// <returns>Returns the session. Check <see cref="StatefulResource.State"/> (inherited by <see cref="OnDemandCaptureSession"/>) for the state of the session.</returns>
        /// <exception cref="ObjectDisposedException"/>
        public OnDemandCaptureSession CreateOnDemandSession(Resolution resolution, StreamUseCase streamUseCase = StreamUseCase.None)
        {
            ThrowIfDisposed();
            long[] streamUseCases = streamUseCase is not StreamUseCase.None
                ? new long[] { (long)StreamUseCase.Preview, (long)streamUseCase }
                : Array.Empty<long>();

            OnDemandCaptureSession session = new(resolution);

            bool initResult = _native.Call<bool>("initializeSession", session._native, (int)CaptureTemplate.Preview, streamUseCases);
            if (!initResult)
            {
                // Invalidates the session immediately.
                _ = session.DisposeAsync();
            }

            return session;
        }

        /// <summary>Creates a generic OpenGL-ES based capture session.</summary>
        /// <remarks>
        /// This initializes a native capture session backed by a SurfaceTexture and a GLES conversion job.
        /// The returned session must be started manually (e.g., via its run loop or single-run methods)
        /// and disposed using <see cref="GLESCaptureSession.DisposeAsync"/>.
        /// </remarks>
        /// <param name="resolution">The capture resolution. Must be from <see cref="CameraInfo.SupportedResolutions"/>.</param>
        /// <param name="template">The template to use for the captures.</param>
        /// <param name="streamUseCase">The stream use case for this session. Must be from <see cref="CameraInfo.SupportedStreamUseCases"/> or <see cref="StreamUseCase.None"/>.</param>
        /// <param name="textureFormat">The output texture format for the converted frames. See <see cref="GLESCaptureSession(Resolution, GraphicsFormat)"/> for default.</param>
        /// <returns>Returns the session. Check <see cref="StatefulResource.State"/> (inherited by <see cref="GLESCaptureSession"/>) for the state of the session.</returns>
        /// <exception cref="ObjectDisposedException"/>
        public async ValueTask<GLESCaptureSession> CreateGLESSessionAsync(Resolution resolution,
            CaptureTemplate template = CaptureTemplate.Preview, StreamUseCase streamUseCase = StreamUseCase.None,
            GraphicsFormat textureFormat = GraphicsFormat.None)
        {
            ThrowIfDisposed();
            long[] streamUseCases = streamUseCase is not StreamUseCase.None
                ? new long[] { (long)streamUseCase }
                : Array.Empty<long>();

            GLESCaptureSession session = new(resolution, textureFormat);
            uint textureId = await session.SetupJobAsync();

            if (textureId == 0)
            {
                // Invalidates the session immediately.
                _ = session.DisposeAsync();
                return session;
            }

            bool initResult = _native.Call<bool>("initializeGLESSession", session._native, (int)template, streamUseCases, resolution.width, resolution.height, (int)textureId);
            if (!initResult)
            {
                // Invalidates the session immediately.
                _ = session.DisposeAsync();
            }

            return session;
        }

        /// <summary>Closes the camera (if not already closed) and releases native resources.</summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            
            _disposed = true;
            State = ResourceState.Invalid;
            
            TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnClosed() => tcs.TrySetResult(true);
            NativeProxy.OnClosed += OnClosed;

            try
            {
                if (_native.Call<bool>("close"))
                    await tcs.Task;
            }
            finally
            {
                NativeProxy.OnClosed        -= OnClosed;

                NativeProxy.OnOpened        -= OnOpenedNative;
                NativeProxy.OnClosed        -= OnClosedNative;
                NativeProxy.OnErred         -= OnErredNative;
                NativeProxy.OnDisconnected  -= OnDisconnectedNative;

                _native.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        #region Callbacks
        private void OnOpenedNative()
        {
            State = ResourceState.Valid;
            OnDeviceOpened?.OnMainThread(CameraId).Forget();
        }

        private void OnClosedNative()
        {
            State = ResourceState.Invalid;
            OnDeviceClosed?.OnMainThread(CameraId).Forget();
        }

        private void OnErredNative(ErrorCode code)
        {
            State = ResourceState.Invalid;
            OnDeviceErred?.OnMainThread(CameraId, code).Forget();
        }

        private void OnDisconnectedNative()
        {
            State = ResourceState.Invalid;
            OnDeviceDisconnected?.OnMainThread(CameraId).Forget();
        }

        #endregion

        protected override void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CameraDevice));
        }

        ~CameraDevice()
        {
            Debug.LogWarning(
                $"A {nameof(CameraDevice)} object was finalized by the garbage collector without being properly disposed.\n" +
                $"The native camera device **may not be closed** and resources may still be held.\n\n" +
                $"To fix this, ensure that you explicitly call `{nameof(DisposeAsync)}` or wrap it in an `await using` block:\n" +
                $"    await using var camera = QuestCameraManager.Instance.OpenCamera(...);\n" +
                $"This ensures that the camera is closed on the correct Unity thread."
            );
        }
    }
}