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

#ifndef UXR_QUESTCAMERA_VKRENDERER_H
#define UXR_QUESTCAMERA_VKRENDERER_H

#include "IUnityInterface.h"
#include "IUnityGraphics.h"

// DON'T link to vulkan
#define VK_NO_PROTOTYPES
#include "IUnityGraphicsVulkan.h"
#include <vulkan/vulkan_core.h>

#define USED_VULKAN_FUNCTIONS(apply)

#define EVENT_ID_RENDER 1

class VkRenderer {

public:
    static PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API
        hookVulkanInitialization(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void*);

    VkRenderer(IUnityGraphicsVulkanV2* unityVkV2, IUnityGraphicsVulkan* unityVkV1);

    void onDeviceInitialized();
    void onDeviceShutdown();

    void render(void* data);

private:
#define DEFINE_VULKAN_FUNCTIONPTR(func) static PFN_##func func
    DEFINE_VULKAN_FUNCTIONPTR(vkGetInstanceProcAddr);
    DEFINE_VULKAN_FUNCTIONPTR(vkCreateInstance);
    USED_VULKAN_FUNCTIONS(DEFINE_VULKAN_FUNCTIONPTR);
#undef DEFINE_VULKAN_FUNCTIONPTR

    static constexpr const char* TAG = "UXRQC.VkRenderer";
    static void LogI(const char* msg, ...);
    static void LogD(const char* msg, ...);
    static void LogE(const char* msg, ...);

    static VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL
        Hook_vkGetInstanceProcAddr(VkInstance device, const char* funcName);

    static VKAPI_ATTR VkResult VKAPI_CALL
        Hook_vkCreateInstance(const VkInstanceCreateInfo* pCreateInfo, const VkAllocationCallbacks* pAllocator, VkInstance* pInstance);

    static void loadVulkanFunctions(PFN_vkGetInstanceProcAddr getInstanceProcAddr, VkInstance instance);
};


#endif //UXR_QUESTCAMERA_VKRENDERER_H
