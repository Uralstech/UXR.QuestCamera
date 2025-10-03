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

using AOT;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// This is an experimental capture session type that uses a native OpenGL texture to capture images for better performance.
    /// </summary>
    /// <remarks>
    /// The results of this capture session may be more noisy compared to <see cref="ContinuousCaptureSession"/>.
    /// Requires OpenGL ES 3.0 or higher as the project's Graphics API. Works with single and multi-threaded rendering.
    /// </remarks>
    public class SurfaceTextureCaptureSession : AndroidJavaProxy, IDisposable
    {
        #region Native Event Handling
        /// <summary>Native event to create the native texture.</summary>
        protected const int CreateGlTextureEvent = 1;
        
        /// <summary>Native event to destroy the native texture.</summary>
        protected const int DestroyGlTextureEvent = 2;
        
        /// <summary>Native event to update the native texture and convert it to the Unity texture.</summary>
        protected const int UpdateSurfaceTextureEvent = 3;
        
        /// <summary>
        /// Gets the pointer to the native rendering function.
        /// </summary>
        [DllImport("NativeTextureHelper")]
        protected static extern IntPtr GetRenderEventFunction();
        
        /// <summary>
        /// Data structure to setup the native texture.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        protected struct TextureSetupData
        {
            /// <summary>The Unity texture that the native texture will convert to.</summary>
            public uint UnityTextureId;
        
            /// <summary>The width of the texture.</summary>
            public int Width;
        
            /// <summary>The height of the texture.</summary>
            public int Height;
        
            /// <summary>The time when this capture session was created.</summary>
            public long TimeStamp;
        
            /// <summary>The callback to call when the setup is done.</summary>
            public IntPtr OnDoneCallback;
        }
        
        /// <summary>
        /// Data structure to update the native and Unity textures.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        protected struct TextureUpdateData
        {
            /// <summary>The ID of the native texture.</summary>
            public int CameraTextureId;
        
            /// <summary>The callback to call when the update is done.</summary>
            public IntPtr OnDoneCallback;
        }
        
        /// <summary>
        /// Data structure to delete the native texture.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        protected struct TextureDeletionData
        {
            /// <summary>The ID of the native texture.</summary>
            public uint TextureId;
        
            /// <summary>The callback to call when the deletion is done.</summary>
            public IntPtr OnDoneCallback;
        }

        /// <summary>Queue for native event callbacks.</summary>
        protected static readonly ConcurrentQueue<Action> s_nativeTextureCallbacks = new();
        
        /// <summary>
        /// Callback for the native texture events. It will dequeue from <see cref="s_nativeTextureCallbacks"/> and call it.
        /// </summary>
        [MonoPInvokeCallback(typeof(Action))]
        protected static void NativeTextureCallback()
        {
            if (s_nativeTextureCallbacks.TryDequeue(out Action action))
                action?.Invoke();
        }
        #endregion

        /// <summary>
        /// The current assumed state of the native CaptureSession wrapper.
        /// </summary>
        public NativeWrapperState CurrentState { get; private set; }

        /// <summary>
        /// Is the native CaptureSession wrapper active and usable?
        /// </summary>
        public bool IsActiveAndUsable => _captureSession?.Get<bool>("isActiveAndUsable") ?? throw new ObjectDisposedException(nameof(SurfaceTextureCaptureSession));

        /// <summary>
        /// The resolution of this capture session.
        /// </summary>
        public Resolution Resolution { get; private set; }
        
        /// <summary>
        /// The texture that will be updated with the camera feed.
        /// </summary>
        public Texture2D Texture { get; private set; }

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

        public event Action? OnFrameRendered;
        
        /// <summary>
        /// The command buffer to issue native events.
        /// </summary>
        protected CommandBuffer _commandBuffer;

        /// <summary>
        /// The native capture session object.
        /// </summary>
        internal protected AndroidJavaObject? _captureSession;

        public SurfaceTextureCaptureSession(Resolution resolution) : base("com.uralstech.ucamera.SurfaceTextureCaptureSession$Callbacks")
        {
            _commandBuffer = new CommandBuffer();
            Texture = new Texture2D(resolution.width, resolution.height, TextureFormat.ARGB32, false);
            Resolution = resolution;
        }

        /// <summary>
        /// Sets up the OpenGL texture.
        /// </summary>
        /// <param name="timeStamp">The time at which this capture session was created.</param>
        internal protected virtual void InitializeNativeTexture(long timestamp)
        {
            TextureSetupData data = new()
            {
                UnityTextureId = (uint)Texture.GetNativeTexturePtr(),
                Width = Resolution.width,
                Height = Resolution.height,
                TimeStamp = timestamp,
                OnDoneCallback = Marshal.GetFunctionPointerForDelegate<Action>(NativeTextureCallback)
            };

            CallNativeEvent(data, CreateGlTextureEvent);
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
                    CurrentState = NativeWrapperState.Closed;

                    bool isAccessOrSecurityError = JNIExtensions.UnboxBoolElement(javaArgs, 0);
                    OnSessionConfigurationFailed.InvokeOnMainThread(isAccessOrSecurityError);
                    return IntPtr.Zero;

                case "onSessionRequestSet":
                    OnSessionRequestSet.InvokeOnMainThread();
                    return IntPtr.Zero;

                case "onSessionRequestFailed":
                    CurrentState = NativeWrapperState.Closed;

                    OnSessionRequestFailed.InvokeOnMainThread();
                    return IntPtr.Zero;

                case "onSessionActive":
                    CurrentState = NativeWrapperState.Opened;

                    OnSessionActive.InvokeOnMainThread();
                    return IntPtr.Zero;

                case "onCaptureCompleted":
                    TextureUpdateData updateData = new()
                    {
                        CameraTextureId = JNIExtensions.UnboxIntElement(javaArgs, 0),
                        OnDoneCallback = Marshal.GetFunctionPointerForDelegate<Action>(NativeTextureCallback)
                    };

                    CallNativeEvent(updateData, UpdateSurfaceTextureEvent, GL.InvalidateState);
                    return IntPtr.Zero;

                case "destroyNativeTexture":
                    TextureDeletionData deletionData = new()
                    {
                        TextureId = (uint)JNIExtensions.UnboxIntElement(javaArgs, 0),
                        OnDoneCallback = Marshal.GetFunctionPointerForDelegate<Action>(NativeTextureCallback)
                    };

                    CallNativeEvent(deletionData, DestroyGlTextureEvent, Dispose);
                    return IntPtr.Zero;
            }

            return base.Invoke(methodName, javaArgs);
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
            if (CurrentState != NativeWrapperState.Initializing)
                return CurrentState;

            await Awaitable.MainThreadAsync();
            while (CurrentState == NativeWrapperState.Initializing && !token.IsCancellationRequested)
                await Awaitable.NextFrameAsync(token);

            return CurrentState;
        }
