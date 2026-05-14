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
        /// <summary>Retrieves a ByteBuffer object from a native object array and obtains its direct memory address.</summary>
        /// <param name="args">JNI object array containing the ByteBuffer.</param>
        /// <param name="index">Index of the ByteBuffer within the array.</param>
        /// <returns>A tuple containing the local JNI reference to the ByteBuffer, and the pointer to its direct buffer memory.</returns>
        public static unsafe (IntPtr obj, IntPtr ptr) UnboxByteBufferElement(IntPtr args, int index)
        {
            IntPtr localRef = AndroidJNI.GetObjectArrayElement(args, index);
            return (localRef, (IntPtr)AndroidJNI.GetDirectBufferAddress(localRef));
        }

        /// <summary>Unboxes an integer from a native Object array.</summary>
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

        /// <summary>Unboxes a long from a native Object array.</summary>
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

        /// <summary>Unboxes a string from a native Object array.</summary>
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

        /// <summary>Unboxes a boolean from a native Object array.</summary>
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


        /// <summary>Unboxes an AndroidJavaObject from a native Object array.</summary>
        /// <param name="args">The native array to take the object from.</param>
        /// <param name="index">The index of the object in the native array.</param>
        /// <returns>The unboxed object.</returns>
        public static AndroidJavaObject UnboxObjectElement(IntPtr args, int index)
        {
            IntPtr ptr = AndroidJNI.GetObjectArrayElement(args, index);
            try
            {
                return new AndroidJavaObject(ptr);
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(ptr);
            }
        }
    }
}
