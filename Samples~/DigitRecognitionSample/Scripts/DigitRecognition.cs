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

using System.IO;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Uralstech.UXR.QuestCamera.Samples
{
    /// <summary>
    /// Does digit recognition using the Meta Quest Passthrough Camera API.
    /// </summary>
    public class DigitRecognition : MonoBehaviour
    {
        [Tooltip("The text to output results in.")]
        [SerializeField] private Text _outputText;

        [Tooltip("Preview to show the camera feed.")]
        [SerializeField] private RawImage _cameraPreview;

        [Tooltip("Preview to show what the model is getting as input.")]
        [SerializeField] private RawImage _modelInputPreview;

        [Tooltip("Button to start the camera.")]
        [SerializeField] private Button _startButton;

        [Tooltip("Button to stop the camera.")]
        [SerializeField] private Button _stopButton;

        [Tooltip("Path to the model in StreamingAssets.")]
        [SerializeField] private string _modelPathInStreamingAssets = "mnist-12.sentis";

        private CameraInfo _cameraInfo; // Camera metadata.
        private CameraDevice _cameraDevice; // Camera device.
        private CaptureSessionObject<ContinuousCaptureSession> _captureSession; // Camera capture session data.

        private Worker _digitRecognitionWorker; // The model inference worker.
        private Tensor<float> _inputTensors; // The model input.
        private Texture2D _imageTexture; // The input texture.
        private bool _isModelBusy; // Busy flag.

        // Start is called on the frame when a script is enabled for the first time.
        protected async void Start()
        {
            // Load the model.
            Model model = await LoadModel();
            if (model is null)
                return;

            // Configure it and get the input tensor shape.
            TensorShape shape;
            (model, shape) = ConfigureModel(model);

            // Initialize the input tensors, the inference worker and the input texture.
            _inputTensors = new Tensor<float>(shape);
            _digitRecognitionWorker = new Worker(model, BackendType.GPUCompute);
            _imageTexture = new Texture2D(28, 28, TextureFormat.ARGB32, false);

            // Set that as the _modelInputPreview image.
            _modelInputPreview.texture = _imageTexture;

            // Add button listeners.
            _startButton.onClick.AddListener(StartCamera);
            _stopButton.onClick.AddListener(StopCamera);

            // Check if the camera permission has been given.
            if (Permission.HasUserAuthorizedPermission(UCameraManager.HeadsetCameraPermission))
            {
                // Get the left eye camera.
                _cameraInfo = UCameraManager.Instance.GetCamera(CameraInfo.CameraEye.Left);
                Debug.Log($"Got camera info: {_cameraInfo}");
            }
            else
            {
                // Callback to set _cameraInfo when the permission is granted.
                PermissionCallbacks callbacks = new();
                callbacks.PermissionGranted += _ =>
                {
                    _cameraInfo = UCameraManager.Instance.GetCamera(CameraInfo.CameraEye.Left);
                    Debug.Log($"Got new camera info after camera permission was granted: {_cameraInfo}");
                };

                // Request the permission and set the flag to true.
                Permission.RequestUserPermission(UCameraManager.HeadsetCameraPermission, callbacks);
                Debug.Log("Camera permission requested.");
            }
        }

        // Destroying the attached Behaviour will result in the game or Scene receiving OnDestroy.
        protected void OnDestroy()
        {
            // Stop the camera and release the model worker and input tensors when the GameObject is destroyed.

            StopCamera();
            _digitRecognitionWorker?.Dispose();
            _inputTensors?.Dispose();
        }

        /// <summary>
        /// Loads the model from the StreamingAssets directory.
        /// </summary>
        /// <remarks>
        /// The StreamingAssets folder is a part of the APK itself, which is a ZIP file,
        /// so you can't use File.Read* for it. Thus, you have to use UnityWebRequest.
        /// </remarks>
        private async Awaitable<Model> LoadModel()
        {
            // Create a web request to download the contents at the model path.
            using UnityWebRequest request = new()
            {
                uri = new System.Uri(Path.Combine(Application.streamingAssetsPath, _modelPathInStreamingAssets)),
                downloadHandler = new DownloadHandlerBuffer(),
                disposeDownloadHandlerOnDispose = true,
            };

            // Send it and check the request state.
            await request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Could not load model through web request: {request.error}");
                return null;
            }

            // Copy it to an accessible temporary file.
            string tempPath = Path.Combine(Application.temporaryCachePath, "tempModelCopy.sentis");
            File.WriteAllBytes(tempPath, request.downloadHandler.data);

            // Load it from there.
            Model sourceModel = ModelLoader.Load(tempPath);

            // Delete the temporary file.
            File.Delete(tempPath);
            return sourceModel;
        }

        /// <summary>
        /// Configures the model.
        /// </summary>
        /// <returns>The model and the shape of the input tensor.</returns>
        private (Model, TensorShape) ConfigureModel(Model model)
        {
            // This code is from Unity's official sample: 
            // https://github.com/Unity-Technologies/sentis-samples/blob/526fbb4e2e6767afe347cd3393becd0e3e64ae2b/DigitRecognitionSample/Assets/Scripts/MNISTEngine.cs#L44

            FunctionalGraph graph = new();

            TensorShape tensorShape = new(1, 1, 28, 28);
            FunctionalTensor input = graph.AddInput(DataType.Float, tensorShape);
            FunctionalTensor[] outputs = Functional.Forward(model, input);
            FunctionalTensor result = outputs[0];

            // Convert the result to probabilities between 0..1 using the softmax function
            FunctionalTensor probabilities = Functional.Softmax(result);
            FunctionalTensor indexOfMaxProba = Functional.ArgMax(probabilities, -1, false);
            return (graph.Compile(probabilities, indexOfMaxProba), tensorShape);
        }

        /// <summary>
        /// Starts the camera.
        /// </summary>
        private async void StartCamera()
        {
            // Check if _cameraInfo is null.
            if (_cameraInfo == null)
            {
                // if null, log an error, as the camera permission was not given.
                Debug.LogError("Camera permission was not given.");
                return;
            }

            // If already open, return.
            if (_cameraDevice != null || _captureSession != null)
            {
                Debug.Log("Camera or capture session is already open.");
                return;
            }

            // Open the camera.
            _cameraDevice = UCameraManager.Instance.OpenCamera(_cameraInfo);

            // Wait for initialization and check its state.
            NativeWrapperState state = await _cameraDevice.WaitForInitializationAsync();
            if (state != NativeWrapperState.Opened)
            {
                Debug.LogError("Failed to open camera.");

                // Destroy the camera to release native resources.
                _cameraDevice.Destroy();
                _cameraDevice = null;
                return;
            }

            Debug.Log("Camera opened.");

            // Open the capture session.
            _captureSession = _cameraDevice.CreateContinuousCaptureSession(_cameraInfo.SupportedResolutions[^1]);

            // Wait for initialization and check its state.
            state = await _captureSession.CaptureSession.WaitForInitializationAsync();
            if (state != NativeWrapperState.Opened)
            {
                Debug.LogError("Failed to open capture session.");

                // Destroy the camera AND capture session to release native resources.
                _captureSession.Destroy();
                _cameraDevice.Destroy();

                (_cameraDevice, _captureSession) = (null, null);
            }

            // Set _cameraPreview to the texture.
            _cameraPreview.texture = _captureSession.TextureConverter.FrameRenderTexture;

            // Set a callback for when each frame is ready for the AI.
            _captureSession.TextureConverter.OnFrameProcessed.AddListener(OnFrameReady);
            Debug.Log("Capture session opened.");
        }

        /// <summary>
        /// Callback for processing the image.
        /// </summary>
        /// <param name="renderTexture">The RenderTexture to process.</param>
        private async void OnFrameReady(RenderTexture renderTexture)
        {
            // If the model is already running, return.
            if (_isModelBusy)
                return;

            // Set _isModelBusy flag.
            _isModelBusy = true;

            // Copy the render texture, since TextureConverter.ToTensor only supports Texture2Ds.
            CopyRenderTexture(_imageTexture, renderTexture);

            // Convert the texture to a tensor.
            TextureConverter.ToTensor(_imageTexture, _inputTensors, new TextureTransform());

            // Schedule inference.
            _digitRecognitionWorker.Schedule(_inputTensors);

            // Wait for the results.
            using Tensor<float> probabilities = await (_digitRecognitionWorker.PeekOutput(0) as Tensor<float>).ReadbackAndCloneAsync();
            using Tensor<int> indexOfMaxProba = await (_digitRecognitionWorker.PeekOutput(1) as Tensor<int>).ReadbackAndCloneAsync();

            // The predicted number, and the probability of it being correct.
            int predictedNumber = indexOfMaxProba[0];
            float probability = probabilities[predictedNumber];

            // Display it.
            _outputText.text = $"Number: {predictedNumber} | Probability: {probability}";

            // Unset _isModelBusy flag.
            _isModelBusy = false;
        }

        /// <summary>
        /// Copies the contents of the RenderTexture into the Texture2D. 
        /// </summary>
        private void CopyRenderTexture(Texture2D texture, RenderTexture renderTexture)
        {
            // Save the current active RenderTexture.
            RenderTexture prev = RenderTexture.active;

            // Set the RenderTexture to copy as the active RenderTexture.
            RenderTexture.active = renderTexture;

            // Read 28 pixels from the center of the RenderTexture.
            texture.ReadPixels(new Rect((renderTexture.width / 2f) - 14f, (renderTexture.height / 2f) - 14f, 28, 28), 0, 0);

            // Save that to GPU.
            texture.Apply();

            // Reset the active RenderTexture.
            RenderTexture.active = prev;
        }

        /// <summary>
        /// Stops the camera.
        /// </summary>
        private void StopCamera()
        {
            if (_captureSession != null)
            {
                // Destroy the session to release native resources.
                _captureSession.Destroy();
                _captureSession = null;
            }

            if (_cameraDevice != null)
            {
                // Destroy the camera to release native resources.
                _cameraDevice.Destroy();
                _cameraDevice = null;
            }
        }
    }
}
