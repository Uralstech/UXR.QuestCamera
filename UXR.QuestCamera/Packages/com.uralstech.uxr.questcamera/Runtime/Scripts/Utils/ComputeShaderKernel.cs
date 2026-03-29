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
using UnityEngine;

#nullable enable
namespace Uralstech.UXR.QuestCamera
{
    /// <summary>Utility class to contain ComputeShader kernel info.</summary>
    [Serializable] public sealed class ComputeShaderKernel
    {
        [Tooltip("The shader in which this kernel is located.")]
        [field: SerializeField] public ComputeShader Shader { get; private set; }
        
        [Tooltip("The name of this kernel.")]
        [field: SerializeField] public string Name { get; private set; }

        public int Index { get; private set; }
        public Vector3Int ThreadGroupSizes { get; private set; }

        public ComputeShaderKernel()
        {
            Shader = null!;
            Name = "CSMain";
        }

        public ComputeShaderKernel(ComputeShader shader, string kernel = "CSMain")
        {
            Shader = shader;
            Name = kernel;
        }

        public bool Validate()
        {
            if (Shader == null || !Shader.HasKernel(Name))
            {
                Debug.LogError($"Shader ({Shader}) is null or does not have a kernel with name '{Name}'.");
                return false;
            }

            Index = Shader.FindKernel(Name);

            Shader.GetKernelThreadGroupSizes(Index, out uint x, out uint y, out uint z);
            ThreadGroupSizes = new Vector3Int((int)x, (int)y, (int)z);
            return true;
        }
    }
}