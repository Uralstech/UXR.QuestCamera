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

using System.Collections.Generic;
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>An immutable package of settings and outputs needed to capture a single image from the camera device.</summary>
    public sealed class CaptureRequest : CameraMetadata
    {
        public CaptureRequest(AndroidJavaObject native) : base(native, "android.hardware.camera2.CaptureRequest") { }

        /// <summary>Gets the tag of this request previously set using <see cref="CaptureRequestBuilder.SetTag(object)"/>.</summary>
        /// <remarks>No special conversion is done for this object, so make sure it's of a type that is be supported by Unity and the JNI.</remarks>
        public T? GetTag<T>()
        {
            // TODO: Would this actually work??

            ThrowIfDisposed();
            return Native.Call<T>("getTag");
        }

        /// <summary>Builder for a capture request. WARNING: Highly unstable API.</summary>
        public sealed class Builder : CameraMetadata
        {
            public Builder(AndroidJavaObject native) : base(native, "android.hardware.camera2.CaptureRequest") { }

            /// <inheritdoc cref="Set(string, AndroidJavaObject)"/>
            public void Set(string keyName, bool value) => SetBoxed(keyName, value, "java.lang.Boolean");

            /// <inheritdoc cref="Set(string, AndroidJavaObject)"/>
            public void Set(string keyName, sbyte value) => SetBoxed(keyName, value, "java.lang.Byte");

            /// <inheritdoc cref="Set(string, AndroidJavaObject)"/>
            public void Set(string keyName, int value) => SetBoxed(keyName, value, "java.lang.Integer");

            /// <inheritdoc cref="Set(string, AndroidJavaObject)"/>
            public void Set(string keyName, long value) => SetBoxed(keyName, value, "java.lang.Long");

            /// <inheritdoc cref="Set(string, AndroidJavaObject)"/>
            public void Set(string keyName, float value) => SetBoxed(keyName, value, "java.lang.Float");

            private void SetBoxed<T>(string keyName, T value, string nativeBoxingType)
                where T : struct
            {
                using AndroidJavaObject nativeValue = new(nativeBoxingType, value);
                Set(keyName, nativeValue);
            }

            /// <summary>Set a capture request field to a value.</summary>
            /// <param name="keyName">The name of the key.</param>
            /// <param name="value">The value to set.</param>
            /// <exception cref="KeyNotFoundException">Thrown if the key is not defined in the native class.</exception>
            public void Set(string keyName, AndroidJavaObject value)
            {
                ThrowIfDisposed();
                using AndroidJavaObject key = KeyProviderClass.GetStatic<AndroidJavaObject>(keyName)
                    ?? throw new KeyNotFoundException(keyName);

                Native.Call("set", key, value);
            }


            /// <summary>Set a capture request field to a value.</summary>
            /// <param name="key">The key.</param>
            /// <param name="value">The value to set.</param>
            public void Set(Key key, AndroidJavaObject value)
            {
                ThrowIfDisposed();
                Native.Call("set", key.Native, value);
            }

            /// <summary>Set a tag for this request.</summary>
            /// <remarks>No special conversion is done for this object, so make sure it's of a type that will be supported by Unity and the JNI.</remarks>
            /// <param name="tag">An arbitrary Object to store with this request.</param>
            public void SetTag(object tag)
            {
                // TODO: Would this actually work??

                ThrowIfDisposed();
                Native.Call("setTag", tag);
            }
        }
    }
}
