# Quick Start

The example code provided in this quick start guide is for educational and demonstration purposes only. It may not represent best practices for production use.
This quick start was last updated for **UXR.QuestCamera v3.1.0-preview.1**.

## Breaking Changes Notice

If you've just updated the package to v3.0.0 or later, it's recommended to check the [migration guide](~/DocSource/V3Migration.md) for information on breaking changes from v2.6.1, including async disposal patterns, renamed types (e.g., `CaptureSessionObject<T>` → `CapturePipeline<T>`), and nullable-aware APIs.

## Setup

### Dependencies

UXR.QuestCamera uses an AAR plugin to access the Camera2 API and requires the **External Dependency Manager for Unity (EDM4U)** package
to handle native dependencies. If you have Firebase or Google SDKs in your project, you likely have it installed. If not, you can see
the installation steps here: <https://github.com/googlesamples/unity-jar-resolver?tab=readme-ov-file#getting-started>

### Unity Version Compatibility

This package uses some `Awaitable` methods for switching between threads for frame processing. Since `Awaitable` was only added in
Unity 6, you will have to install **com.utilities.async** by Stephen Hodgson on older versions of Unity. You can see the installation
steps here: <https://github.com/RageAgainstThePixel/com.utilities.async>

### AndroidManifest.xml

> [!NOTE]
> You can skip this step if you're using the Meta XR Core SDK v81 or higher by enabling the ‘Enabled Passthrough Camera Access’ setting in your `OVR Manager` instance and regenerating your AndroidManifest using the SDK's tools.

Add the following to your project's `AndroidManifest.xml` file:

```xml
<uses-feature android:name="android.hardware.camera2.any" android:required="true"/>
<uses-permission android:name="horizonos.permission.HEADSET_CAMERA" android:required="true"/>
```

The `HEADSET_CAMERA` permission is required by Horizon OS for apps to access the headset cameras.
You must request it at runtime before using any of this package's APIs, like so:

```csharp
if (!Permission.HasUserAuthorizedPermission(UCameraManager.HeadsetCameraPermission))
    Permission.RequestUserPermission(UCameraManager.HeadsetCameraPermission);
```

## Usage

### Device Support

The Passthrough Camera API is restricted to the Quest 3 family and newer devices on Horizon OS version >= 74.
Check if the current device is supported with the [`CameraSupport.IsSupported`](~/api/Uralstech.UXR.QuestCamera.CameraSupport.yml) static property.

### Choosing the Camera

[`UCameraManager`](~/api/Uralstech.UXR.QuestCamera.UCameraManager.yml) allows you to access [`CameraInfo`](~/api/Uralstech.UXR.QuestCamera.CameraInfo.yml)
objects and create [`CameraDevice`](~/api/Uralstech.UXR.QuestCamera.CameraDevice.yml) objects. It's a persistent singleton, so add it to a GameObject
in the first scene it's referenced in, and it can be used in any scene loaded thereafter.

`UCameraManager` creates a `CameraInfo` object for each camera the native plugin detects. Each `CameraInfo` then exposes the
supported resolutions and intrinsic information of the physical camera device. You can get an array of all detected cameras in
`UCameraManager.Cameras`, or request a camera associated with the left or right eye using `UCameraManager.GetCamera(CameraInfo.CameraEye)`.

Even though `CameraInfo` is an `IDisposable`, `UCameraManager` manages the disposal of the objects returned by the above APIs.
You can get independently managed `CameraInfo` objects using `UCameraManager.GetCameraInfos()`.

### Opening the Camera

You can open a camera device using `UCameraManager.OpenCamera(cameraId)`. Pass the `CameraInfo` object to this function
(it is implicitly converted to the ID of the camera it represents). This method returns a `CameraDevice?`.

`CameraDevice` is a wrapper for the native Camera2 `CameraDevice` class. It allows you to create capture sessions, which provide
actual images from the camera. It takes a bit for the camera device to open, so you need to wait for it using
`CameraDevice.WaitForInitialization()` (Coroutine) or `CameraDevice.WaitForInitializationAsync()` (Task).

The task returns a boolean confirming that the camera opened successfully. When using the coroutine method, use the `CurrentState` property
after yielding to confirm the camera opened. To get specific error reasons, check logcat or add listeners to
`CameraDevice.OnDeviceDisconnected` and `CameraDevice.OnDeviceErred`.

If the camera couldn't open, release its native resources by awaiting `camera.DisposeAsync()`.
You can yield async calls in coroutines using the package-provided `.Yield()` extension.

### Creating a Capture Session

After opening a camera device, you can start a capture session. Keep in mind that you can't close the camera device while the
capture session is still active.

