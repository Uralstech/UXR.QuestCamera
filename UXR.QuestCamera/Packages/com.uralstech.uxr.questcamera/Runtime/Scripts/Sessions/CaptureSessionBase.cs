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

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>Base class for all capture session types.</summary>
    /// <typeparam name="TProxy">The proxy type.</typeparam>
    public abstract class CaptureSessionBase<TProxy> : StatefulResource, IAsyncDisposable
        where TProxy : CaptureSessionBase<TProxy>.ProxyBase
    {
        /// <summary>Error codes from the native plugin.</summary>
        public enum ErrorCode
        {
            /// <summary>In case the session configuration is invalid, if the captureTemplate is not supported by this device, or other illegal arguments were passed.</summary>
            IllegalArgumentError    = 1000,

            /// <summary>In case the camera device is no longer connected or has encountered a fatal error.</summary>
            CameraAccessError       = 1001,

            /// <summary>In case the camera device or session has been closed.</summary>
            IllegalStateError       = 1003,

            /// <summary>If the surface or surfaceTexture for the session could not be created due to an out-of-resources error.</summary>
            OutOfResourcesError     = 1004,

            /// <summary>The session could not be configured.</summary>
            ConfigurationFailed     = 2000,

            /// <summary>The Kotlin session could not bind to its C++ job.</summary>
            NativeJobBindingFailed  = 3000,
        }

        /// <summary>
        /// A callback for modification of all <a href="https://developer.android.com/reference/android/hardware/camera2/CaptureRequest.Builder">CaptureRequest builders</a>
        /// created for the session.
        /// </summary>
        /// <param name="builder">The builder object. DO NOT dispose this, or persist a reference to it beyond the callback.</param>
        /// <param name="isRepeatingRequest">If this builder is for a repeating or on-demand capture request.</param>
        public delegate void ModifyRequestBuilderCallback(CaptureRequest.Builder builder, bool isRepeatingRequest);

        /// <summary>A callback for configuring if capture-specific events should be registered for a capture request. Defaults to <see langword="false"/>.</summary>
        /// <remarks>Please only register one listener to this event for each session.</remarks>
        /// <param name="request">The request for which events should be registered.</param>
        /// <param name="isRepeatingRequest">If this is for a repeating or on-demand capture request.</param>
        public delegate bool ShouldRegisterCaptureEventsCallback(CaptureRequest request, bool isRepeatingRequest);

        /// <summary>Java proxy to handle native callbacks.</summary>
        /// <remarks>All event callbacks will be on a Java thread, and are performance sensitive.</remarks>
        public abstract class ProxyBase : AndroidJavaProxy
        {
            private const string ClassName = "com.uralstech.uxr.questcamera.CaptureSessionManagerBase$CallbacksBase";

            /// <remarks>You should generally avoid configuring the request if <c>isRepeatingRequest</c> is <see langword="true"/> for on-demand sessions.</remarks>
            /// <inheritdoc cref="ModifyRequestBuilderCallback"/>
            public event ModifyRequestBuilderCallback? ModifyRequestBuilder;

            /// <inheritdoc cref="ShouldRegisterCaptureEventsCallback"/>
            public event ShouldRegisterCaptureEventsCallback? ShouldRegisterCaptureEvents;

            /// <inheritdoc cref="OnSessionConfigured"/>
            public event Action? OnConfigured;

            /// <inheritdoc cref="OnSessionConfigurationFailed"/>
            public event Action<ErrorCode>? OnConfigureFailed;

            /// <inheritdoc cref="OnSessionRequestSet"/>
            public event Action? OnRequestSet;

            /// <summary>Same as <see cref="OnRequestSet"/>, but includes the sequence ID of the capture request.</summary>
            public event Action<int>? OnRequestSetWithId;

            /// <inheritdoc cref="OnSessionRequestFailed"/>
            public event Action<ErrorCode>? OnRequestFailed;

            /// <summary>Called when an image capture has fully completed and all the result metadata is available. </summary>
            /// <remarks>Override <see cref="ShouldRegisterCaptureEvents"/> for capture requests that should invoke this event. DO NOT persist references beyond the callback.</remarks>
            public event Action<CaptureRequest, TotalCaptureResult>? OnCaptureCompleted;

            /// <summary>Called instead of <see cref="OnCaptureCompleted"/> when the camera device failed to produce a CaptureResult for the request.</summary>
            /// <inheritdoc cref="OnCaptureCompleted"/>
            public event Action<CaptureRequest, CaptureFailure>? OnCaptureFailed;

            /// <summary>Called when a capture sequence finishes and all CaptureResult or CaptureFailure for it have been returned via this listener.</summary>
            /// <inheritdoc cref="OnCaptureCompleted"/>
            public event Action<int, long>? OnCaptureSequenceCompleted;

            /// <summary>Called when a capture sequence aborts before any CaptureResult or CaptureFailure for it have been returned via this listener.</summary>
            /// <inheritdoc cref="OnCaptureCompleted"/>
            public event Action<int>? OnCaptureSequenceAborted;

            /// <inheritdoc cref="OnSessionClosed"/>
            public event Action? OnClosed;

            public ProxyBase() : base(ClassName) { }

            protected ProxyBase(string className) : base(className) { }

            /// <inheritdoc/>
            /// <exclude />
            public override IntPtr Invoke(string methodName, IntPtr javaArgs)
            {
                bool isRepeatingRequest;
                int errorCode, sequenceId;
                AndroidJavaObject nativeRequest;

                switch (methodName)
                {
                    case "modifyRequestBuilder":
                        if (ModifyRequestBuilder is not ModifyRequestBuilderCallback modifyBuilderCallback)
                            break;

                        AndroidJavaObject nativeBuilder = JNIExtensions.UnboxObjectElement(javaArgs, 0);
                        isRepeatingRequest = JNIExtensions.UnboxBoolElement(javaArgs, 1);

                        using (CaptureRequest.Builder builder = new(nativeBuilder))
                            modifyBuilderCallback.Invoke(builder, isRepeatingRequest);
                        break;

                    case "shouldRegisterCaptureEvents":
                        if (ShouldRegisterCaptureEvents is not ShouldRegisterCaptureEventsCallback shouldRegisterEvents)
                            return AndroidJNIHelper.Box(false);

                        nativeRequest = JNIExtensions.UnboxObjectElement(javaArgs, 0);
                        isRepeatingRequest = JNIExtensions.UnboxBoolElement(javaArgs, 1);

                        using (CaptureRequest request = new(nativeRequest))
                        {
                            try
                            {
                                bool result = shouldRegisterEvents.Invoke(request, isRepeatingRequest);
                                return AndroidJNIHelper.Box(result);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogException(ex);
                                return AndroidJNIHelper.Box(false);
                            }
                        }

                    case "onConfigured":
                        OnConfigured?.Invoke(); break;

                    case "onConfigureFailed":
                        errorCode = JNIExtensions.UnboxIntElement(javaArgs, 0);
                        OnConfigureFailed?.Invoke((ErrorCode)errorCode); break;
                    
                    case "onRequestSet":
                        OnRequestSet?.Invoke();
                        OnRequestSetWithId?.Invoke(JNIExtensions.UnboxIntElement(javaArgs, 0));
                        break;

                    case "onRequestFailed":
                        errorCode = JNIExtensions.UnboxIntElement(javaArgs, 0);
                        OnRequestFailed?.Invoke((ErrorCode)errorCode); break;

                    case "onCaptureCompleted":
                        nativeRequest = JNIExtensions.UnboxObjectElement(javaArgs, 0);
                        AndroidJavaObject nativeCaptureResult = JNIExtensions.UnboxObjectElement(javaArgs, 1);

                        using (CaptureRequest request = new(nativeRequest))
                        using (TotalCaptureResult result = new(nativeCaptureResult))
                            OnCaptureCompleted?.Invoke(request, result);
                        break;

                    case "onCaptureFailed":
                        nativeRequest = JNIExtensions.UnboxObjectElement(javaArgs, 0);
                        AndroidJavaObject nativeCaptureFailure = JNIExtensions.UnboxObjectElement(javaArgs, 1);

                        using (CaptureRequest request = new(nativeRequest))
                        using (CaptureFailure failure = new(nativeCaptureFailure))
                            OnCaptureFailed?.Invoke(request, failure);
                        break;

                    case "onCaptureSequenceCompleted":
                        sequenceId = JNIExtensions.UnboxIntElement(javaArgs, 0);
                        long frameNumber = JNIExtensions.UnboxLongElement(javaArgs, 1);

                        OnCaptureSequenceCompleted?.Invoke(sequenceId, frameNumber);
                        break;

                    case "onCaptureSequenceAborted":
                        sequenceId = JNIExtensions.UnboxIntElement(javaArgs, 0);
                        OnCaptureSequenceAborted?.Invoke(sequenceId); break;

                    case "onClosed":
                        OnClosed?.Invoke(); break;

                    default:
                        return base.Invoke(methodName, javaArgs);
                }

                return IntPtr.Zero;
            }
        }

        /// <summary>Called when the session has been configured.</summary>
        public event Action? OnSessionConfigured;

        /// <summary>Called when the session could not be configured.</summary>
        public event Action<ErrorCode>? OnSessionConfigurationFailed;

        /// <summary>Called when the session request has been set.</summary>
        public event Action? OnSessionRequestSet;

        /// <summary>Same as <see cref="OnSessionRequestSet"/>, but includes the sequence ID of the capture request.</summary>
        public event Action<int>? OnSessionRequestSetWithId;

        /// <summary>Called when the session request could not be set.</summary>
        public event Action<ErrorCode>? OnSessionRequestFailed;

        /// <summary>Called when the session is closed.</summary>
        public event Action? OnSessionClosed;
        
        /// <summary>Native callback handler.</summary>
        public readonly TProxy NativeProxy;

        internal protected readonly AndroidJavaObject _native;
        protected bool _disposed;

        protected CaptureSessionBase(TProxy proxy, AndroidJavaObject native)
        {
            NativeProxy = proxy;
            _native = native;

            NativeProxy.OnConfigured         += OnConfiguredNative;
            NativeProxy.OnConfigureFailed    += OnConfigureFailedNative;
            NativeProxy.OnRequestSetWithId   += OnRequestSetNative;
            NativeProxy.OnRequestFailed      += OnRequestFailedNative;
            NativeProxy.OnClosed             += OnClosedNative;
        }

        /// <summary>Discard all captures currently pending and in-progress as fast as possible.</summary>
        /// <remarks>This makes the session unusable for the future, so call <see cref="DisposeAsync()"/> afterwards.</remarks>
        /// <param name="errorCode">Error code if the operation was unsuccessful.</param>
        /// <returns><see langword="true"/> if successful; <see langword="false"/> otherwise.</returns>
        /// <exception cref="ObjectDisposedException"/>
        public bool TryAbortCaptures(out ErrorCode errorCode)
        {
            ThrowIfDisposed();
            
            int result = _native.Call<int>("abortCaptures");
            errorCode = (ErrorCode)result;
            return result == 0;
        }
        
        /// <summary>Closes the session (if not already closed) and releases native resources.</summary>
        public virtual async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;
            State = ResourceState.Invalid;

            await CloseWork();
            GC.SuppressFinalize(this);
        }

        protected async ValueTask CloseWork()
        {
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
                NativeProxy.OnClosed -= OnClosed;
                
                // Final deregistration
                NativeProxy.OnConfigured        -= OnConfiguredNative;
                NativeProxy.OnConfigureFailed   -= OnConfigureFailedNative;
                NativeProxy.OnRequestSetWithId  -= OnRequestSetNative;
                NativeProxy.OnRequestFailed     -= OnRequestFailedNative;
                NativeProxy.OnClosed            -= OnClosedNative;

                _native.Dispose();
            }
        }

        #region Callbacks

        private void OnConfiguredNative() =>
            OnSessionConfigured?.OnMainThread().Forget();

        private void OnConfigureFailedNative(ErrorCode errorCode)
        {
            State = ResourceState.Invalid;
            OnSessionConfigurationFailed?.OnMainThread(errorCode).Forget();
        }

        private void OnRequestSetNative(int sequenceId)
        {
            State = ResourceState.Valid;
            OnSessionRequestSet?.OnMainThread().Forget();
            OnSessionRequestSetWithId?.OnMainThread(sequenceId).Forget();
        }

        private void OnRequestFailedNative(ErrorCode errorCode)
        {
            State = ResourceState.Invalid;
            OnSessionRequestFailed?.OnMainThread(errorCode).Forget();
        }

        private void OnClosedNative()
        {
            State = ResourceState.Invalid;
            OnSessionClosed?.OnMainThread().Forget();
        }

        #endregion

        protected override void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        ~CaptureSessionBase()
        {
            Debug.LogWarning(
                $"A capture session object was finalized by the garbage collector without being properly disposed.\n" +
                $"The native camera capture session **may not be closed** and resources may still be held.\n\n" +
                $"To fix this, ensure that you explicitly call `{nameof(DisposeAsync)}` or wrap it in an `await using` block:\n" +
                $"    await using var session = cameraDevice.CreateContinuousSession(...);\n" +
                $"This ensures that the capture session is closed on the correct Unity thread."
            );
        }
    }
}