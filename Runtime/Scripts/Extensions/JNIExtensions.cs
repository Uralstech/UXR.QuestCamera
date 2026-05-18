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
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>QOL extensions for the JNI.</summary>
    /// <remarks>
    /// This class is <see langword="public"/> to allow package users to reuse these extensions if useful.
    /// However, it should not be considered stable and is not part of the supported public API.
    /// It exists solely as an internal utility and may change or be removed at any time.
    /// </remarks>
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

        /// <summary>Converts a java.util.List to a managed array.</summary>
        /// <remarks>
        /// <para>
        /// WARNING: This method <b>DOES NOT</b> verify if the native Java/Kotlin elements can actually be converted to the target type.
        /// </para>
        /// 
        /// Supports converting elements to the types supported by <see cref="Convert{T}(AndroidJavaObject)"/> + AndroidJavaObject.
        /// </remarks>
        /// <typeparam name="T">The type of the elements.</typeparam>
        /// <returns>The converted array.</returns>
        public static T[] ConvertList<T>(this AndroidJavaObject current)
        {
            int length = current.Call<int>("size");
            T[] result = new T[length];

            bool isAndroidJavaObjectArray = typeof(T) == typeof(AndroidJavaObject);
            for (int i = 0; i < length; i++)
            {
                if (isAndroidJavaObjectArray)
                {
                    result[i] = (T)(object)current.Call<AndroidJavaObject>("get", i);
                    continue;
                }

                using AndroidJavaObject element = current.Call<AndroidJavaObject>("get", i);
                result[i] = Convert<T>(element);
            }

            return result;
        }

        /// <summary>Safe version of <see cref="Convert{T}(AndroidJavaObject)"/>.</summary>
        public static bool TryConvert<T>(this AndroidJavaObject current, [MaybeNullWhen(false)] out T? value)
        {
            try
            {
                value = Convert<T>(current);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <inheritdoc cref="Convert(AndroidJavaObject, Type)"/>
        /// <typeparam name="T">The managed target type.</typeparam>
        public static T Convert<T>(this AndroidJavaObject current) => (T)Convert(current, typeof(T));

        /// <summary>Converts a Java/Kotlin object represented by an <see cref="AndroidJavaObject"/> into a managed .NET/Unity type.</summary>
        /// <remarks>
        /// <para>
        /// WARNING: This method <b>DOES NOT</b> verify if the native Java/Kotlin object can actually be converted to the target type.
        /// </para>
        /// 
        /// Basic supported types:
        /// <list type="bullet">
        ///     <item><description><see cref="bool"/></description></item>
        ///     <item><description><see cref="char"/></description></item>
        ///     <item><description><see cref="sbyte"/></description></item>
        ///     <item><description><see cref="short"/></description></item>
        ///     <item><description><see cref="int"/></description></item>
        ///     <item><description><see cref="long"/></description></item>
        ///     <item><description><see cref="float"/></description></item>
        ///     <item><description><see cref="double"/></description></item>
        ///     <item><description><see cref="string"/></description></item>
        /// </list>
        ///
        /// Supported Android types:
        /// <list type="table">
        ///     <listheader>
        ///         <term>Unity Type</term>
        ///         <description>Android Type</description>
        ///     </listheader>
        /// 
        ///     <item>
        ///         <term><see cref="Resolution"/></term>
        ///         <description><a href="https://developer.android.com/reference/android/util/Size">android.util.Size</a></description>
        ///     </item>
        ///     <item>
        ///         <term><see cref="Rect"/></term>
        ///         <description><a href="https://developer.android.com/reference/android/graphics/RectF">android.graphics.RectF</a></description>
        ///     </item>
        ///     <item>
        ///         <term><see cref="RectInt"/></term>
        ///         <description><a href="https://developer.android.com/reference/android/graphics/Rect">android.graphics.Rect</a></description>
        ///     </item>
        ///     <item>
        ///         <term><see cref="CameraMetadata.IntRange"/></term>
        ///         <description><a href="https://developer.android.com/reference/android/util/Range">android.util.Range&lt;Integer&gt;</a></description>
        ///     </item>
        ///     <item>
        ///         <term><see cref="CameraMetadata.LongRange"/></term>
        ///         <description>android.util.Range&lt;Long&gt;</description>
        ///     </item>
        ///     <item>
        ///         <term><see cref="CameraMetadata.FloatRange"/></term>
        ///         <description>android.util.Range&lt;Float&gt;</description>
        ///     </item>
        /// </list>
        ///
        /// Also supports converting arrays of all above types + generic AndroidJavaObject[].
        /// </remarks>
        /// <param name="target">The managed target type.</param>
        /// <returns>The converted managed object.</returns>
        /// <exception cref="NotSupportedException">Thrown when the requested target type is unsupported.</exception>
        [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "Code is neater without conditionals.")]
        public static object Convert(this AndroidJavaObject current, Type target)
        {
            if (target.IsPrimitive)
            {
                return UnboxPrimitive(current, target);
            }

            if (target == typeof(string))
            {
                return current.Call<string>("toString");
            }

            if (target.IsArray)
            {
                Type elementType = target.GetElementType();
                if (elementType.IsPrimitive
                 || elementType == typeof(string)
                 || elementType == typeof(AndroidJavaObject))
                {
                    if (elementType == typeof(byte))
                        throw new NotSupportedException("Cannot unbox byte[] from Java/Kotlin. Use sbyte[] instead.");

                    return HandleBasicArrays(current, elementType);
                }

                return HandleCustomArrays(current, elementType);
            }

            return HandleCustomTypes(current, target);
        }

        private static Array HandleCustomArrays(AndroidJavaObject current, Type elementType)
        {
            IntPtr nativeObj = current.GetRawObject();
            int length = AndroidJNI.GetArrayLength(nativeObj);

            Array result = Array.CreateInstance(elementType, length);
            for (int i = 0; i < length; i++)
            {
                using AndroidJavaObject element = UnboxObjectElement(nativeObj, i);
                object converted = Convert(element, elementType);
                result.SetValue(converted, i);
            }

            return result;
        }
        
        [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "Code is neater without conditionals.")]
        private static object HandleCustomTypes(AndroidJavaObject current, Type target)
        {
            if (target == typeof(Resolution))
            {
                return new Resolution()
                {
                    width   = current.Call<int>("getWidth"),
                    height  = current.Call<int>("getHeight")
                };
            }

            if (target == typeof(RectInt))
            {
                return new RectInt(
                    xMin:   current.Get<int>("left"),
                    yMin:   current.Get<int>("top"),
                    width:  current.Call<int>("width"),
                    height: current.Call<int>("height")
                );
            }

            if (target == typeof(Rect))
            {
                return new Rect(
                    x:      current.Get<float>("left"),
                    y:      current.Get<float>("top"),
                    width:  current.Call<float>("width"),
                    height: current.Call<float>("height")
                );
            }

            if (target == typeof(CameraMetadata.IntRange))
            {
                return new CameraMetadata.IntRange(
                    Lower: current.Call<int>("getLower"),
                    Upper: current.Call<int>("getUpper")
                );
            }

            if (target == typeof(CameraMetadata.LongRange))
            {
                return new CameraMetadata.LongRange(
                    Lower: current.Call<long>("getLower"),
                    Upper: current.Call<long>("getUpper")
                );
            }

            if (target == typeof(CameraMetadata.FloatRange))
            {
                return new CameraMetadata.FloatRange(
                    Lower: current.Call<float>("getLower"),
                    Upper: current.Call<float>("getUpper")
                );
            }

            throw new NotSupportedException($"Type '{target.Name}' is not supported.");
        }

        [SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "Code is neater without conditionals.")]
        private static object HandleBasicArrays(AndroidJavaObject current, Type elementType)
        {
            IntPtr nativeObj = current.GetRawObject();

            if (elementType == typeof(bool))
            {
                return AndroidJNIHelper.ConvertFromJNIArray<bool[]>(nativeObj);
            }

            if (elementType == typeof(char))
            {
                return AndroidJNIHelper.ConvertFromJNIArray<char[]>(nativeObj);
            }

            if (elementType == typeof(sbyte))
            {
                return AndroidJNIHelper.ConvertFromJNIArray<sbyte[]>(nativeObj);
            }

            if (elementType == typeof(byte))
            {
                throw new NotSupportedException("Cannot unbox byte[] from Java/Kotlin. Use sbyte[] instead.");
            }

            if (elementType == typeof(short))
            {
                return AndroidJNIHelper.ConvertFromJNIArray<short[]>(nativeObj);
            }

            if (elementType == typeof(int))
            {
                return AndroidJNIHelper.ConvertFromJNIArray<int[]>(nativeObj);
            }

            if (elementType == typeof(long))
            {
                return AndroidJNIHelper.ConvertFromJNIArray<long[]>(nativeObj);
            }

            if (elementType == typeof(float))
            {
                return AndroidJNIHelper.ConvertFromJNIArray<float[]>(nativeObj);
            }

            if (elementType == typeof(double))
            {
                return AndroidJNIHelper.ConvertFromJNIArray<double[]>(nativeObj);
            }

            if (elementType == typeof(string))
            {
                return AndroidJNIHelper.ConvertFromJNIArray<string[]>(nativeObj);
            }

            if (elementType == typeof(AndroidJavaObject))
            {
                return AndroidJNIHelper.ConvertFromJNIArray<AndroidJavaObject[]>(nativeObj);
            }

            throw new NotSupportedException($"Array with elements of type '{elementType.Name}' is not supported.");
        }

        private static object UnboxPrimitive(AndroidJavaObject current, Type target)
        {
            IntPtr nativeObj = current.GetRawObject();

            if (target == typeof(bool))
            {
                AndroidJNIHelper.Unbox(nativeObj, out bool value);
                return value;
            }

            if (target == typeof(char))
            {
                AndroidJNIHelper.Unbox(nativeObj, out char value);
                return value;
            }

            if (target == typeof(sbyte))
            {
                AndroidJNIHelper.Unbox(nativeObj, out sbyte value);
                return value;
            }

            if (target == typeof(byte))
            {
                throw new NotSupportedException("Cannot unbox byte from Java/Kotlin. Use sbyte instead.");
            }

            if (target == typeof(short))
            {
                AndroidJNIHelper.Unbox(nativeObj, out short value);
                return value;
            }

            if (target == typeof(int))
            {
                AndroidJNIHelper.Unbox(nativeObj, out int value);
                return value;
            }

            if (target == typeof(long))
            {
                AndroidJNIHelper.Unbox(nativeObj, out long value);
                return value;
            }

            if (target == typeof(float))
            {
                AndroidJNIHelper.Unbox(nativeObj, out float value);
                return value;
            }

            if (target == typeof(double))
            {
                AndroidJNIHelper.Unbox(nativeObj, out double value);
                return value;
            }

            throw new NotSupportedException($"Primitive '{target.Name}' is not supported.");
        }
    }
}
