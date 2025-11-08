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
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using static Uralstech.UXR.QuestCamera.SurfaceTextureCapture.STCaptureSessionNative;

#if !UNITY_6000_0_OR_NEWER
using Utilities.Async;
#endif

#nullable enable
namespace Uralstech.UXR.QuestCamera.SurfaceTextureCapture
{
    /// <summary>
    /// This experimental capture session uses a native OpenGL texture to capture images for better performance.
    /// </summary>
    /// <remarks>
    /// Requires OpenGL ES 3.0 as the project's graphics API. Works with single and multi-threaded rendering.
    /// </remarks>
    public class SurfaceTextureCaptureSession : AndroidJavaProxy, IAsyncDisposable
    {
        /// <summary>
        /// The current assumed state of the native CaptureSession wrapper.
        /// </summary>
        public NativeWrapperState CurrentState { get; private set; }
        private readonly object _stateLock = new();

        /// <summary>
        /// The texture being rendered to.
        /// </summary>
        public readonly Texture2D Texture;

        /// <summary>
        /// The timestamp the last frame processed was captured at in nanoseconds.
        /// </summary>
        public long CaptureTimestamp { get; protected set; }

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
        /// Called when the session could not be registered with the native renderer.
        /// </summary>
        public event Action? OnSessionRegistrationFailed;

        /// <summary>
        /// Called when the session has started actively processing capture requests.
        /// </summary>
        public event Action? OnSessionActive;

        /// <summary>
        /// Called when the session is closed.
        /// </summary>
        public event Action? OnSessionClosed;

        /// <summary>
        /// Called when a frame is ready, with its capture timestamp in nanoseconds.
        /// </summary>
        /// <remarks>
        /// This callback may not be called from the main thread.
        /// </remarks>
        public event Action<Texture2D, long>? OnFrameReady;

        /// <summary>
        /// Called when the native Kotlin wrapper has been completely disposed.
        /// </summary>
        protected event Action? OnDisposeCompleted;

        /// <summary>
        /// CommandBuffer for invoking native renderer events.
        /// </summary>
        protected readonly CommandBuffer _commandBuffer;

        /// <summary>
        /// The native capture session object.
        /// </summary>
        internal protected AndroidJavaObject? _captureSession;

        /// <summary>
        /// The native texture which captures YUV 4:2:0 data.
        /// </summary>
        protected uint? _nativeTextureId;

        public SurfaceTextureCaptureSession(Resolution resolution) : base("com.uralstech.ucamera.STCaptureSessionWrapper$Callbacks")
        {
            Texture = new Texture2D(resolution.width, resolution.height, TextureFormat.ARGB32, false);
            _commandBuffer = new CommandBuffer();
        }

