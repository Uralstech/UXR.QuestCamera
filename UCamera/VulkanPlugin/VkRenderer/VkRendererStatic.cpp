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
#include <vector>

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
USED_VULKAN_FUNCTIONS(INIT_VULKAN_FUNCTIONPTR);
#undef INIT_VULKAN_FUNCTIONPTR

bool VkRenderer::canContinueWithVulkanInstance = false;
bool VkRenderer::canContinueWithVulkanDevice = false;

bool VkRenderer::isVulkanSetup() {
    return canContinueWithVulkanInstance && canContinueWithVulkanDevice;
}

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
    INTERCEPT(vkCreateDevice);

#undef INTERCEPT

    return vkGetInstanceProcAddr(device, funcName);
}

VKAPI_ATTR VkResult VKAPI_CALL
    VkRenderer::Hook_vkCreateInstance(const VkInstanceCreateInfo* pCreateInfo,
                                      const VkAllocationCallbacks* pAllocator,
                                      VkInstance* pInstance) {

    bool isSupportedVulkan = true;
    auto vkCreateInstance = (PFN_vkCreateInstance)vkGetInstanceProcAddr(VK_NULL_HANDLE, "vkCreateInstance");

    if (!pCreateInfo->pApplicationInfo || pCreateInfo->pApplicationInfo->apiVersion < minVulkan) {
        LogE("Unknown/unsupported Vulkan API version, cannot continue.");
        canContinueWithVulkanInstance = isSupportedVulkan = false;
    }

    VkResult result = vkCreateInstance(pCreateInfo, pAllocator, pInstance);
    if (result == VK_SUCCESS && isSupportedVulkan) {
        loadVulkanFunctions(vkGetInstanceProcAddr, *pInstance);
        canContinueWithVulkanInstance = true;
        LogD("Vulkan instance created.");
    }

    return result;
}

static bool vecContains(const std::vector<const char*>& vec, const char* val) {
    for (const char* i : vec)
        if (strcmp(i, val) == 0)
            return true;

    return false;
}

static bool vecContains(const std::vector<VkExtensionProperties>& vec, const char* extName) {
    for (const VkExtensionProperties& i : vec)
        if (strcmp(i.extensionName, extName) == 0)
            return true;

    return false;
}

static const VkBaseInStructure* getLastNode(const VkDeviceCreateInfo* createInfo) {

    auto* result = reinterpret_cast<const VkBaseInStructure*>(createInfo);
    while (result->pNext) {
        result = result->pNext;
    }

    return result;
}

static const VkPhysicalDeviceVulkan11Features* tryGetVulkan11Features(const VkDeviceCreateInfo* createInfo) {

    for (const auto* data = reinterpret_cast<const VkBaseInStructure*>(createInfo->pNext);
         data != nullptr; data = data->pNext) {

        if (data->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES) {
            return reinterpret_cast<const VkPhysicalDeviceVulkan11Features*>(data);
        }
    }

    return nullptr;
}

static const VkPhysicalDeviceSamplerYcbcrConversionFeatures* tryGetYuvFeatures(const VkDeviceCreateInfo* createInfo) {

    for (const auto* data = reinterpret_cast<const VkBaseInStructure*>(createInfo->pNext);
         data != nullptr; data = data->pNext) {

        if (data->sType == VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES) {
            return reinterpret_cast<const VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(data);
        }
    }

    return nullptr;
}

