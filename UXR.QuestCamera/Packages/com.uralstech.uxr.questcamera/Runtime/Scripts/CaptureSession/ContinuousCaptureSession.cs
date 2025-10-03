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
    public class ContinuousCaptureSession : AndroidJavaProxy, IDisposable
    {
        /// <summary>
        /// The current assumed state of the native CaptureSession wrapper.
        /// </summary>
        public NativeWrapperState CurrentState { get; private set; }

        /// <summary>
        /// Is the native CaptureSession wrapper active and usable?
        /// </summary>
        public bool IsActiveAndUsable => _captureSession?.Get<bool>("isActiveAndUsable") ?? throw new ObjectDisposedException(nameof(ContinuousCaptureSession));

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
        /// Callback for processing the YUV 4:2:0 frame.
        /// </summary>
        /// <remarks>
        /// <list type="table">
        ///     <listheader>
        ///         <term>Parameters</term>
        ///     </listheader>
        ///     <item>
        ///         <term>yBuffer (IntPtr)</term>
        ///         <description>Pointer to the buffer containing Y (luminance) data of the frame.</description>
        ///     </item>
        ///     <item>
        ///         <term>uBuffer (IntPtr)</term>
        ///         <description>Pointer to the buffer containing U (color) data of the frame.</description>
        ///     </item>
        ///     <item>
        ///         <term>vBuffer (IntPtr)</term>
        ///         <description>Pointer to the buffer containing V (color) data of the frame.</description>
        ///     </item>
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
        public event Action<IntPtr, IntPtr, IntPtr, int, int, int, long>? OnFrameReady;

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
                            yBufferPtr, uBufferPtr, vBufferPtr,
                            yRowStride, uvRowStride,
                            uvPixelStride, timestampNs);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                    finally
                    {
                        AndroidJNI.DeleteGlobalRef(yBufferObj);
                        AndroidJNI.DeleteGlobalRef(uBufferObj);
                        AndroidJNI.DeleteGlobalRef(vBufferObj);
                    }

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
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}