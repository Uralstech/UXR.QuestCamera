## Setup

### Java/Kotlin Dependencies
- In the `UCamera` sub-directory, create a new folder named `libs`.
- Copy `classes.jar` from `C:\Program Files\Unity\Hub\Editor\[Unity version]\Editor\Data\PlaybackEngines\AndroidPlayer\Variations\il2cpp\Release\Classes` (or wherever Unity is installed on your computer) into `libs`.

### C++ Dependencies
- In the `UCamera\src\main\cpp` sub-directory, create a new folder named `UnityInterfaces`.
- Copy `IUnityGraphics.h` and `IUnityInterface.h` from `C:\Program Files\Unity\Hub\Editor\[Unity version]\Editor\Data\PluginAPI` (or wherever Unity is installed on your computer) into `UnityInterfaces`.