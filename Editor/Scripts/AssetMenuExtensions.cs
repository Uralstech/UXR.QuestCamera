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
using UnityEditor;
using UnityEngine;

namespace Uralstech.UXR.QuestCamera.Editor
{
    public static class AssetMenuExtensions
    {
        private const string PackageName = "com.uralstech.uxr.questcamera";
        private const string CameraManagerPrefabPath = "Runtime/Prefabs/QuestCameraManager.prefab";

        [MenuItem("GameObject/Quest Camera/Quest Camera Manager", false, 10)]
        private static void CreateCameraManager()
        {
            // Instantiate the prefab
            if (!InstantiatePrefab(CameraManagerPrefabPath, out string prefabPath))
                Debug.LogError($"Could not find prefab for Quest Camera Manager at path: {prefabPath}. Verify the path and ensure it exists in the package.");
        }

        private static bool InstantiatePrefab(string relativePrefabPath, out string prefabPath, bool overridePackagePathCache = false)
        {
            prefabPath = string.Empty;

            // 1. Find the package path
            string packagePath = GetPackagePath(overridePackagePathCache);
            if (string.IsNullOrEmpty(packagePath))
            {
                Debug.LogError($"Could not find package path for {PackageName}.");
                return false;
            }

            // 2. Construct the prefab path relative to the package root.
            // Use forward slashes for path since it can be used for both Windows and Unix paths.
            prefabPath = Path.Combine(packagePath, relativePrefabPath);

            // 3. Load the prefab
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return !overridePackagePathCache && InstantiatePrefab(relativePrefabPath, out prefabPath, true);

            // 4. Instantiate the prefab
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;

            // 5. Place it in the scene
            instance.transform.SetParent(Selection.activeTransform, false); // Parent to current selection

            // 6. Set name
            instance.name = prefab.name;

            // 7. Handle undo
            Undo.RegisterCreatedObjectUndo(instance, $"Create {prefab.name}");

            // 8. Select created object
            Selection.activeGameObject = instance;
            return true;
        }

        private static string s_packagePathCached = string.Empty;

        // Helper method to get the package path
        private static string GetPackagePath(bool overrideCached)
        {
            if (!string.IsNullOrEmpty(s_packagePathCached) && !overrideCached)
                return s_packagePathCached;

            s_packagePathCached = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageName)?.assetPath;
            return s_packagePathCached;
        }
    }
}
