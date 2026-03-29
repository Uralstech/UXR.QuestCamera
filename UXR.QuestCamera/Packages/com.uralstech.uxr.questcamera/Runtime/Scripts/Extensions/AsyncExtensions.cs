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
using System.Threading.Tasks;
using UnityEngine;

#if !UNITY_6000_0_OR_NEWER
using Utilities.Async;
#endif

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    internal static class AsyncExtensions
    {
        public static void Forget(this Task task)
        {
            task.ContinueWith(static t =>
            {
                foreach (Exception ex in t.Exception.Flatten().InnerExceptions)
                    Debug.LogException(ex);
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        public static async Task OnMainThread(this Action action)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#else
            await Awaiters.UnityMainThread;
#endif

            action.Invoke();
        }

        public static async Task OnMainThread<T>(this Action<T> action, T arg)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#else
            await Awaiters.UnityMainThread;
#endif

            action.Invoke(arg);
        }

        public static async Task OnMainThread<T1, T2>(this Action<T1, T2> action, T1 arg0, T2 arg1)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.MainThreadAsync();
#else
            await Awaiters.UnityMainThread;
#endif

            action.Invoke(arg0, arg1);
        }
    }
}
