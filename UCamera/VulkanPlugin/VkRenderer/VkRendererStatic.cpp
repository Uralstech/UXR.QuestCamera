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

#include "VkRenderer.h"
#include <android/log.h>
#include <cstdarg>
#include <cstring>

void VkRenderer::LogI(const char* msg, ...)
{
    va_list args;
    va_start(args, msg);
    __android_log_vprint(ANDROID_LOG_INFO, TAG, msg, args);
    va_end(args);
}

void VkRenderer::LogD(const char* msg, ...)
{
    va_list args;
    va_start(args, msg);
    __android_log_vprint(ANDROID_LOG_DEBUG, TAG, msg, args);
    va_end(args);
}

void VkRenderer::LogE(const char* msg, ...)
{
    va_list args;
    va_start(args, msg);
    __android_log_vprint(ANDROID_LOG_ERROR, TAG, msg, args);
    va_end(args);
}

#define INIT_VULKAN_FUNCTIONPTR(func) PFN_##func VkRenderer::func = nullptr
INIT_VULKAN_FUNCTIONPTR(vkGetInstanceProcAddr);
INIT_VULKAN_FUNCTIONPTR(vkCreateInstance);
USED_VULKAN_FUNCTIONS(INIT_VULKAN_FUNCTIONPTR);
#undef INIT_VULKAN_FUNCTIONPTR

PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API
    VkRenderer::hookVulkanInitialization(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void *) {

    vkGetInstanceProcAddr = getInstanceProcAddr;
    return Hook_vkGetInstanceProcAddr;
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL
    VkRenderer::Hook_vkGetInstanceProcAddr(VkInstance device, const char* funcName) {

    if (!funcName)
        return nullptr;

#define INTERCEPT(func) if (strcmp(funcName, #func) == 0) return (PFN_vkVoidFunction)&Hook_##func

    INTERCEPT(vkCreateInstance);

#undef INTERCEPT

    return vkGetInstanceProcAddr(device, funcName);
}

VKAPI_ATTR VkResult VKAPI_CALL
    VkRenderer::Hook_vkCreateInstance(const VkInstanceCreateInfo* pCreateInfo,
                                      const VkAllocationCallbacks* pAllocator,
                                      VkInstance* pInstance) {

    auto extCount = pCreateInfo->enabledExtensionCount;
    auto extPtr = pCreateInfo->ppEnabledExtensionNames;
    for (uint32_t i = 0; i < extCount; i++) {
        LogD("Enabled extension: %s", extPtr[i]);
    }

    if (const auto* appInfo = pCreateInfo->pApplicationInfo) {
        LogD("Vulkan version: %u.%u",
             VK_API_VERSION_MAJOR(appInfo->apiVersion),
             VK_API_VERSION_MINOR(appInfo->apiVersion));
    }

    vkCreateInstance = (PFN_vkCreateInstance)vkGetInstanceProcAddr(VK_NULL_HANDLE, "vkCreateInstance");
    VkResult result = vkCreateInstance(pCreateInfo, pAllocator, pInstance);
    if (result == VK_SUCCESS) {
        loadVulkanFunctions(vkGetInstanceProcAddr, *pInstance);
    }

    return result;
}

void
    VkRenderer::loadVulkanFunctions(PFN_vkGetInstanceProcAddr getInstanceProcAddr, VkInstance instance) {

    if (!vkGetInstanceProcAddr && getInstanceProcAddr) {
        vkGetInstanceProcAddr = getInstanceProcAddr;
    }

    if (!vkCreateInstance) {
        vkCreateInstance = (PFN_vkCreateInstance)vkGetInstanceProcAddr(VK_NULL_HANDLE, "vkCreateInstance");
    }

#define LOAD_VULKAN_FUNCTION(func) if (!func) func = (PFN_##func)vkGetInstanceProcAddr(instance, #func)
    USED_VULKAN_FUNCTIONS(LOAD_VULKAN_FUNCTION);
#undef LOAD_VULKAN_FUNCTION
}