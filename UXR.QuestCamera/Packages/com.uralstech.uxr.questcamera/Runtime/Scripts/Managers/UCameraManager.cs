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
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

namespace Uralstech.QuestCamera
{
    public class UCameraManager : MonoBehaviour
    {
        #region Native Method Names
        private const string Kotlin_ClassName = "com.uralstech.ucamera.Camera2Helper";
        private const string Kotlin_GetInstance = "getInstance";
        private const string Kotlin_ChangeCallbackListener = "changeCallbackListener";
        private const string Kotlin_GetDevices = "getDevices";
        private const string Kotlin_GetSupportedResolutions = "getSupportedResolutions";
        private const string Kotlin_StartCaptureSession = "startCaptureSession";
        private const string Kotlin_StopCaptureSession = "stopCaptureSession";
        #endregion

        public string[] Devices => _camera2Helper?.Call<string[]>(Kotlin_GetDevices);
        public RenderTexture CurrentFrame { get; private set; }

        public ComputeShader YUVConverter;
        public CameraEvents CallbackEvents = new();

        private ComputeBuffer _yComputeBuffer;
        private ComputeBuffer _uComputeBuffer;
        private ComputeBuffer _vComputeBuffer;
        private bool _isProcessingFrame;

        private NativeCameraFrameCallback _nativeCameraFrameCallback;
        private AndroidJavaObject _camera2Helper;

        private static unsafe void WriteNativeByteBuffer(ref ComputeBuffer buffer, IntPtr nativeBuffer, int size)
        {
            int compressedSize = Mathf.FloorToInt(size / 4f);
            if (buffer == null || buffer.count < compressedSize)
            {
                buffer?.Release();
                buffer = new ComputeBuffer(compressedSize, sizeof(uint), ComputeBufferType.Default, ComputeBufferMode.SubUpdates);

                Debug.Log($"Created new computebuffer of size: {compressedSize}");
            }
            
            NativeArray<uint> computeBufferData = buffer.BeginWrite<uint>(0, compressedSize);
            byte* pointer = (byte*)nativeBuffer.ToPointer();
            
            for (int i = 0; i < compressedSize; i++)
            {
                int originalIndex = i * 4;
                computeBufferData[i] = ((uint)pointer[originalIndex] & 0xFF)
                                        | (((uint)pointer[originalIndex + 1] & 0xFF) << 8)
                                        | (((uint)pointer[originalIndex + 2] & 0xFF) << 16)
                                        | (((uint)pointer[originalIndex + 3] & 0xFF) << 24);
            }
            
            buffer.EndWrite<uint>(compressedSize);
        }

        protected void Awake()
        {
            _nativeCameraFrameCallback = new NativeCameraFrameCallback();
            _nativeCameraFrameCallback.OnFrameReady += ProcessNewFrame;

            using AndroidJavaClass camera2HelperClass = new(Kotlin_ClassName);
            _camera2Helper = camera2HelperClass.CallStatic<AndroidJavaObject>(Kotlin_GetInstance, gameObject.name, _nativeCameraFrameCallback);
        }

