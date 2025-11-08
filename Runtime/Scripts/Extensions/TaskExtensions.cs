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
using System.Threading.Tasks;
using UnityEngine;

#if !UNITY_6000_0_OR_NEWER
using Utilities.Async;
#endif

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>
    /// Extensions for <see cref="Action"/>.
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Adds a continuation to a task to log exceptions.
        /// </summary>
        public static void HandleAnyException(this Task current)
        {
            current.ContinueWith(static t =>
            {
                foreach (Exception ex in t.Exception.Flatten().InnerExceptions)
                    Debug.LogException(ex);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>
        /// Invokes the current action on the main thread.
        /// </summary>
        public static async Task InvokeOnMainThread(this Action? current)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#else
            await Awaiters.UnityMainThread;
#endif

            current?.Invoke();
        }

        /// <summary>
        /// Invokes the current action on the main thread.
        /// </summary>
        public static async Task InvokeOnMainThread<T>(this Action<T>? current, T arg0)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#else
            await Awaiters.UnityMainThread;
#endif

            current?.Invoke(arg0);
        }

        /// <summary>
        /// Invokes the current action on the main thread.
        /// </summary>
        public static async Task InvokeOnMainThread<T0, T1>(this Action<T0, T1>? current, T0 arg0, T1 arg1)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#else
            await Awaiters.UnityMainThread;
#endif

            current?.Invoke(arg0, arg1);
        }

        /// <summary>
        /// Allows for "yielding" a <see cref="ValueTask"/> using a <see cref="WaitUntil"/> object.
        /// </summary>
        public static WaitUntil Yield(this ValueTask current) => new(() => current.IsCompleted);

        /// <summary>
        /// Allows for "yielding" a <see cref="Task"/> using a <see cref="WaitUntil"/> object.
        /// </summary>
        public static WaitUntil Yield(this Task current) => new(() => current.IsCompleted);
    }
}