        /// <inheritdoc/>
        public override IntPtr Invoke(string methodName, IntPtr javaArgs)
        {
            switch (methodName)
            {
                case "onSessionConfigured":
                    OnSessionConfigured.InvokeOnMainThread().HandleAnyException();
                    return IntPtr.Zero;

                case "onSessionConfigurationFailed":
                    if (JNIExtensions.UnboxBoolElement(javaArgs, 0))
                    {
                        OnSessionConfigurationFailed.InvokeOnMainThread(true).HandleAnyException();
                        return IntPtr.Zero;
                    }

                    if (_nativeTextureId == null)
                    {
                        SetCurrentState(NativeWrapperState.Closed);
                        OnSessionConfigurationFailed.InvokeOnMainThread(false).HandleAnyException();
                        return IntPtr.Zero;
                    }
                    
                    SendNativeUpdate(NativeEventId.CleanupNativeTexture, (textureId, _, _) =>
                    {
                        DeregisterNativeUpdateCallbacks(textureId);
                        SetCurrentState(NativeWrapperState.Closed);
                        OnSessionConfigurationFailed.InvokeOnMainThread(false).HandleAnyException();
                    }).HandleAnyException();
                    return IntPtr.Zero;

                case "onSessionRequestSet":
                    OnSessionRequestSet.InvokeOnMainThread().HandleAnyException();
                    return IntPtr.Zero;

                case "onSessionRequestFailed":
                    OnSessionRequestFailed.InvokeOnMainThread().HandleAnyException();
                    return IntPtr.Zero;

                case "onSessionRegistrationFailed":
                    OnSessionRegistrationFailed.InvokeOnMainThread().HandleAnyException();
                    return IntPtr.Zero;

                case "onSessionActive":
                    SetCurrentState(NativeWrapperState.Opened);

                    OnSessionActive.InvokeOnMainThread().HandleAnyException();
                    return IntPtr.Zero;

                case "onSessionClosed":
                    if (_nativeTextureId == null)
                    {
                        SetCurrentState(NativeWrapperState.Closed);
                        OnSessionClosed?.InvokeOnMainThread().HandleAnyException();
                        return IntPtr.Zero;
                    }

                    SendNativeUpdate(NativeEventId.CleanupNativeTexture, (textureId, _, _) =>
                    {
                        DeregisterNativeUpdateCallbacks(textureId);
                        SetCurrentState(NativeWrapperState.Closed);
                        OnSessionClosed?.InvokeOnMainThread().HandleAnyException();
                    }).HandleAnyException();
                    return IntPtr.Zero;

                case "onCaptureCompleted":
                    long timestamp = JNIExtensions.UnboxLongElement(javaArgs, 0);
                    SendNativeUpdate(NativeEventId.RenderTextures, (_, result, timestamp) =>
                    {
                        if (result)
                        {
                            GL.InvalidateState();
                            CaptureTimestamp = timestamp;
                            OnFrameReady?.Invoke(Texture, timestamp);
                        }
                    }, timestamp).HandleAnyException();
                    return IntPtr.Zero;

                case "disposeCompleted":
                    OnDisposeCompleted?.Invoke();
                    return IntPtr.Zero;
            }

            return base.Invoke(methodName, javaArgs);
        }

        /// <summary>Invokes <see cref="OnFrameReady"/> for child classes.</summary>
        protected void OnFrameReadyInvk(Texture2D texture, long timestamp) => OnFrameReady?.Invoke(texture, timestamp);

        /// <summary>
        /// Initializes the native renderer.
        /// </summary>
        /// <param name="timestamp">The timestamp corresponding to the native capture session wrapper.</param>
        internal protected virtual void InitializeNative(long timestamp)
        {
            uint unityTextureId = (uint)Texture.GetNativeTexturePtr();
            NativeSetupData data = new()
            {
                UnityTexture = unityTextureId,
                Width = Texture.width,
                Height = Texture.height,
                Timestamp = timestamp,
                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<NativeSetupCallbackType>(NativeSetupCallback),
            };

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            NativeSetupCallbacksQueue.TryAdd(unityTextureId, (glIsClean, sessionCallSent, __, textureId, idIsValid) =>
            {
                Marshal.FreeHGlobal(dataPtr);
                GL.InvalidateState();

                _nativeTextureId = idIsValid ? textureId : null;
                if (!glIsClean && idIsValid)
                {
                    SendNativeUpdate(NativeEventId.CleanupNativeTexture, (textureId, _, _) =>
                    {
                        DeregisterNativeUpdateCallbacks(textureId);
                        SetCurrentState(NativeWrapperState.Closed);
                        OnSessionClosed.InvokeOnMainThread().HandleAnyException();
                    }).HandleAnyException();
                }
                else if (!sessionCallSent)
                {
                    SetCurrentState(NativeWrapperState.Closed);
                    OnSessionClosed.InvokeOnMainThread().HandleAnyException();
                }
            });

            _commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), (int)NativeEventId.SetupNativeTexture, dataPtr);
            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();
        }

        /// <summary>
        /// Sends an update event to the native renderer.
        /// </summary>
        /// <param name="eventId">The type of the event.</param>
        /// <param name="callback">An optional callback for the event's completion.</param>
        /// <param name="timestamp">An optional timestamp to be tracked in C# code, to be forwarded to <paramref name="timestamp"/>.</param>
        protected virtual async Task SendNativeUpdate(NativeEventId eventId, NativeUpdateCallbackWithTimestampType? callback, long timestamp = 0)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#else
            await Awaiters.UnityMainThread;
