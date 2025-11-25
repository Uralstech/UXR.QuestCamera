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
using System.Threading.Tasks;
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// A wrapper for a native Camera2 CaptureSession and ImageReader.
    /// </summary>
    /// <remarks>
    /// This is different from <see cref="OnDemandCaptureSession"/> as it returns a
    /// continuous stream of images.
    /// </remarks>
    public class ContinuousCaptureSession : AndroidJavaProxy, IAsyncDisposable
    {
        /// <summary>
        /// The current assumed state of the native CaptureSession wrapper.
        /// </summary>
        public NativeWrapperState CurrentState { get; private set; }

        /// <summary>
        /// Called when the session has been configured.
        /// </summary>
        public event Action? OnSessionConfigured;

        /// <summary>
        /// Called when the session could not be configured, and a boolean value indicating if the failure was caused due to a camera access/security exception.
        /// </summary>
        public event Action<bool>? OnSessionConfigurationFailed;

        /// <summary>
        /// Called when the session request has been set.
        /// </summary>
        public event Action? OnSessionRequestSet;

        /// <summary>
        /// Called when the session request could not be set.
        /// </summary>
        public event Action? OnSessionRequestFailed;

        /// <summary>
        /// Called when the session has started actively processing capture requests.
        /// </summary>
        public event Action? OnSessionActive;

        /// <summary>
        /// Called when the session is closed.
        /// </summary>
        public event Action? OnSessionClosed;

        /// <summary>
        /// Callback for processing the YUV 4:2:0 frame.
        /// </summary>
        /// <remarks>
        /// This callback may not be called from the main thread.
        /// <list type="table">
        ///     <listheader>
        ///         <term>Parameters</term>
        ///     </listheader>
        ///     <item>
        ///         <term>yBuffer (IntPtr)</term>
        ///         <description>The pointer to this frame's Y (luminance) data.</description>
        ///     </item>
        ///     <item>
        ///        <term>yBufferSize (long)</term>
        ///        <description>The size of the Y buffer in bytes.</description>
        ///    </item>
        ///     <item>
        ///         <term>uBuffer (IntPtr)</term>
        ///         <description>The pointer to this frame's U (color) data.</description>
        ///     </item>
        ///     <item>
        ///         <term>vBuffer (IntPtr)</term>
        ///         <description>The pointer to this frame's V (color) data.</description>
        ///     </item>
        ///     <item>
        ///        <term>uvBufferSize (long)</term>
        ///        <description>The size of the U and V buffers in bytes.</description>
        ///    </item>
        ///     <item>
        ///         <term>yRowStride (int)</term>
        ///         <description>The size of each row of the image in yBuffer in bytes.</description>
        ///     </item>
        ///     <item>
        ///         <term>uvRowStride (int)</term>
        ///         <description>The size of each row of the image in uBuffer and vBuffer in bytes.</description>
        ///     </item>
        ///     <item>
        ///         <term>uvPixelStride (int)</term>
        ///         <description>The size of a pixel in a row of the image in uBuffer and vBuffer in bytes.</description>
        ///     </item>
        ///     <item>
        ///        <term>timestamp (long)</term>
        ///        <description>The timestamp the frame was captured at in nanoseconds.</description>
        ///    </item>
        /// </list>
        /// </remarks>
        public event Action<IntPtr, long, IntPtr, IntPtr, long, int, int, int, long>? OnFrameReady;

        /// <summary>
        /// Called when the native wrapper has been completely disposed.
        /// </summary>
        protected event Action? OnDisposeCompleted;

        /// <summary>
        /// The native capture session object.
        /// </summary>
        internal protected AndroidJavaObject? _captureSession;

        public ContinuousCaptureSession() : base("com.uralstech.ucamera.CaptureSessionWrapper$Callbacks") { }

        /// <inheritdoc/>
        public override IntPtr Invoke(string methodName, IntPtr javaArgs)
        {
            switch (methodName)
            {
                case "onSessionConfigured":
                    OnSessionConfigured.InvokeOnMainThread().HandleAnyException();
                    return IntPtr.Zero;

                case "onSessionConfigurationFailed":
                    bool isAccessOrSecurityError = JNIExtensions.UnboxBoolElement(javaArgs, 0);
                    OnSessionConfigurationFailed.InvokeOnMainThread(isAccessOrSecurityError).HandleAnyException();

                    if (!isAccessOrSecurityError)
                    {
                        CurrentState = NativeWrapperState.Closed;
                        OnSessionClosed.InvokeOnMainThread().HandleAnyException();
                    }
                    return IntPtr.Zero;

                case "onSessionRequestSet":
                    OnSessionRequestSet.InvokeOnMainThread().HandleAnyException();
                    return IntPtr.Zero;

                case "onSessionRequestFailed":
                    OnSessionRequestFailed.InvokeOnMainThread().HandleAnyException();
                    return IntPtr.Zero;

                case "onSessionActive":
                    CurrentState = NativeWrapperState.Opened;
                    
                    OnSessionActive.InvokeOnMainThread().HandleAnyException();
                    return IntPtr.Zero;

                case "onSessionClosed":
                    CurrentState = NativeWrapperState.Closed;

                    OnSessionClosed.InvokeOnMainThread().HandleAnyException();
                    return IntPtr.Zero;

                case "onFrameReady":
                    (IntPtr yBufferObj, IntPtr yBufferPtr) = JNIExtensions.UnboxAndCreateGlobalRefForByteBufferElement(javaArgs, 0);
                    (IntPtr uBufferObj, IntPtr uBufferPtr) = JNIExtensions.UnboxAndCreateGlobalRefForByteBufferElement(javaArgs, 1);
                    (IntPtr vBufferObj, IntPtr vBufferPtr) = JNIExtensions.UnboxAndCreateGlobalRefForByteBufferElement(javaArgs, 2);
                    int yRowStride = JNIExtensions.UnboxIntElement(javaArgs, 3);
                    int uvRowStride = JNIExtensions.UnboxIntElement(javaArgs, 4);
                    int uvPixelStride = JNIExtensions.UnboxIntElement(javaArgs, 5);
                    long timestampNs = JNIExtensions.UnboxLongElement(javaArgs, 6);

                    try
                    {
                        OnFrameReady?.Invoke(
                            yBufferPtr, AndroidJNI.GetDirectBufferCapacity(yBufferObj),
                            uBufferPtr,
                            vBufferPtr, AndroidJNI.GetDirectBufferCapacity(uBufferObj),
                            yRowStride, uvRowStride,
                            uvPixelStride, timestampNs);
                    }
                    finally
                    {
                        AndroidJNI.DeleteGlobalRef(yBufferObj);
                        AndroidJNI.DeleteGlobalRef(uBufferObj);
                        AndroidJNI.DeleteGlobalRef(vBufferObj);
                    }

                    return IntPtr.Zero;

                case "disposeCompleted":
                    OnDisposeCompleted?.Invoke();
                    return IntPtr.Zero;
            }

            return base.Invoke(methodName, javaArgs);
        }

        /// <summary>
        /// Waits until the CaptureSession is open or erred out.
        /// </summary>
        public WaitUntil WaitForInitialization()
        {
            ThrowIfDisposed();
            return new(() => CurrentState != NativeWrapperState.Initializing);
        }

        /// <summary>
        /// Waits until the CaptureSession opens or errs out.
        /// </summary>
        /// <inheritdoc cref="CameraDevice.WaitForInitialization(TimeSpan, Action, WaitTimeoutMode)"/>
        public WaitUntil WaitForInitialization(TimeSpan timeout, Action onTimeout, WaitTimeoutMode timeoutMode = WaitTimeoutMode.Realtime)
        {
            ThrowIfDisposed();
            return new(() => CurrentState != NativeWrapperState.Initializing, timeout, onTimeout, timeoutMode);
        }

        /// <summary>
        /// Waits until the CaptureSession opens or errs out.
        /// </summary>
        /// <returns><see langword="true"/> if the session was opened successfully, <see langword="false"/> otherwise.</returns>
        public async Task<bool> WaitForInitializationAsync(CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (CurrentState != NativeWrapperState.Initializing)
                return CurrentState == NativeWrapperState.Opened;

            TaskCompletionSource<bool> wrapperState = new();
            void OnOpened() => wrapperState.SetResult(true);
            void OnClosed() => wrapperState.SetResult(false);

            OnSessionActive += OnOpened;
            OnSessionClosed += OnClosed;

            try
            {
                using CancellationTokenRegistration _ = token.Register(wrapperState.SetCanceled);
                return await wrapperState.Task;
            }
            finally
            {
                OnSessionActive -= OnOpened;
                OnSessionClosed -= OnClosed;
            }
        }

        private bool _disposed = false;

        /// <summary>
        /// Closes and disposes the capture session.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_captureSession != null)
            {
                TaskCompletionSource<bool> tcs = new();
                void OnDisposed() => tcs.TrySetResult(true);

                OnDisposeCompleted += OnDisposed;
                if (!_captureSession.Call<bool>("close"))
                    tcs.TrySetResult(true);

                await tcs.Task;

                OnDisposeCompleted -= OnDisposed;
                _captureSession.Dispose();
                _captureSession = null;
            }

            GC.SuppressFinalize(this);
        }
        
        ~ContinuousCaptureSession()
        {
            Debug.LogWarning(
                $"A {nameof(ContinuousCaptureSession)} object was finalized by the garbage collector without being properly disposed.\n" +
                $"The native camera capture session was **not closed** and resources may still be held.\n\n" +
                $"To fix this, ensure that you explicitly call `{nameof(DisposeAsync)}` or wrap it in an `await using` block:\n" +
                $"    await using var session = cameraDevice.CreateContinuousCaptureSession(...);\n" +
                $"This ensures that the capture session is closed on the correct Unity thread."
            );
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ContinuousCaptureSession));
        }
    }
}