#endif

        /// <summary>
        /// Sends an event to the native plugin.
        /// </summary>
        /// <typeparam name="T">The type of the data to send.</typeparam>
        /// <param name="data">The data to send.</param>
        /// <param name="eventId">The unique ID of the event.</param>
        /// <param name="additionalAction">Any additional action to be done after the event is completed.</param>
        protected virtual async void CallNativeEvent<T>(T data, int eventId, Action? additionalAction = null)
            where T : struct
        {
            if (_disposed)
                return;

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, dataPtr, false);

            s_nativeTextureCallbacks.Enqueue(() =>
            {
                Debug.Log($"Native plugin event of ID \"{eventId}\" completed.");
                Marshal.FreeHGlobal(dataPtr);
                additionalAction?.Invoke();
                OnFrameRendered?.InvokeOnMainThread();
            });

            await Awaitable.MainThreadAsync();
            if (_disposed)
            {
                Marshal.FreeHGlobal(dataPtr);
                return;
            }

            _commandBuffer.IssuePluginEventAndData(GetRenderEventFunction(), eventId, dataPtr);
            Graphics.ExecuteCommandBuffer(_commandBuffer);
            _commandBuffer.Clear();
        }

        private bool _disposed = false;

        /// <summary>
        /// Releases native plugin resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _captureSession?.Call("close");
            _captureSession?.Dispose();
            _captureSession = null;

            _commandBuffer.Dispose();
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}