---
_layout: landing
---

# UXR.QuestCamera

A Unity package to use the new Meta Quest Passthrough Camera API.

[![openupm](https://img.shields.io/npm/v/com.uralstech.uxr.questcamera?label=openupm&registry_uri=https://package.openupm.com)](https://openupm.com/packages/com.uralstech.uxr.questcamera/)
[![openupm](https://img.shields.io/badge/dynamic/json?color=brightgreen&label=downloads&query=%24.downloads&suffix=%2Fmonth&url=https%3A%2F%2Fpackage.openupm.com%2Fdownloads%2Fpoint%2Flast-month%2Fcom.uralstech.uxr.questcamera)](https://openupm.com/packages/com.uralstech.uxr.questcamera/)

## Installation

This *should* work on any reasonably modern Unity version. Built and tested in Unity 6.0.

# [OpenUPM](#tab/openupm)

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

# [Unity Package Manager](#tab/upm)

1. Open the Unity Package Manager window (`Window` -> `Package Manager`)
2. Select the `+` icon and `Add package from git URL...`
3. Paste the UPM branch URL and press enter:
    - `https://github.com/Uralstech/UXR.QuestCamera.git#upm`
4. Add the [External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver) - you may already have it installed in your Unity project if you use Firebase or any other Google plugins.

# [GitHub Clone](#tab/github)

1. Clone or download the repository from the desired branch (master, preview/unstable)
2. Drag the package folder `UXR.QuestCamera/UXR.QuestCamera/Packages/com.uralstech.uxr.questcamera` into your Unity project's `Packages` folder
3. Add the [External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver) - you may already have it installed in your Unity project if you use Firebase or any other Google plugins.

---

## Preview Versions

Do not use preview versions (i.e. versions that end with "-preview") for production use as they are unstable and untested.

## Documentation

See <https://uralstech.github.io/UXR.QuestCamera/DocSource/QuickStart.html> or `APIReferenceManual.pdf` and `Documentation.pdf` in the package documentation for the reference manual and tutorial.
