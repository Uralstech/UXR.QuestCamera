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
using System.Collections.Generic;
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>An immutable package of settings and outputs needed to capture a single image from the camera device.</summary>
    public sealed class CaptureRequest : CameraMetadata
    {
        public CaptureRequest(AndroidJavaObject native) : base(native, "android.hardware.camera2.CaptureRequest") { }

        /// <summary>Gets the tag of this request previously set using <see cref="Builder.SetTag{T}(T)"/>.</summary>
        /// <remarks>Supports the types supported by <see cref="JNIExtensions.ToManaged{T}(AndroidJavaObject)"/> + AndroidJavaObject.</remarks>
        public T? GetTag<T>()
        {
            ThrowIfDisposed();

            Type targetType = typeof(T);
            AndroidJavaObject tag = Native.Call<AndroidJavaObject>("getTag");
            if (targetType == typeof(AndroidJavaObject))
                return (T)(object)tag;

            using (tag)
                return (T)tag.ToManaged(targetType);
        }

        /// <summary>Builder for a capture request.</summary>
        public sealed class Builder : CameraMetadata
        {
            public Builder(AndroidJavaObject native) : base(native, "android.hardware.camera2.CaptureRequest") { }

            /// <summary>Set a capture request field to a value.</summary>
            /// <remarks>Supports the types supported by <see cref="JNIExtensions.ToJava{T}(T)"/> + AndroidJavaObject.</remarks>
            /// <typeparam name="T">The type of the value.</typeparam>
            /// <param name="keyName">The name of the key.</param>
            /// <param name="value">The value to set.</param>
            /// <exception cref="KeyNotFoundException">Thrown if the key is not defined in <see cref="CameraMetadata.KeyProviderClass"/>.</exception>
            public void Set<T>(string keyName, T value)
                where T : notnull
            {
                ThrowIfDisposed();
                using AndroidJavaObject key = KeyProviderClass.GetStatic<AndroidJavaObject>(keyName)
                    ?? throw new KeyNotFoundException(keyName);

                Set(key, value);
            }

            /// <summary>Set a capture request field to a value.</summary>
            /// <remarks>Supports the types supported by <see cref="JNIExtensions.ToJava{T}(T)"/> + AndroidJavaObject.</remarks>
            /// <typeparam name="T">The type of the value.</typeparam>
            /// <param name="key">The key.</param>
            /// <param name="value">The value to set.</param>
            public void Set<T>(Key key, T value)
                where T : notnull
            {
                ThrowIfDisposed();
                Set(key.Native, value);
            }

            private void Set<T>(AndroidJavaObject key, T value)
                where T : notnull
            {
                Type tagType = typeof(T);
                if (tagType == typeof(AndroidJavaObject))
                {
                    Native.Call("set", key, (AndroidJavaObject)(object)value);
                    return;
                }

                using AndroidJavaObject converted = value.ToJava(tagType);
                Native.Call("set", key, converted);
            }

            /// <summary>Set a tag for this request.</summary>
            /// <remarks>Supports the types supported by <see cref="JNIExtensions.ToJava{T}(T)"/> + AndroidJavaObject.</remarks>
            /// <typeparam name="T">The type of the tag.</typeparam>
            /// <param name="tag">An arbitrary Object to store with this request.</param>
            public void SetTag<T>(T tag)
                where T : notnull
            {
                ThrowIfDisposed();

                Type tagType = typeof(T);
                if (tagType == typeof(AndroidJavaObject))
                {
                    Native.Call("setTag", tag);
                    return;
                }

                using AndroidJavaObject converted = tag.ToJava(tagType);
                Native.Call("setTag", converted);
            }
        }
    }
}