You can create two kinds of capture sessions: continuous and on-demand. A continuous session streams a sequence of frames
to Unity, each converted from YUV to RGBA. If you don’t need a live feed, you can save resources by using an on-demand capture session.
On-demand sessions only process frames when explicitly requested.

To create a new continuous session, use `CameraDevice.CreateContinuousCaptureSession(resolution)`.
To create an on-demand session, use `CameraDevice.CreateOnDemandCaptureSession(resolution)`. Supported resolutions for the camera
are exposed in `CameraInfo.SupportedResolutions` as an array of Unity’s `Resolution` objects. The last value in the array is usually
the highest resolution.

These methods return [`CapturePipeline<ContinuousCaptureSession>?`](~/api/Uralstech.UXR.QuestCamera.ContinuousCaptureSession.yml) and
[`CapturePipeline<OnDemandCaptureSession>?`](~/api/Uralstech.UXR.QuestCamera.OnDemandCaptureSession.yml) objects respectively.
Each contains the session object (`CapturePipeline<T>.CaptureSession`) and a YUV-to-RGBA texture converter (`CapturePipeline<T>.TextureConverter`).

As with `CameraDevice`, wait for the session to open using `CaptureSession.WaitForInitialization()` or
`CaptureSession.WaitForInitializationAsync()`, and check `CurrentState` when using the coroutine method.
If the session could not be started successfully, release its native resources by awaiting `CapturePipeline<T>.DisposeAsync()`.
For error details, check logcat or add listeners to `CaptureSession.OnSessionConfigurationFailed` and `CaptureSession.OnSessionRequestFailed`.

Once started, you’ll get frames from the camera in an ARGB32 `RenderTexture` stored in `TextureConverter.FrameRenderTexture`.
For on-demand sessions, the `RenderTexture` remains black until you call `CaptureSession.RequestCapture()`, which can be called
any number of times. The method returns a boolean indicating whether the request succeeded.
You can then await `TextureConverter.GetNextFrameAsync()` to get the requested frame, along with the capture timestamp.

When you’re done with the session, dispose of it by awaiting `CapturePipeline<T>.DisposeAsync()`.
This disposes both the texture converter and capture session simultaneously. You can then dispose of the camera device,
ensuring you close the capture session *before* closing the camera.

See Unity’s `RenderTexture` documentation for information on reading pixel data to the CPU:
[https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.html](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.html)

#### Capture Templates

