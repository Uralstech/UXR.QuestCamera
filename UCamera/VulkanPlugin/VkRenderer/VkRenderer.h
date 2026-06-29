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

#include <array>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"

// DON'T link to vulkan
#define VK_NO_PROTOTYPES
#include "IUnityGraphicsVulkan.h"
#include <vulkan/vulkan_core.h>
#include <vulkan/vulkan_android.h>

#define USED_VULKAN_FUNCTIONS(apply)                \
    apply(vkCmdClearColorImage);                    \
    apply(vkCreateDevice);                          \
    apply(vkEnumerateDeviceExtensionProperties);    \
    apply(vkGetPhysicalDeviceFeatures2);

#define EVENT_ID_RENDER 1

struct RenderData {
    VkImage* image;
    float r,g,b;
};

class VkRenderer {

public:
    static PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API
        hookVulkanInitialization(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void*);

    static bool isVulkanSetup();

    VkRenderer(IUnityGraphicsVulkan* unityVulkan);

    void onDeviceInitialized();
    void onDeviceShutdown();

    void render(RenderData* data);

private:
#define DEFINE_VULKAN_FUNCTIONPTR(func) static PFN_##func func
    DEFINE_VULKAN_FUNCTIONPTR(vkGetInstanceProcAddr);
    USED_VULKAN_FUNCTIONS(DEFINE_VULKAN_FUNCTIONPTR);
#undef DEFINE_VULKAN_FUNCTIONPTR

    static constexpr const char* TAG = "UXRQC.VkRenderer";
    static void LogI(const char* msg, ...);
    static void LogD(const char* msg, ...);
    static void LogE(const char* msg, ...);

    static constexpr uint32_t minVulkan = VK_VERSION_1_1;
    static bool canContinueWithVulkanInstance;
    static bool canContinueWithVulkanDevice;

    static constexpr std::array<const char*, 5> reqDeviceExtensions = {
            VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME,
            VK_KHR_EXTERNAL_MEMORY_EXTENSION_NAME,
            VK_KHR_DEDICATED_ALLOCATION_EXTENSION_NAME,
            VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME,
            VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME
    };

    static VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL
        Hook_vkGetInstanceProcAddr(VkInstance device, const char* funcName);

    static VKAPI_ATTR VkResult VKAPI_CALL
        Hook_vkCreateInstance(const VkInstanceCreateInfo* pCreateInfo, const VkAllocationCallbacks* pAllocator, VkInstance* pInstance);

    static VKAPI_ATTR VkResult VKAPI_CALL
        Hook_vkCreateDevice(VkPhysicalDevice physicalDevice, const VkDeviceCreateInfo* pCreateInfo, const VkAllocationCallbacks* pAllocator, VkDevice* pDevice);

    static void loadVulkanFunctions(PFN_vkGetInstanceProcAddr getInstanceProcAddr, VkInstance instance);
    static bool tryGetDeviceSupportedExtensions(VkPhysicalDevice device, std::vector<VkExtensionProperties>* result);

    IUnityGraphicsVulkan* unityVulkan;
};


#endif //UXR_QUESTCAMERA_VKRENDERER_H