        private void ProcessNewFrame(
            IntPtr yBuffer,
            IntPtr uBuffer,
            IntPtr vBuffer,
            int ySize,
            int uSize,
            int vSize,
            int yRowStride,
            int uvRowStride,
            int uvPixelStride)
        {
            if (_isProcessingFrame)
            {
                Debug.LogWarning("Camera frame dropped due to processing overload.");
                return;
            }

            _isProcessingFrame = true;

            Task copyTask = Task.Run(async () =>
            {
                await Awaitable.MainThreadAsync();
                WriteNativeByteBuffer(ref _yComputeBuffer, yBuffer, ySize);
                WriteNativeByteBuffer(ref _uComputeBuffer, uBuffer, uSize);
                WriteNativeByteBuffer(ref _vComputeBuffer, vBuffer, vSize);
            });

            copyTask.Wait();
            Task.Run(async () =>
            {
                await Awaitable.MainThreadAsync();
                int kernelHandle = YUVConverter.FindKernel("CSMain");

                YUVConverter.SetBuffer(kernelHandle, "YBuffer", _yComputeBuffer);
                YUVConverter.SetBuffer(kernelHandle, "UBuffer", _uComputeBuffer);
                YUVConverter.SetBuffer(kernelHandle, "VBuffer", _vComputeBuffer);

                YUVConverter.SetInt("YRowStride", yRowStride);
                YUVConverter.SetInt("UVRowStride", uvRowStride);
                YUVConverter.SetInt("UVPixelStride", uvPixelStride);

                YUVConverter.SetInt("TargetWidth", CurrentFrame.width);
                YUVConverter.SetInt("TargetHeight", CurrentFrame.height);

                YUVConverter.SetTexture(kernelHandle, "OutputTexture", CurrentFrame);

                int threadGroupsX = Mathf.CeilToInt(CurrentFrame.width / 8.0f);
                int threadGroupsY = Mathf.CeilToInt(CurrentFrame.height / 8.0f);
                YUVConverter.Dispatch(kernelHandle, threadGroupsX, threadGroupsY, 1);

                CallbackEvents.OnFrameReceived?.Invoke(CurrentFrame);
                _isProcessingFrame = false;
            });
        }

        public Resolution[] GetSupportedResolutions(string deviceName)
        {
            string[] supportedResolutions = _camera2Helper?.Call<string[]>(Kotlin_GetSupportedResolutions, deviceName);
            if (supportedResolutions == null)
                return null;

            Resolution[] resolutions = new Resolution[supportedResolutions.Length];
            for (int i = 0; i < supportedResolutions.Length; i++)
            {
                string[] resolutionPair = supportedResolutions[i].Split('x');
                resolutions[i] = new Resolution()
                {
                    width = int.Parse(resolutionPair[0]),
                    height = int.Parse(resolutionPair[1]),
                };
            }

            return resolutions;
        }

        public void StartCapture(string deviceName, Resolution resolution)
        {
            if (CurrentFrame == null || CurrentFrame.width != resolution.width || CurrentFrame.height != resolution.height)
            {
                if (CurrentFrame != null)
                    CurrentFrame.Release();

                CurrentFrame = new RenderTexture(resolution.width, resolution.height, 0, RenderTextureFormat.ARGB32)
                {
                    enableRandomWrite = true // Enable compute shader write access
                };

                CurrentFrame.Create();
            }

            _camera2Helper?.Call<AndroidJavaObject>(Kotlin_StartCaptureSession, deviceName, resolution.width, resolution.height);
        }

        public void StopCapture()
        {
            _camera2Helper?.Call(Kotlin_StopCaptureSession);
        }

        protected void OnDestroy()
        {
            StopCapture();

            if (CurrentFrame != null)
                CurrentFrame.Release();

            _yComputeBuffer?.Release();
            _uComputeBuffer?.Release();
            _vComputeBuffer?.Release();

            _camera2Helper?.Dispose();
            _camera2Helper = null;
        }

        #region Native Callback Forwarding
#pragma warning disable IDE1006 // Naming Styles
        public void _onCameraConnected(string data)
        {
            CallbackEvents.OnCameraConnected?.Invoke(data);
        }

        public void _onCameraDisconnected(string data)
        {
            CallbackEvents.OnCameraDisconnected?.Invoke(data);
        }

        public void _onCameraErred(string data)
        {
            CallbackEvents.OnCameraErred?.Invoke(data);
        }

        public void _onCameraConfigured(string data)
        {
            CallbackEvents.OnCameraConfigured?.Invoke(data);
        }

        public void _onCameraConfigureErred(string data)
        {
            CallbackEvents.OnCameraConfigureErred?.Invoke(data);
        }

        public void _onCameraAccessError(string data)
        {
            CallbackEvents.OnCameraAccessError?.Invoke(data);
        }

        public void _onCameraCaptureStarted(string data)
        {
            CallbackEvents.OnCameraCaptureStarted?.Invoke(data);
        }
#pragma warning restore IDE1006 // Naming Styles
        #endregion
    }
}