VKAPI_ATTR VkResult VKAPI_CALL
    VkRenderer::Hook_vkCreateDevice(VkPhysicalDevice physicalDevice,
                                    const VkDeviceCreateInfo* pCreateInfo,
                                    const VkAllocationCallbacks* pAllocator, VkDevice* pDevice) {

    if (!canContinueWithVulkanInstance) {
        LogD("Ignoring device extension registration as Vulkan instance is plugin-unusable.");
        canContinueWithVulkanDevice = false;

        return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
    }

    std::vector<const char*> extensions;
    extensions.reserve(pCreateInfo->enabledExtensionCount + reqDeviceExtensions.size());
    if (pCreateInfo->enabledExtensionCount > 0) {
        extensions.insert(extensions.end(),
                          pCreateInfo->ppEnabledExtensionNames,
                          pCreateInfo->ppEnabledExtensionNames + pCreateInfo->enabledExtensionCount);
    }

    std::vector<VkExtensionProperties> deviceExtensions;
    for (const char* extName : reqDeviceExtensions) {
        if (vecContains(extensions, extName)) {
            continue;
        }

        if (deviceExtensions.empty()
            && !tryGetDeviceSupportedExtensions(physicalDevice, &deviceExtensions)) {
            canContinueWithVulkanDevice = false;
            return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
        }

        if (!vecContains(deviceExtensions, extName)) {
            LogE("Device does not support extension '%s', cannot continue.", extName);
            canContinueWithVulkanDevice = false;
            return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
        }

        extensions.push_back(extName);
    }

    VkDeviceCreateInfo createInfoOverride = *pCreateInfo;
    createInfoOverride.enabledExtensionCount = static_cast<uint32_t>(extensions.size());
    createInfoOverride.ppEnabledExtensionNames = extensions.data();

    const VkPhysicalDeviceSamplerYcbcrConversionFeatures* yuvFeaturesPtr
        = tryGetYuvFeatures(&createInfoOverride);

    const VkPhysicalDeviceVulkan11Features* vulkan11FeaturesPtr
        = tryGetVulkan11Features(&createInfoOverride);

    VkBaseInStructure* modifiedLastNode = nullptr;
    VkPhysicalDeviceSamplerYcbcrConversionFeatures yuvFeaturesOverride = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES,
            .pNext = nullptr,
            .samplerYcbcrConversion = VK_TRUE,
    };

    if ((!vulkan11FeaturesPtr && !yuvFeaturesPtr)
        || (vulkan11FeaturesPtr && vulkan11FeaturesPtr->samplerYcbcrConversion != VK_TRUE)
        || (yuvFeaturesPtr && yuvFeaturesPtr->samplerYcbcrConversion != VK_TRUE)) {

        VkPhysicalDeviceSamplerYcbcrConversionFeatures yuvFeaturesOut = {
                .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES,
                .pNext = nullptr,
        };

        VkPhysicalDeviceFeatures2 features2 = {
                .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,
                .pNext = &yuvFeaturesOut,
                .features = VkPhysicalDeviceFeatures { },
        };

        vkGetPhysicalDeviceFeatures2(physicalDevice, &features2);
        if (yuvFeaturesOut.samplerYcbcrConversion != VK_TRUE) {
            LogE("Device does not support YUV sampler, cannot continue.");
            canContinueWithVulkanDevice = false;
            return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
        }

        if (vulkan11FeaturesPtr) {
            const_cast<VkPhysicalDeviceVulkan11Features*>(vulkan11FeaturesPtr)->samplerYcbcrConversion = VK_TRUE;
        } else if (yuvFeaturesPtr) {
            const_cast<VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(yuvFeaturesPtr)->samplerYcbcrConversion = VK_TRUE;
        } else {
            auto lastNode = getLastNode(&createInfoOverride);
            modifiedLastNode = const_cast<VkBaseInStructure*>(lastNode);

            modifiedLastNode->pNext = reinterpret_cast<const VkBaseInStructure*>(&yuvFeaturesOverride);
        }
    }

    VkResult result = vkCreateDevice(physicalDevice, &createInfoOverride, pAllocator, pDevice);
    if (modifiedLastNode != nullptr) {
        modifiedLastNode->pNext = nullptr;
    }

    if (result == VK_SUCCESS) {
        canContinueWithVulkanDevice = true;
        LogD("Vulkan device created.");
    }

    return result;
}

bool
    VkRenderer::tryGetDeviceSupportedExtensions(VkPhysicalDevice device, std::vector<VkExtensionProperties>* result) {

    uint32_t count = 0;
    result->clear();

    VkResult apiResult = vkEnumerateDeviceExtensionProperties(device, nullptr, &count, nullptr);
    if (apiResult != VK_SUCCESS) {
        LogE("Could not get device extension properties (A), cannot continue.");
        return false;
    }

    result->resize(count);
    apiResult = vkEnumerateDeviceExtensionProperties(device, nullptr, &count, result->data());
    if (apiResult != VK_SUCCESS) {
        LogE("Could not get device extension properties (B), cannot continue.");
        return false;
    }

    return true;
}

void
    VkRenderer::loadVulkanFunctions(PFN_vkGetInstanceProcAddr getInstanceProcAddr, VkInstance instance) {

    if (!vkGetInstanceProcAddr && getInstanceProcAddr) {
        vkGetInstanceProcAddr = getInstanceProcAddr;
    }

#define LOAD_VULKAN_FUNCTION(func) if (!func) func = (PFN_##func)vkGetInstanceProcAddr(instance, #func)
    USED_VULKAN_FUNCTIONS(LOAD_VULKAN_FUNCTION);
#undef LOAD_VULKAN_FUNCTION
}