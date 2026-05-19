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
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>Represents a map of camera device, capture request or capture result metadata.</summary>
    public abstract class CameraMetadata : IDisposable
    {
        /// <summary>The native object.</summary>
        public readonly AndroidJavaObject Native;

        /// <summary>The Java/Kotlin class which provides the keys and constants for this instance.</summary>
        public readonly AndroidJavaClass KeyProviderClass;

        private bool _disposed;

        /// <summary>A range of two integer values.</summary>
        /// <param name="Lower">The lower endpoint.</param>
        /// <param name="Upper">The upper endpoint.</param>
        public sealed record IntRange(int Lower, int Upper);

        /// <summary>A range of two long values.</summary>
        /// <inheritdoc cref="IntRange"/>
        public sealed record LongRange(long Lower, long Upper);

        /// <summary>A range of two float values.</summary>
        /// <inheritdoc cref="IntRange"/>
        public sealed record FloatRange(float Lower, float Upper);

        /// <summary>
        /// Light wrapper for <a href="https://developer.android.com/reference/android/hardware/camera2/CaptureRequest.Key">CaptureRequest.Key</a>,
        /// <a href="https://developer.android.com/reference/android/hardware/camera2/CameraCharacteristics.Key">CameraCharacteristics.Key</a>, etc.
        /// </summary>
        public sealed class Key : IDisposable, IEquatable<Key>
        {
            /// <summary>The native object.</summary>
            public readonly AndroidJavaObject Native;

            /// <summary>The name of the key.</summary>
            public readonly string Name;

            private bool _disposed;

            public Key(AndroidJavaObject native)
            {
                Native = native;
                Name = native.Call<string>("getName");
            }

            /// <inheritdoc/>
            public override string ToString() => Name;

            /// <inheritdoc/>
            public override bool Equals(object? obj) => Equals(obj as Key);

            /// <inheritdoc/>
            public bool Equals(Key? other) => other != null && string.Compare(other.Name, Name, StringComparison.Ordinal) == 0;

            /// <inheritdoc/>
            public override int GetHashCode() => Name.GetHashCode();

            /// <inheritdoc/>
            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                Native.Dispose();
            }
        }

        public CameraMetadata(AndroidJavaObject native, string className)
        {
            KeyProviderClass = new AndroidJavaClass(className);
            Native = native;
        }

        /// <summary>Gets a constant value from <a href="https://developer.android.com/reference/android/hardware/camera2/CameraMetadata">android.hardware.camera2.CameraMetadata</a>.</summary>
        /// <param name="name">The name of the constant field.</param>
        public int GetConstant(string name)
        {
            ThrowIfDisposed();
            return KeyProviderClass.GetStatic<int>(name);
        }

        /// <summary>Returns the keys contained in this map.</summary>
        public Key[] GetKeys()
        {
            ThrowIfDisposed();
            return GetKeysFromListNative("getKeys");
        }

        protected Key[] GetKeysFromListNative(string methodName)
        {
            using AndroidJavaObject nativeKeysList = Native.Call<AndroidJavaObject>(methodName);
            AndroidJavaObject[] nativeKeys = nativeKeysList.JavaListAsManagedArray<AndroidJavaObject>();
            return Array.ConvertAll(nativeKeys, static nativeKey => new Key(nativeKey));
        }

        /// <summary>Tries to get a value from this map.</summary>
        /// <remarks>Supports converting to the types supported by <see cref="JNIExtensions.ToManaged{T}(AndroidJavaObject)"/> + AndroidJavaObject.</remarks>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="keyName">The name of the key.</param>
        /// <param name="value">The result.</param>
        /// <returns><see langword="true"/> if the key exists and was converted successfully into a managed type, <see langword="false"/> otherwise.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the key is not defined in the native class.</exception>
        public bool TryGet<T>(string keyName, [MaybeNullWhen(false)] out T value)
        {
            ThrowIfDisposed();
            using AndroidJavaObject key = KeyProviderClass.GetStatic<AndroidJavaObject>(keyName)
                ?? throw new KeyNotFoundException(keyName);

            return TryGet(key, out value);
        }

        /// <summary>Tries to get a value from this map.</summary>
        /// <remarks>Supports converting to the types supported by <see cref="JNIExtensions.ToManaged{T}(AndroidJavaObject)"/> + AndroidJavaObject.</remarks>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="key">The key.</param>
        /// <param name="value">The result.</param>
        /// <returns><see langword="true"/> if the key exists and was converted successfully into a managed type, <see langword="false"/> otherwise.</returns>
        public bool TryGet<T>(Key key, [MaybeNullWhen(false)] out T value)
        {
            ThrowIfDisposed();
            return TryGet(key.Native, out value);
        }

        private bool TryGet<T>(AndroidJavaObject nativeKey, [MaybeNullWhen(false)] out T value)
        {
            if (typeof(T) == typeof(AndroidJavaObject))
            {
                AndroidJavaObject? nativeValue = Native.Call<AndroidJavaObject>("get", nativeKey);
                value = (T?)(object?)nativeValue;
                return nativeValue != null;
            }

            value = default;

            using AndroidJavaObject? native = Native.Call<AndroidJavaObject>("get", nativeKey);
            return native != null && native.TryConvertToManaged(out value);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
                return;

            KeyProviderClass.Dispose();
            Native.Dispose();
            _disposed = true;
        }

        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
    }
}
