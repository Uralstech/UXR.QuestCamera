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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

#nullable enable
namespace Uralstech.UXR.QuestCamera.GLES
{
    /// <summary>Manages a camera capture session with a repeating request, but supports both repeating and on-demand conversion.</summary>
    /// <remarks>Texture conversion is done in native OpenGL-ES.</remarks>
    public sealed class GLESCaptureSession : CaptureSessionBase<GLESCaptureSession.Proxy>
    {
        /// <inheritdoc/>
        public sealed class Proxy : ProxyBase { }

        private const string ClassName = "com.uralstech.uxr.questcamera.GLESCaptureSessionManager";

        private static readonly int s_largestDataStructSize = Marshal.SizeOf<RenderJobSetupData>();

        /// <summary>Callback for when a frame has been processed, with the frame texture and capture timestamp.</summary>
        public event Action<Texture2D, long>? OnFrameProcessed;

        /// <summary><see langword="true"/> if a capture was processed this frame; <see langword="false"/> otherwise.</summary>
        public bool HasNewFrame => _lastUpdateFrame == Time.frameCount;

        /// <summary>The output texture with converted frames.</summary>
        public readonly Texture2D Texture;

        /// <summary>The capture timestamp of the last processed frame.</summary>
        public long CaptureTimestamp { get; private set; }

        private readonly uint _textureId;
        private int _lastUpdateFrame;

        private readonly CancellationTokenSource _runsCancellation = new();
        private readonly SemaphoreSlim _eventsSemaphore = new(1, 1);

        private readonly CommandBuffer _eventsCommandBuffer;
        private readonly IntPtr _eventsDataPtr;

        private bool _isJobDisposed;
        private Task? _runsLoop;

        private static Proxy MakeProxy(out Proxy proxy) => proxy = new Proxy();

        private static int MakeTexture(Resolution resolution, GraphicsFormat textureFormat, out Texture2D texture, out uint textureId)
        {
            if (textureFormat == GraphicsFormat.None)
                textureFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);

            if (!GraphicsUtils.IsGraphicsFormatSupportedForRender(textureFormat))
                throw new ArgumentException($"Format {textureFormat} is not supported on device.", nameof(textureFormat));

            texture = new Texture2D(resolution.width, resolution.height, textureFormat, TextureCreationFlags.DontUploadUponCreate | TextureCreationFlags.DontInitializePixels);
            return (int)(textureId = (uint)texture.GetNativeTexturePtr());
        }

        /// <param name="textureFormat">If not specified, uses equivalent of <see cref="RenderTextureFormat.ARGB32"/>.</param>
        public GLESCaptureSession(Resolution resolution, GraphicsFormat textureFormat = GraphicsFormat.None)
            : base(MakeProxy(out Proxy proxy), new(ClassName, MakeTexture(resolution, textureFormat, out Texture2D texture, out uint textureId), proxy))
        {
            Texture = texture;
            _textureId = textureId;
            
            _eventsCommandBuffer = new CommandBuffer();
            _eventsDataPtr = Marshal.AllocHGlobal(s_largestDataStructSize);

            OnFrameProcessed += LastUpdateFrameCallback;
        }

        /// <summary>Registers the texture and creates a job in the native C++ manager.</summary>
        /// <returns>The ID of the source texture created for the job, which the capture session will render to, or 0 if the operation failed.</returns>
        public async ValueTask<uint> SetupJobAsync()
        {
            ThrowIfDisposed();
            await _eventsSemaphore.WaitAsync();

            try
            {
                TaskCompletionSource<uint> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnComplete(uint textureId, uint _) => tcs.SetResult(textureId);

                GLESAPI.SetupCallbacksRegistry[_textureId] = OnComplete;

                RenderJobSetupData data = new(_textureId, Texture.width, Texture.height, GLESAPI.RenderJobSetupCallbackPtr);
                Marshal.StructureToPtr(data, _eventsDataPtr, false);

                _eventsCommandBuffer.IssuePluginEventAndData(GLESAPI.getGLESManageConverterJobEvent(), (int)RenderJobEvent.Setup, _eventsDataPtr);
                Graphics.ExecuteCommandBuffer(_eventsCommandBuffer);

                uint result = await tcs.Task;
                if (result == 0) // Failure, consider the job disposed.
                    _isJobDisposed = true;
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return 0;
            }
            finally
            {
                _eventsCommandBuffer.Clear();
                _eventsSemaphore.Release();
            }
        }

        /// <summary>Starts the job run loop. Ensure this is ONLY called after <see cref="SetupJobAsync"/> successfully creates a texture.</summary>
        /// <param name="maxFramerate">The maximum rate at which frames will be processed by the GLES pipeline.</param>
        /// <exception cref="InvalidOperationException">If an existing run loop is active.</exception>
        public void StartRunLoop(int maxFramerate = 60)
        {
            ThrowIfDisposed();
            if (_runsLoop != null)
                throw new InvalidOperationException($"Cannot call {nameof(StartRunLoop)} twice!");

            GLESAPI.RunCallbacksRegistry[_textureId] = OnFrameProcessedNative;
            _runsLoop = RunsLoopAsync(maxFramerate, _runsCancellation.Token);
        }

