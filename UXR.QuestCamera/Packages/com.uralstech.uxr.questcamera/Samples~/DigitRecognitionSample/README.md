## Sample Setup

- In the sample folder, under `Models`, click on `mnist-21`.
- In the Inspector, click `Serialize To StreamingAssets`.

The main script in this sample is `DigitRecognition.cs`, which requires a reference to the MNIST-21 model.
If you change the path to the model, remember to change it in all `DigitRecognition.cs` scripts.

### Package Dependencies

This sample requires the Unity Sentis package (`com.unity.sentis`) and was built with version 2.1.2 of the package.
This sample also uses the old input system. There is no code that references it, but you will have to change the
UI input module in the scenes.