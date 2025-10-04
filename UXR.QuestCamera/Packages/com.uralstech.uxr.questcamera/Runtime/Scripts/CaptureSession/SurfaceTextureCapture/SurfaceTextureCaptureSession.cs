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
using UnityEngine;
using UnityEngine.Rendering;
using static Uralstech.UXR.QuestCamera.SurfaceTextureCapture.STCaptureSessionNative;

#if !UNITY_6000_0_OR_NEWER
using Utilities.Async;
#endif

#nullable enable
namespace Uralstech.UXR.QuestCamera.SurfaceTextureCapture
{
    public class SurfaceTextureCaptureSession : AndroidJavaProxy, IDisposable
    {
        /// <summary>
        /// The current assumed state of the native CaptureSession wrapper.
        /// </summary>
        public NativeWrapperState CurrentState { get; private set; }

        /// <summary>
        /// Is the native CaptureSession wrapper active and usable?
        /// </summary>
        public bool IsActiveAndUsable => _captureSession?.Get<bool>("isActiveAndUsable") ?? throw new ObjectDisposedException(nameof(SurfaceTextureCaptureSession));

        /// <summary>
        /// The texture being rendered to.
        /// </summary>
        public readonly Texture2D Texture;

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
        /// Called when the session could not be registered with the native shader helpers.
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
        /// Called when a frame is ready.
        /// </summary>
        public event Action<long>? OnFrameReady;

        protected readonly CommandBuffer _commandBuffer;

        /// <summary>
        /// The native capture session object.
        /// </summary>
        internal protected AndroidJavaObject? _captureSession;

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
                    OnSessionConfigured.InvokeOnMainThread();
                    return IntPtr.Zero;

                case "onSessionConfigurationFailed":
                    bool isAccessOrSecurityError = JNIExtensions.UnboxBoolElement(javaArgs, 0);
                    OnSessionConfigurationFailed.InvokeOnMainThread(isAccessOrSecurityError);
                    return IntPtr.Zero;

                case "onSessionRequestSet":
                    OnSessionRequestSet.InvokeOnMainThread();
                    return IntPtr.Zero;

                case "onSessionRequestFailed":
                    OnSessionRequestFailed.InvokeOnMainThread();
                    return IntPtr.Zero;

                case "onSessionRegistrationFailed":
                    OnSessionRegistrationFailed.InvokeOnMainThread();
                    return IntPtr.Zero;

                case "onSessionActive":
                    CurrentState = NativeWrapperState.Opened;

                    OnSessionActive.InvokeOnMainThread();
                    return IntPtr.Zero;

                case "onSessionClosed":
                    if (_nativeTextureId == null)
                    {
                        CurrentState = NativeWrapperState.Closed;
                        OnSessionClosed?.InvokeOnMainThread();
                        return IntPtr.Zero;
                    }

                    SendNativeUpdate(NativeEventId.CleanupNativeTexture, (_, __, ___) =>
                    {
                        CurrentState = NativeWrapperState.Closed;
                        OnSessionClosed?.InvokeOnMainThread();
                    });
                    return IntPtr.Zero;
                
                case "onCaptureCompleted":
                    long timestamp = JNIExtensions.UnboxLongElement(javaArgs, 0);
                    SendNativeUpdate(NativeEventId.RenderTextures, (_, result, timestamp) =>
                    {
                        if (result)
                        {
                            GL.InvalidateState();
                            OnFrameReady?.Invoke(timestamp);
                        }
                    }, timestamp);
                    return IntPtr.Zero;
            }

            return base.Invoke(methodName, javaArgs);
        }

        internal protected virtual void InitializeNative(long timestamp)
        {
            NativeSetupData data = new()
            {
                UnityTexture = (uint)Texture.GetNativeTexturePtr(),
                Width = Texture.width,
                Height = Texture.height,
                Timestamp = timestamp,
                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<NativeSetupCallbackType>(NativeSetupCallback),
            };

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            NativeSetupCallbacksQueue.Enqueue((glIsClean, sessionCallSent, textureId, idIsValid) =>
            {
                Marshal.FreeHGlobal(dataPtr);

                _nativeTextureId = idIsValid ? textureId : null;
                if (!glIsClean && idIsValid)
                {
                    SendNativeUpdate(NativeEventId.CleanupNativeTexture, (_, __, ___) =>
                    {
                        CurrentState = NativeWrapperState.Closed;
                        OnSessionClosed.InvokeOnMainThread();
                    });
                }
                else if (!sessionCallSent)
                {
                    CurrentState = NativeWrapperState.Closed;
                    OnSessionClosed.InvokeOnMainThread();
                }
            });

            _commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), (int)NativeEventId.SetupNativeTexture, dataPtr);
            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();
        }

        protected virtual async void SendNativeUpdate(NativeEventId eventId, NativeUpdateCallbackWithTimestampType? callback, long timestamp = 0)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#else
            await Awaiters.UnityMainThread;
#endif
            if (_disposed || _nativeTextureId is not uint textureId)
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
        public WaitUntil WaitForInitialization() => new(() => CurrentState != NativeWrapperState.Initializing);

#if UNITY_6000_0_OR_NEWER
        /// <summary>
        /// Waits until the CaptureSession is open or erred out.
        /// </summary>
        /// <remarks>
        /// Requires Unity 6.0 or higher.
        /// </remarks>
        /// <returns>The current state of the CaptureSession.</returns>
        public async Awaitable<NativeWrapperState> WaitForInitializationAsync(CancellationToken token = default)
        {
            await Awaitable.MainThreadAsync();
            while (CurrentState == NativeWrapperState.Initializing && !token.IsCancellationRequested)
                await Awaitable.NextFrameAsync(token);

            return CurrentState;
        }

        /// <summary>
        /// Closes the capture session.
        /// </summary>
        /// <returns><see langword="true"/> if the session was closed successfully, <see langword="false"/> if the operation was cancelled.</returns>
        public async Awaitable<bool> CloseAsync(CancellationToken token = default)
        {
            await Awaitable.MainThreadAsync();

            _captureSession?.Call("close");
            while (CurrentState != NativeWrapperState.Closed && !token.IsCancellationRequested)
                await Awaitable.NextFrameAsync(token);

            return CurrentState == NativeWrapperState.Closed;
        }
#endif

        private bool _disposed = false;

        /// <summary>
        /// Releases native plugin resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _captureSession?.Dispose();
            _captureSession = null;
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}