        /// <summary>Dispatches a single capture and awaits the resulting frame.</summary>
        /// <returns>Capture timestamp and updated texture. Timestamp will be -1 if the capture could not be processed.</returns>
        /// <exception cref="InvalidOperationException">If the run loop is active.</exception>
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="TimeoutException"/>
        public async ValueTask<(long, Texture2D)> SingleRunAsync(CancellationToken token = default)
        {
            ThrowIfDisposed();
            if (_runsLoop != null)
                throw new InvalidOperationException($"Cannot call {nameof(SingleRunAsync)} on a looping session!");

            TaskCompletionSource<long> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnComplete(long timestamp, uint _) => tcs.TrySetResult(timestamp);

            if (!await _eventsSemaphore.WaitAsync(1000, token))
                throw new TimeoutException("Timed out waiting for semaphore!");

            try
            {
                GLESAPI.RunCallbacksRegistry[_textureId] = OnComplete;

                RenderJobRunData data = new(_textureId, GLESAPI.RenderJobRunCallbackPtr);
                Marshal.StructureToPtr(data, _eventsDataPtr, false);

                _eventsCommandBuffer.IssuePluginEventAndData(GLESAPI.getGLESManageConverterJobEvent(), (int)RenderJobEvent.Run, _eventsDataPtr);
                Graphics.ExecuteCommandBuffer(_eventsCommandBuffer);

                long timestamp;
                using (CancellationTokenRegistration _ = token.Register(tcs.SetCanceled))
                    timestamp = await tcs.Task;

                return (timestamp, Texture);
            }
            finally
            {
                _eventsSemaphore.Release();
                _eventsCommandBuffer.Clear();
                GLESAPI.RunCallbacksRegistry.TryRemove(_textureId, out _);
            }
        }

        private async Task RunsLoopAsync(int maxFramerate, CancellationToken token)
        {
            try
            {
                const float IntervalMargin = 1f / 72f;
                float minInterval = 1f / maxFramerate;

                RenderJobRunData data = new(_textureId, GLESAPI.RenderJobRunCallbackPtr);
                Marshal.StructureToPtr(data, _eventsDataPtr, false);

                _eventsCommandBuffer.IssuePluginEventAndData(GLESAPI.getGLESManageConverterJobEvent(), (int)RenderJobEvent.Run, _eventsDataPtr);

                float jobDispatchTime = Time.time;
                while (!token.IsCancellationRequested)
                {
                    if (!await _eventsSemaphore.WaitAsync(1000, token))
                    {
                        Debug.LogError("Timed out waiting for job run!");
                        _eventsSemaphore.Release();
                        return;
                    }

                    try
                    {
                        float elapsed = Time.time - jobDispatchTime;
                        float additionalWait = minInterval - elapsed;

                        if (additionalWait > IntervalMargin)
                        {
#if UNITY_6000_0_OR_NEWER
                            await Awaitable.WaitForSecondsAsync(additionalWait, token);
#else
                            await Task.Delay((int)(additionalWait * 1000), token);
#endif
                        }

                        Graphics.ExecuteCommandBuffer(_eventsCommandBuffer);
                        jobDispatchTime = Time.time;
                    }
                    catch
                    {
                        _eventsSemaphore.Release();
                        throw;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                _eventsCommandBuffer.Clear();
            }
        }

        private void OnFrameProcessedNative(long timestamp, uint renderTextureId)
        {
            _eventsSemaphore.Release();
            if (timestamp == -1 || timestamp == CaptureTimestamp)
                return;

            CaptureTimestamp = timestamp;
            OnFrameProcessed?.OnMainThread(Texture, timestamp).Forget();
        }

        private void LastUpdateFrameCallback(Texture2D _, long __) => _lastUpdateFrame = Time.frameCount;

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            
            _disposed = true;
            State = ResourceState.Invalid;

            _runsCancellation.Cancel();
            _runsCancellation.Dispose();
            if (_runsLoop != null)
                await _runsLoop;

            try
            {
                await CloseWork();
            }
            finally
            {
                GLESAPI.RunCallbacksRegistry.TryRemove(_textureId, out _);

                if (!_isJobDisposed)
                    await DisposeJobAsync();

                _eventsSemaphore.Dispose();
                _eventsCommandBuffer.Dispose();

                Marshal.FreeHGlobal(_eventsDataPtr);
                UnityEngine.Object.Destroy(Texture);
            }

            GC.SuppressFinalize(this);
        }

        private async ValueTask DisposeJobAsync()
        {
            await _eventsSemaphore.WaitAsync();
            
            try
            {
                TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnComplete(bool result, uint _) => tcs.TrySetResult(result);

                GLESAPI.DisposeCallbacksRegistry[_textureId] = OnComplete;

                RenderJobDisposeData data = new(_textureId, GLESAPI.RenderJobDisposeCallbackPtr);
                Marshal.StructureToPtr(data, _eventsDataPtr, false);

                _eventsCommandBuffer.IssuePluginEventAndData(GLESAPI.getGLESManageConverterJobEvent(), (int)RenderJobEvent.Dispose, _eventsDataPtr);
                Graphics.ExecuteCommandBuffer(_eventsCommandBuffer);

                if (!await tcs.Task)
                    Debug.LogWarning("Native job disposal failed, check previous logs for more details.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                _isJobDisposed = true;
                _eventsCommandBuffer.Clear();
                _eventsSemaphore.Release();
            }
        }
    }
}