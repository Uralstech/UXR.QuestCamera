# UXR.QuestCamera

A Unity package to use the new Meta Quest Passthrough Camera API.

[![openupm](https://img.shields.io/npm/v/com.uralstech.uxr.questcamera?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.uralstech.uxr.questcamera/)
[![openupm](https://img.shields.io/badge/dynamic/json?color=brightgreen&label=downloads&query=%24.downloads&suffix=%2Fmonth&url=https%3A%2F%2Fpackage.openupm.com%2Fdownloads%2Fpoint%2Flast-month%2Fcom.uralstech.uxr.questcamera)](https://openupm.com/packages/com.uralstech.uxr.questcamera/)

## Installation

This *should* work on any reasonably modern Unity version. Built and tested in Unity 6.0.

### OpenUPM

1. Open project settings
2. Select `Package Manager`
3. Add the OpenUPM package registry:
    - Name: `OpenUPM`
    - URL: `https://package.openupm.com`
    - Scope(s)
        - `com.uralstech`
4. Open the Unity Package Manager window (`Window` -> `Package Manager`)
5. Change the registry from `Unity` to `My Registries`
6. Add the `UXR.QuestCamera` package
7. Add the [External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver) - you may already have it installed in your Unity project if you use Firebase or any other Google plugins.

### Unity Package Manager

1. Open the Unity Package Manager window (`Window` -> `Package Manager`)
2. Select the `+` icon and `Add package from git URL...`
3. Paste the UPM branch URL and press enter:
    - `https://github.com/Uralstech/UXR.QuestCamera.git#upm`
4. Add the [External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver) - you may already have it installed in your Unity project if you use Firebase or any other Google plugins.

### GitHub Clone

1. Clone or download the repository from the desired branch (master, preview/unstable)
2. Drag the package folder `UXR.QuestCamera/UXR.QuestCamera/Packages/com.uralstech.uxr.questcamera` into your Unity project's `Packages` folder
3. Add the [External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver) - you may already have it installed in your Unity project if you use Firebase or any other Google plugins.

## Preview Versions

Do not use preview versions (i.e. versions that end with "-preview") for production use as they are unstable and untested.

## AndroidManifest Setup

You will have to define the following permissions in your Android Manifest:
```xml
<uses-permission android:name="android.permission.CAMERA" android:required="true"/>
<uses-permission android:name="horizonos.permission.HEADSET_CAMERA" android:required="true"/>
```

This package cannot request these permissions for you during runtime, you will have to do that manually.

## Example Usage

```csharp
using UnityEngine;
using UnityEngine.UI;
using Uralstech.UXR.QuestCamera;

public class CameraTest : MonoBehaviour
{
    [SerializeField] private RawImage _rawImage;
    [SerializeField] private int _width = 1280;
    [SerializeField] private int _height = 920;

    private UCameraManager _cameraManager;

    void Start()
    {
        _cameraManager = FindAnyObjectByType<UCameraManager>();

        // Called when capture starts.
        _cameraManager.CallbackEvents.OnCameraCaptureStarted.AddListener(_ => _rawImage.texture = _cameraManager.CurrentFrame);

        // Get available camera devices.
        string[] devices = _cameraManager.Devices;
        foreach (string device in devices)
            Debug.Log($"Found device: {device}");

        Resolution largestResolution = new()
        {
            width = 0,
            height = 0,
        };

        // Get supported resolutions for the first device, and select the largest.
        foreach (Resolution resolution in _cameraManager.GetSupportedResolutions(devices[0]))
        {
            Debug.Log($"Supported resolution: {resolution}");
            if (resolution.width * resolution.height > largestResolution.width * largestResolution.height)
                largestResolution = resolution;
        }

        Debug.Log($"Selected resolution: {largestResolution}");

        // Start capture.
        _cameraManager.StartCapture(devices[0], largestResolution);
    }
}
```

## Documentation

See <https://uralstech.github.io/UXR.QuestCamera/DocSource/QuickStart.html> or `APIReferenceManual.pdf` and `Documentation.pdf` in the package documentation for the reference manual and tutorial.