UXR.QuestCamera supports a subset of Camera2’s capture templates. By default,
`CameraDevice.CreateContinuousCaptureSession` uses
[TEMPLATE_PREVIEW](https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_PREVIEW), and
`OnDemandCaptureSession.RequestCapture()` uses
[TEMPLATE_STILL_CAPTURE](https://developer.android.com/reference/android/hardware/camera2/CameraDevice#TEMPLATE_STILL_CAPTURE).
You can change them by specifying one of the templates defined in the [`CaptureTemplate`](~/api/Uralstech.UXR.QuestCamera.CaptureTemplate.yml) enum.

### Releasing Resources

Make sure to dispose of all camera resources *immediately* after you finish using them so the native camera device and
capture session are properly closed. You can also force closure synchronously, for example in `OnApplicationQuit`,
where Unity won’t wait for async methods:

```csharp
Task.WhenAll(
    captureSession.DisposeAsync().AsTask(),
    cameraDevice.DisposeAsync().AsTask()
).Wait();

Debug.Log("Synchronously closed resources.");
```

### Using the `await using` Pattern

If you can use C#’s `await using` statement, you can simplify the entire process significantly. For example:

```csharp
public async Task TakePicture()
{
    if (UCameraManager.Instance.GetCamera(CameraInfo.CameraEye.Left) is not CameraInfo cameraInfo)
    {
        Debug.LogError("Could not get camera info!");
        return;
    }

    await using CameraDevice? cameraDevice = UCameraManager.Instance.OpenCamera(cameraInfo);
    if (cameraDevice == null || !await cameraDevice.WaitForInitializationAsync())
    {
        Debug.LogError("Could not open camera!");
        return;
    }

    Resolution resolution = cameraInfo.SupportedResolutions[^1];
    await using CapturePipeline<OnDemandCaptureSession>? capturePipeline = cameraDevice.CreateOnDemandCaptureSession(resolution);
    if (capturePipeline == null || !await capturePipeline.CaptureSession.WaitForInitializationAsync())
    {
        Debug.LogError("Could not open capture session!");
        return;
    }

    if (!capturePipeline.CaptureSession.RequestCapture())
    {
        Debug.LogError("Could not capture frame!");
        return;
    }

    (RenderTexture texture, long timestamp) = await capturePipeline.TextureConverter.GetNextFrameAsync();
    // Process the RenderTexture here!
}
```

### Better Performance in OpenGL

If your app uses the OpenGL Graphics API, you can use `SurfaceTextureCaptureSession` and `OnDemandSurfaceTextureCaptureSession`
(in the `Uralstech.UXR.QuestCamera.SurfaceTextureCapture` namespace) instead of `ContinuousCaptureSession` and `OnDemandCaptureSession`.
This can improve performance since SurfaceTexture-based sessions use low-level OpenGL shaders for YUV-to-RGBA conversion.

They’re also simpler to use, as they don’t require additional components like texture converters or frame forwarders.
Both provide a read-only `Texture` property that stores the camera images.

You can create them by calling `CameraDevice.CreateSurfaceTextureCaptureSession()` or
`CameraDevice.CreateOnDemandSurfaceTextureCaptureSession()`, like so:

```csharp
CameraDevice camera = ...;
Resolution resolution = ...;

// Create a capture session with the camera at the chosen resolution.
SurfaceTextureCaptureSession? session = camera.CreateSurfaceTextureCaptureSession(resolution);
if (session == null) { /* Handle error */ }
yield return session.WaitForInitialization();

// Check if it opened successfully
if (session.CurrentState != NativeWrapperState.Opened)
{
    Debug.LogError("Could not open camera session!");

    // Release camera and session resources.
    yield return session.DisposeAsync().Yield();
    yield return camera.DisposeAsync().Yield();
    yield break;
}

// Set the image texture.
_rawImage.texture = session.Texture;
```

## Example Script

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UI;
using Uralstech.UXR.QuestCamera;

public class CameraTest : MonoBehaviour
{
    [SerializeField] private RawImage _rawImage;

    private IEnumerator Start()
    {
        // Check if the current device is supported.
        if (!CameraSupport.IsSupported)
        {
            Debug.LogError("Device does not support the Passthrough Camera API!");
            yield break;
        }

        // Check for permission.
        if (!Permission.HasUserAuthorizedPermission(UCameraManager.HeadsetCameraPermission))
        {
            // If the has not yet given the permission, request it and exit out of this function.
            Permission.RequestUserPermission(UCameraManager.HeadsetCameraPermission);
            yield break;
        }

        // Get a camera device.
        CameraInfo? currentCamera = UCameraManager.Instance.GetCamera(CameraInfo.CameraEye.Left);
        if (currentCamera == null)
        {
            Debug.LogError("No camera available!");
            yield break;
        }

        // Get the supported resolutions of the camera and choose the highest resolution.
        Resolution highestResolution = default;
        foreach (Resolution resolution in currentCamera.SupportedResolutions)
        {
            if (resolution.width * resolution.height > highestResolution.width * highestResolution.height)
                highestResolution = resolution;
        }

        // Open the camera.
        CameraDevice? camera = UCameraManager.Instance.OpenCamera(currentCamera);
        if (camera == null)
        {
            Debug.LogError("Could not open camera!");
            yield break;
        }

        yield return camera.WaitForInitialization();

        // Check if it opened successfully
        if (camera.CurrentState != NativeWrapperState.Opened)
        {
            Debug.LogError("Could not open camera!");

            // Very important, this frees up any resources held by the camera.
            yield return camera.DisposeAsync().Yield();
            yield break;
        }

        // Create a capture session with the camera, at the chosen resolution.
        CapturePipeline<ContinuousCaptureSession>? sessionPipeline = camera.CreateContinuousCaptureSession(highestResolution);
        if (sessionPipeline == null)
        {
            Debug.LogError("Could not create session!");
            yield return camera.DisposeAsync().Yield();
            yield break;
        }

        yield return sessionPipeline.CaptureSession.WaitForInitialization();

        // Check if it opened successfully
        if (sessionPipeline.CaptureSession.CurrentState != NativeWrapperState.Opened)
        {
            Debug.LogError("Could not open camera session!");

            // Both of these are important for releasing the camera and session resources.
            yield return sessionPipeline.DisposeAsync().Yield();
            yield return camera.DisposeAsync().Yield();
            yield break;
        }

        // Set the image texture.
        _rawImage.texture = sessionPipeline.TextureConverter.FrameRenderTexture;

        // Optional: Dispose at end of use (e.g., in StopCoroutine or OnDestroy)
        // yield return sessionPipeline.DisposeAsync().Yield();
        // yield return camera.DisposeAsync().Yield();
    }
}
```

## Sample - Digit Recognition with Unity Inference Engine

The package contains a Computer Vision sample that uses an MNIST trained model to recognize handwritten digits, through the Camera API.

### Package Dependencies

This sample requires the Unity Inference Engine package (`com.unity.ai.inference`) and was built with version 2.3.0 of the package.