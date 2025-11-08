# Migrating to UXR.QuestCamera V3

UXR.QuestCamera v3 introduces a soft rewrite of the package with several breaking changes. It's highly recommended to update to v3, as it includes fixes for many crashes and stutters. This page documents all changes to the **public API** in v3.0.0 compared to v2.6.1.

## Basic Sample

This script shows the most important changes in V3 compared to V2:
```C#
// Open the camera.
CameraDevice camera = UCameraManager.Instance.OpenCamera(currentCamera);
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

    // V2: camera.Destroy();
    // V3: Yield to DisposeAsync() using extension instead of calling Destroy().
    yield return camera.DisposeAsync().Yield();
    yield break;
}

// Create a capture session with the camera, at the chosen resolution.
// V2: CreateContinuousCaptureSession returns CaptureSessionObject<ContinuousCaptureSession>
// V3: CreateContinuousCaptureSession returns CapturePipeline<ContinuousCaptureSession>

CapturePipeline<ContinuousCaptureSession> sessionObject = camera.CreateContinuousCaptureSession(highestResolution);
if (sessionObject == null)
{
    Debug.LogError("Could not open camera session!");
    yield return camera.DisposeAsync().Yield(); // V3 dispose
    yield break;
}

yield return sessionObject.CaptureSession.WaitForInitialization();

// Check if it opened successfully
if (sessionObject.CaptureSession.CurrentState != NativeWrapperState.Opened)
{
    Debug.LogError("Could not open camera session!");

    // Both of these are important for releasing the camera and session resources.

    // V2: sessionObject.Destroy();
    // V3: Yield to DisposeAsync().
    yield return sessionObject.DisposeAsync().Yield();

    yield return camera.DisposeAsync().Yield(); // V3 dispose
    yield break;
}

// Set the image texture.
_rawImage.texture = sessionObject.TextureConverter.FrameRenderTexture;
```

---

## CameraDevice

[`CameraDevice`](~/api/Uralstech.UXR.QuestCamera.CameraDevice.yml) is no longer a `MonoBehavior` and now implements `AndroidJavaProxy` and `IAsyncDisposable`.

- **Removed**
    - `Release()`, `Destroy()` — replaced by `DisposeAsync()`.
    - `IsActiveAndUsable`

- **Changed**
    - `OnDeviceOpened`: `Action<string>` — parameter is the ID of the opened camera.
    - `OnDeviceClosed`: `Action<string?>` — parameter is the ID of the closed camera, or `null` if it failed to open.
    - `OnDeviceErred`: `Action<string?, ErrorCode>` — parameters are the ID of the erred camera (or `null` if it failed to open) and the error code.
    - `OnDeviceDisconnected`: `Action<string>` — parameter is the ID of the disconnected camera.
    - `WaitForInitialization()` now returns a `WaitUntil` object and throws `ObjectDisposedException` if the `CameraDevice` was disposed at the time of calling.
    - `WaitForInitializationAsync()` now accepts an optional `CancellationToken` and throws `ObjectDisposedException` if the `CameraDevice` was disposed at the time of calling.
    - `CreateContinuousCaptureSession()` and `CreateOnDemandCaptureSession()` now return `CapturePipeline<ContinuousCaptureSession>?` and `CapturePipeline<OnDemandCaptureSession>?`, respectively, and throw `ObjectDisposedException` if the `CameraDevice` was disposed at the time of calling.
    - `CreateSurfaceTextureCaptureSession()` and `CreateOnDemandSurfaceTextureCaptureSession()` now have nullable return types and throw `ObjectDisposedException` if the `CameraDevice` was disposed at the time of calling.
    - `CameraId` is no longer a property and is now a cached value.

---

## CaptureSessionObject<T>

`CaptureSessionObject<T>` has been replaced by [`CapturePipeline<T>`](~/api/Uralstech.UXR.QuestCamera.CapturePipeline-1.yml), which implements `IAsyncDisposable`.

- **Removed**
    - `GameObject`
    - `CameraFrameForwarder` — functionality moved to `ContinuousCaptureSession`.
    - `Destroy()` — replaced by `DisposeAsync()`.

---

## ContinuousCaptureSession

[`ContinuousCaptureSession`](~/api/Uralstech.UXR.QuestCamera.ContinuousCaptureSession.yml) is no longer a `MonoBehavior` and now implements `AndroidJavaProxy` and `IAsyncDisposable`.

- **Removed**
    - `Release()` — replaced by `DisposeAsync()`.
    - `IsActiveAndUsable`

- **Changed**
    - `OnSessionConfigurationFailed`: `Action<bool>` — parameter indicates whether the failure was caused by a camera access or security exception.
    - `OnSessionConfigured`, `OnSessionRequestSet`, `OnSessionRequestFailed`: `Action`
    - `WaitForInitialization()` now returns a `WaitUntil` object and throws `ObjectDisposedException` if the `ContinuousCaptureSession` was disposed at the time of calling.
    - `WaitForInitializationAsync()` now accepts an optional `CancellationToken` and throws `ObjectDisposedException` if the `ContinuousCaptureSession` was disposed at the time of calling.

---

## OnDemandCaptureSession

[`OnDemandCaptureSession`](~/api/Uralstech.UXR.QuestCamera.OnDemandCaptureSession.yml) inherits from `ContinuousCaptureSession` and includes the same breaking changes, plus the following:

- **Changed**
    - `RequestCapture()` now throws `ObjectDisposedException` if the `OnDemandCaptureSession` was disposed at the time of calling.

---

## YUVToRGBAConverter

[`YUVToRGBAConverter`](~/api/Uralstech.UXR.QuestCamera.YUVToRGBAConverter.yml) is no longer a `MonoBehavior` and now implements `IDisposable`.

- **Removed**
    - `Release()` — replaced by `Dispose()`.
    - `OnFrameProcessedWithTimestamp` — replaced with new `OnFrameProcessed`.
    - `SetupCameraFrameForwarder()` — replaced with new constructor `YUVToRGBAConverter(Resolution)`.
    - `CameraFrameForwarder`

- **Changed**
    - `Shader` is now a nullable-aware property.
    - `OnFrameProcessed`: `Action<RenderTexture, long>` — parameters for the frame's `RenderTexture` and the capture's timestamp, in nanoseconds.

---

## SurfaceTextureCaptureSession

[`SurfaceTextureCaptureSession`](~/api/Uralstech.UXR.QuestCamera.SurfaceTextureCapture.SurfaceTextureCaptureSession.yml) has been moved to the `Uralstech.UXR.QuestCamera.SurfaceTextureCapture` namespace, no longer inherits from `ContinuousCaptureSession`, and now implements `AndroidJavaProxy` and `IAsyncDisposable`.

- **Removed**
    - `Release()` — replaced by `DisposeAsync()`.
    - `Resolution` — use `Texture.width` and `Texture.height` instead.
    - `IsActiveAndUsable`

- **Changed**
    - `Texture` is now a read-only field.
    - `OnSessionConfigurationFailed`: `Action<bool>` — parameter indicates whether the failure was caused by a camera access or security exception.
    - `OnSessionConfigured`, `OnSessionRequestSet`, `OnSessionRequestFailed`: `Action`
    - `WaitForInitialization()` now returns a `WaitUntil` object and throws `ObjectDisposedException` if the `SurfaceTextureCaptureSession` was disposed at the time of calling.
    - `WaitForInitializationAsync()` now accepts an optional `CancellationToken` and throws `ObjectDisposedException` if the `SurfaceTextureCaptureSession` was disposed at the time of calling.

---

## OnDemandSurfaceTextureCaptureSession

[`OnDemandSurfaceTextureCaptureSession`](~/api/Uralstech.UXR.QuestCamera.SurfaceTextureCapture.OnDemandSurfaceTextureCaptureSession.yml) (moved to the `Uralstech.UXR.QuestCamera.SurfaceTextureCapture` namespace) inherits from `SurfaceTextureCaptureSession` and includes the same breaking changes, plus the following:

- **Changed**
    - `RequestCapture(Action<Texture2D>)` is now `bool RequestCapture(Action<Texture2D, long>)` where the `long` callback parameter is the capture timestamp, returning the success of the capture, and throws `ObjectDisposedException` if the `OnDemandSurfaceTextureCaptureSession` was disposed at the time of calling.
    - `RequestCapture()` now returns a `WaitUntil?` object (`null` when the capture fails) and throws `ObjectDisposedException` if the `OnDemandSurfaceTextureCaptureSession` was disposed at the time of calling.
    - `RequestCaptureAsync()` is now `Awaitable<(Texture2D?, long)> RequestCaptureAsync()` (`Texture2D` is `null` when the capture fails), accepts an optional `CancellationToken`, and throws `ObjectDisposedException` if the `OnDemandSurfaceTextureCaptureSession` was disposed at the time of calling.

---

## CameraInfo

[`CameraInfo`](~/api/Uralstech.UXR.QuestCamera.CameraInfo.yml) is now a record type and implements `IDisposable`.

- **Changed**
    - `CameraEye` now defines the following enumeration values:
        - `Unknown = -1`
        - `Left = 0`
        - `Right = 1`
    - `CameraSource` now defines the following enumeration values:
        - `Unknown = -1`
        - `PassthroughRGB = 0`
    - `LensPoseTranslation` is now nullable (`Vector3?`).
    - `LensPoseRotation` is now nullable (`Quaternion?`).
    - `Intrinsics` is now nullable (`CameraIntrinsics?`).
    - `NativeCameraCharacteristics` is now managed by the `CameraInfo` instance — **do not** dispose it manually.
    - `CameraIntrinsics` is now a record type.
    - All properties are now read-only fields with cached values.

---

## CameraFrameForwarder (Removed)

`CameraFrameForwarder` has been removed. Its functionality has been moved to `ContinuousCaptureSession`.

- **Changed**
    - `OnFrameReady`: `Action<IntPtr, IntPtr, IntPtr, int, int, int, long>` — moved to `ContinuousCaptureSession`.  
      See the [`OnFrameReady` documentation](~/api/Uralstech.UXR.QuestCamera.ContinuousCaptureSession.yml#Uralstech_UXR_QuestCamera_ContinuousCaptureSession_OnFrameReady) for parameter details.

---

## UCameraManager

All methods and properties in [`UCameraManager`](~/api/Uralstech.UXR.QuestCamera.UCameraManager.yml) are now nullable-aware with no breaking changes.

---

## CaptureTemplate

- **Removed**
    - `ZeroShutterLag` — not compatible with capture sessions created by the plugin.