#endif
            if ((Disposed && eventId != NativeEventId.CleanupNativeTexture) || _nativeTextureId is not uint textureId)
                return;

            if (eventId == NativeEventId.CleanupNativeTexture)
                _nativeTextureId = null;

            NativeUpdateData data = new()
            {
                NativeTexture = textureId,
                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<NativeUpdateCallbackType>(NativeUpdateCallback),
            };

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            if (!NativeUpdateCallbacksQueue.ContainsKey(textureId))
                NativeUpdateCallbacksQueue.TryAdd(textureId, new ConcurrentQueue<AdditionalUpdateCallbackData>());

            NativeUpdateCallbacksQueue[textureId].Enqueue(new AdditionalUpdateCallbackData(callback, dataPtr, timestamp));

            _commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), (int)eventId, dataPtr);
            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();
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
        /// <returns><see langword="true"/> if the session was opened successfully, <see langword="false"/> otherwise.</returns>
        public async Task<bool> WaitForInitializationAsync(CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (CurrentState != NativeWrapperState.Initializing)
                return CurrentState == NativeWrapperState.Opened;

            TaskCompletionSource<bool> wrapperState = new();
            void OnEvent() => wrapperState.SetResult(CurrentState == NativeWrapperState.Opened);
            void OnConfigError(bool isAccessOrSecurityError)
            {
                if (!isAccessOrSecurityError)
                    wrapperState.SetResult(false);
            }

            OnSessionActive += OnEvent;
            OnSessionClosed += OnEvent;
            OnSessionConfigurationFailed += OnConfigError;

            try
            {
                using CancellationTokenRegistration _ = token.Register(() => wrapperState.SetCanceled());
                return await wrapperState.Task;
            }
            finally
            {
                OnSessionActive -= OnEvent;
                OnSessionClosed -= OnEvent;
                OnSessionConfigurationFailed -= OnConfigError;
            }
        }

        /// <summary>
        /// Sets <see cref="CurrentState"/> with a lock.
        /// </summary>
        private void SetCurrentState(NativeWrapperState state)
        {
            lock (_stateLock)
            {
                CurrentState = state;
            }
        }

        protected bool Disposed { get; private set; } = false;

        /// <summary>
        /// Closes and releases the capture session..
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Disposed)
                return;

            Disposed = true;

            if (_captureSession != null)
            {
                TaskCompletionSource<bool> disposeTCS = new();
                TaskCompletionSource<bool> cleanupTCS = new();
                void OnDisposed() => disposeTCS.TrySetResult(true);
                void OnCleanup() => cleanupTCS.TrySetResult(true);

                OnSessionClosed += OnCleanup;
                OnDisposeCompleted += OnDisposed;

                if (!_captureSession.Call<bool>("close"))
                    disposeTCS.TrySetResult(true);
                if (CurrentState == NativeWrapperState.Closed)
                    cleanupTCS.TrySetResult(true);

                await Task.WhenAll(disposeTCS.Task, cleanupTCS.Task);

                OnDisposeCompleted -= OnDisposed;
                OnSessionClosed -= OnCleanup;

                _captureSession.Dispose();
                _captureSession = null;
            }

            GC.SuppressFinalize(this);
        }
        
        ~SurfaceTextureCaptureSession()
        {
            Debug.LogWarning(
                $"A {nameof(SurfaceTextureCaptureSession)} object was finalized by the garbage collector without being properly disposed.\n" +
                $"The native camera capture session was **not closed**, renderer data was **not cleaned up** and resources may still be held.\n\n" +
                $"To fix this, ensure that you explicitly call `{nameof(DisposeAsync)}` or wrap it in an `await using` block:\n" +
                $"    await using var session = cameraDevice.CreateSurfaceTextureCaptureSession(...);\n" +
                $"This ensures that the capture session is closed on the correct Unity thread."
            );
        }

        private void ThrowIfDisposed()
        {
            if (Disposed)
                throw new ObjectDisposedException(nameof(SurfaceTextureCaptureSession));
        }
    }
}