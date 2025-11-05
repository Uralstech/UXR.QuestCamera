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

using System;
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// QOL extensions for the JNI.
    /// </summary>
    public static class JNIExtensions
    {
        /// <summary>
        /// Unboxes and creates a global ref of a native ByteBuffer from a native Object array, and returns its direct buffer address.
        /// </summary>
        /// <param name="args">The native array to take the buffer from.</param>
        /// <param name="index">The index of the buffer object in the native array.</param>
        /// <returns>The global reference and the direct buffer address.</returns>
        public static unsafe (IntPtr obj, IntPtr ptr) UnboxAndCreateGlobalRefForByteBufferElement(IntPtr args, int index)
        {
            IntPtr localRef = AndroidJNI.GetObjectArrayElement(args, index);
            IntPtr globalRef = AndroidJNI.NewGlobalRef(localRef);
            AndroidJNI.DeleteLocalRef(localRef);

            return (globalRef, (IntPtr)AndroidJNI.GetDirectBufferAddress(globalRef));
        }

        /// <summary>
        /// Unboxes an integer from a native Object array.
        /// </summary>
        /// <param name="args">The native array to take the integer from.</param>
        /// <param name="index">The index of the integer object in the native array.</param>
        /// <returns>The unboxed integer.</returns>
        public static int UnboxIntElement(IntPtr args, int index)
        {
            IntPtr ptr = AndroidJNI.GetObjectArrayElement(args, index);
            AndroidJNIHelper.Unbox(ptr, out int value);

            AndroidJNI.DeleteLocalRef(ptr);
            return value;
        }

        /// <summary>
        /// Unboxes a long from a native Object array.
        /// </summary>
        /// <param name="args">The native array to take the long from.</param>
        /// <param name="index">The index of the long object in the native array.</param>
        /// <returns>The unboxed long.</returns>
        public static long UnboxLongElement(IntPtr args, int index)
        {
            IntPtr ptr = AndroidJNI.GetObjectArrayElement(args, index);
            AndroidJNIHelper.Unbox(ptr, out long value);

            AndroidJNI.DeleteLocalRef(ptr);
            return value;
        }

        /// <summary>
        /// Unboxes a string from a native Object array.
        /// </summary>
        /// <param name="args">The native array to take the string from.</param>
        /// <param name="index">The index of the string object in the native array.</param>
        /// <returns>The unboxed string.</returns>
        public static string? UnboxStringElement(IntPtr args, int index)
        {
            IntPtr ptr = AndroidJNI.GetObjectArrayElement(args, index);
            string? value = AndroidJNI.GetStringUTFChars(ptr);

            AndroidJNI.DeleteLocalRef(ptr);
            return value;
        }

        /// <summary>
        /// Unboxes a boolean from a native Object array.
        /// </summary>
        /// <param name="args">The native array to take the boolean from.</param>
        /// <param name="index">The index of the boolean object in the native array.</param>
        /// <returns>The unboxed boolean.</returns>
        public static bool UnboxBoolElement(IntPtr args, int index)
        {
            IntPtr ptr = AndroidJNI.GetObjectArrayElement(args, index);
            AndroidJNIHelper.Unbox(ptr, out bool value);

            AndroidJNI.DeleteLocalRef(ptr);
            return value;
        }

        /// <summary>
        /// Unboxes a native nullable integer field into an int?.
        /// </summary>
        /// <param name="fieldName">The field to unbox.</param>
        /// <returns>The unboxed value.</returns>
        public static int? GetNullableInt(this AndroidJavaObject current, string fieldName)
        {
            using AndroidJavaObject? nullable = current.Get<AndroidJavaObject>(fieldName);
            return nullable?.Call<int>("intValue");
        }

        /// <summary>
        /// Unboxes a native nullable float field into an float?.
        /// </summary>
        /// <inheritdoc cref="GetNullableInt(AndroidJavaObject, string)"/>
        public static float? GetNullableFloat(this AndroidJavaObject current, string fieldName)
        {
            using AndroidJavaObject? nullable = current.Get<AndroidJavaObject>(fieldName);
            return nullable?.Call<float>("floatValue");
        }
    }
}