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

#include "VulkanRenderManager.h"

#include <cstdarg>
#include <cstring>
#include <vector>
#include <android/log.h>

#define INIT_VULKAN_FUNCTIONPTR(func) PFN_##func VulkanRenderManager::func = nullptr
INIT_VULKAN_FUNCTIONPTR(vkGetInstanceProcAddr);
USED_VULKAN_FUNCTIONS(INIT_VULKAN_FUNCTIONPTR)
#undef INIT_VULKAN_FUNCTIONPTR

bool VulkanRenderManager::isVulkanInstanceSupported = false;
bool VulkanRenderManager::isVulkanDeviceSupported   = false;

void VulkanRenderManager::LogI(const char* msg, ...)
{
    va_list args;
    va_start(args, msg);
    __android_log_vprint(ANDROID_LOG_INFO, TAG, msg, args);
    va_end(args);
}

void VulkanRenderManager::LogD(const char* msg, ...)
{
    va_list args;
    va_start(args, msg);
    __android_log_vprint(ANDROID_LOG_DEBUG, TAG, msg, args);
    va_end(args);
}

void VulkanRenderManager::LogE(const char* msg, ...)
{
    va_list args;
    va_start(args, msg);
    __android_log_vprint(ANDROID_LOG_ERROR, TAG, msg, args);
    va_end(args);
}

bool VulkanRenderManager::isRuntimeSupported() {
    return isVulkanInstanceSupported && isVulkanDeviceSupported;
}

PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API
    VulkanRenderManager::hookVulkanInitialization(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void *) {

    vkGetInstanceProcAddr = getInstanceProcAddr;
    return Hook_vkGetInstanceProcAddr;
}

VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL
    VulkanRenderManager::Hook_vkGetInstanceProcAddr(VkInstance device, const char* funcName) {

    if (!funcName)
        return nullptr;

#define INTERCEPT(func) if (strcmp(funcName, #func) == 0) return (PFN_vkVoidFunction)&Hook_##func

    INTERCEPT(vkCreateInstance);
    INTERCEPT(vkCreateDevice);

#undef INTERCEPT

    return vkGetInstanceProcAddr(device, funcName);
}

VKAPI_ATTR VkResult VKAPI_CALL
    VulkanRenderManager::Hook_vkCreateInstance(const VkInstanceCreateInfo* pCreateInfo,
                                               const VkAllocationCallbacks* pAllocator,
                                               VkInstance* pInstance) {

    bool shouldLoadFunctions = true;
    if (!pCreateInfo->pApplicationInfo || pCreateInfo->pApplicationInfo->apiVersion < minimumVulkanAPI) {
        LogE("Unknown/unsupported Vulkan API version, cannot continue.");
        shouldLoadFunctions = isVulkanInstanceSupported = false;
    }

    auto vkCreateInstance = (PFN_vkCreateInstance)vkGetInstanceProcAddr(VK_NULL_HANDLE,
                                                                        "vkCreateInstance");
    VkResult result = vkCreateInstance(pCreateInfo, pAllocator, pInstance);

    if (result == VK_SUCCESS && shouldLoadFunctions) {
        loadVulkanFunctions(vkGetInstanceProcAddr, *pInstance);
        isVulkanInstanceSupported = true;

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

template<typename T, VkStructureType structureType>
static const T* tryGetLinkedStructure(const VkDeviceCreateInfo* createInfo) {

    for (const auto* data = reinterpret_cast<const VkBaseInStructure*>(createInfo->pNext);
         data != nullptr; data = data->pNext) {

        if (data->sType == structureType) {
            return reinterpret_cast<const T*>(data);
        }
    }

    return nullptr;
}

VKAPI_ATTR VkResult VKAPI_CALL
    VulkanRenderManager::Hook_vkCreateDevice(VkPhysicalDevice physicalDevice,
                                             const VkDeviceCreateInfo* pCreateInfo,
                                             const VkAllocationCallbacks* pAllocator, VkDevice* pDevice) {

    if (!isVulkanInstanceSupported) {
        LogD("Ignoring device extension registration as Vulkan instance is plugin-unusable.");
        isVulkanDeviceSupported = false;

        return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
    }

    std::vector<const char*> extensions;
    extensions.reserve(pCreateInfo->enabledExtensionCount + reqDeviceExtensions.size());

    if (pCreateInfo->enabledExtensionCount > 0) {
        extensions.insert(extensions.end(),
                          pCreateInfo->ppEnabledExtensionNames,
                          pCreateInfo->ppEnabledExtensionNames + pCreateInfo->enabledExtensionCount);
    }

    std::vector<VkExtensionProperties> supportedExtensions;
    for (const char* extName : reqDeviceExtensions) {
        if (vecContains(extensions, extName)) {
            continue;
        }

        if (supportedExtensions.empty()
            && !tryGetDeviceSupportedExtensions(physicalDevice, &supportedExtensions)) {
            isVulkanDeviceSupported = false;

            return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
        }

        if (!vecContains(supportedExtensions, extName)) {
            LogE("Device does not support extension '%s', cannot continue.", extName);
            isVulkanDeviceSupported = false;

            return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
        }

        extensions.push_back(extName);
    }

    VkDeviceCreateInfo createInfoOverride = *pCreateInfo;
    createInfoOverride.enabledExtensionCount = static_cast<uint32_t>(extensions.size());
    createInfoOverride.ppEnabledExtensionNames = extensions.data();

    const VkPhysicalDeviceSamplerYcbcrConversionFeatures* yuvFeaturesPtr
            = tryGetLinkedStructure<VkPhysicalDeviceSamplerYcbcrConversionFeatures,
                VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES>(&createInfoOverride);

    const VkPhysicalDeviceVulkan11Features* vulkan11FeaturesPtr
            = tryGetLinkedStructure<VkPhysicalDeviceVulkan11Features,
                VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES>(&createInfoOverride);

    const VkPhysicalDeviceMaintenance6FeaturesKHR* maintenance6FeaturesPtr
            = tryGetLinkedStructure<VkPhysicalDeviceMaintenance6FeaturesKHR,
                VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MAINTENANCE_6_FEATURES_KHR>(&createInfoOverride);

    const VkPhysicalDeviceVulkan13Features* vulkan13FeaturesPtr
            = tryGetLinkedStructure<VkPhysicalDeviceVulkan13Features,
                VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES>(&createInfoOverride);

    const VkPhysicalDeviceDynamicRenderingFeaturesKHR* dynamicRenderingFeaturesPtr
            = tryGetLinkedStructure<VkPhysicalDeviceDynamicRenderingFeaturesKHR,
                VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_FEATURES_KHR>(&createInfoOverride);

    const VkBaseInStructure* lastProvidedNode = getLastNode(&createInfoOverride);
    bool hasModifiedLastProvidedNode = false;
    VkBaseInStructure* newLastNode = nullptr;

    VkPhysicalDeviceSamplerYcbcrConversionFeatures yuvFeaturesOverride = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES,
            .pNext = nullptr,
            .samplerYcbcrConversion = VK_TRUE,
    };

    VkPhysicalDeviceMaintenance6FeaturesKHR maintenance6FeaturesOverride = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MAINTENANCE_6_FEATURES_KHR,
            .pNext = nullptr,
            .maintenance6 = VK_TRUE
    };

    VkPhysicalDeviceDynamicRenderingFeaturesKHR dynamicRenderingFeaturesOverride = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_FEATURES_KHR,
            .pNext = nullptr,
            .dynamicRendering = VK_TRUE
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
                .pNext = &yuvFeaturesOut
        };

        vkGetPhysicalDeviceFeatures2(physicalDevice, &features2);
        if (yuvFeaturesOut.samplerYcbcrConversion != VK_TRUE) {
            LogE("Device does not support YUV sampler, cannot continue.");
            isVulkanDeviceSupported = false;

            return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
        }

        if (vulkan11FeaturesPtr) {
            const_cast<VkPhysicalDeviceVulkan11Features*>(vulkan11FeaturesPtr)->samplerYcbcrConversion = VK_TRUE;
        } else if (yuvFeaturesPtr) {
            const_cast<VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(yuvFeaturesPtr)->samplerYcbcrConversion = VK_TRUE;
        } else {
            const_cast<VkBaseInStructure*>(lastProvidedNode)->pNext = newLastNode = reinterpret_cast<VkBaseInStructure*>(&yuvFeaturesOverride);
            hasModifiedLastProvidedNode = true;
        }
    }

    if (!maintenance6FeaturesPtr || maintenance6FeaturesPtr->maintenance6 != VK_TRUE) {

        VkPhysicalDeviceMaintenance6FeaturesKHR maintenance6FeaturesOut = {
                .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MAINTENANCE_6_FEATURES_KHR,
                .pNext = nullptr,
        };

        VkPhysicalDeviceFeatures2 features2 = {
                .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,
                .pNext = &maintenance6FeaturesOut
        };

        vkGetPhysicalDeviceFeatures2(physicalDevice, &features2);
        if (maintenance6FeaturesOut.maintenance6 != VK_TRUE) {
            LogE("Device does not support maintenance6 extension, cannot continue.");
            isVulkanDeviceSupported = false;

            return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
        }

        if (maintenance6FeaturesPtr) {
            const_cast<VkPhysicalDeviceMaintenance6FeaturesKHR*>(maintenance6FeaturesPtr)->maintenance6 = VK_TRUE;
        } else if (newLastNode) {
            VkBaseInStructure* ptr = reinterpret_cast<VkBaseInStructure*>(&maintenance6FeaturesOverride);
            newLastNode->pNext = ptr;
            newLastNode = ptr;
        } else {
            const_cast<VkBaseInStructure*>(lastProvidedNode)->pNext = newLastNode = reinterpret_cast<VkBaseInStructure*>(&maintenance6FeaturesOverride);
            hasModifiedLastProvidedNode = true;
        }
    }

    if ((!vulkan13FeaturesPtr && !dynamicRenderingFeaturesPtr)
        || (vulkan13FeaturesPtr && vulkan13FeaturesPtr->dynamicRendering != VK_TRUE)
        || (dynamicRenderingFeaturesPtr && dynamicRenderingFeaturesPtr->dynamicRendering != VK_TRUE)) {

        VkPhysicalDeviceDynamicRenderingFeaturesKHR dynamicRenderingFeaturesOut = {
                .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_FEATURES_KHR,
                .pNext = nullptr,
        };

        VkPhysicalDeviceFeatures2 features2 = {
                .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,
                .pNext = &dynamicRenderingFeaturesOut
        };

        vkGetPhysicalDeviceFeatures2(physicalDevice, &features2);
        if (dynamicRenderingFeaturesOut.dynamicRendering != VK_TRUE) {
            LogE("Device does not support dynamic rendering, cannot continue.");
            isVulkanDeviceSupported = false;

            return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
        }

        if (vulkan13FeaturesPtr) {
            const_cast<VkPhysicalDeviceVulkan13Features*>(vulkan13FeaturesPtr)->dynamicRendering = VK_TRUE;
        } else if (dynamicRenderingFeaturesPtr) {
            const_cast<VkPhysicalDeviceDynamicRenderingFeaturesKHR *>(dynamicRenderingFeaturesPtr)->dynamicRendering = VK_TRUE;
        } else if (newLastNode) {
            VkBaseInStructure* ptr = reinterpret_cast<VkBaseInStructure*>(&dynamicRenderingFeaturesOverride);
            newLastNode->pNext = ptr;
            newLastNode = ptr;
        } else {
            const_cast<VkBaseInStructure*>(lastProvidedNode)->pNext = newLastNode = reinterpret_cast<VkBaseInStructure*>(&dynamicRenderingFeaturesOverride);
            hasModifiedLastProvidedNode = true;
        }
    }

    VkResult result = vkCreateDevice(physicalDevice, &createInfoOverride, pAllocator, pDevice);
    if (hasModifiedLastProvidedNode) {
        const_cast<VkBaseInStructure*>(lastProvidedNode)->pNext = nullptr;
    }

    if (result == VK_SUCCESS) {
        isVulkanDeviceSupported = true;
        LogD("Vulkan device created.");
    }

    return result;
}

bool VulkanRenderManager::tryGetDeviceSupportedExtensions(VkPhysicalDevice device,
                                                          std::vector<VkExtensionProperties>* result) {

    uint32_t count = 0;
    result->clear();

    VkResult vkResult = vkEnumerateDeviceExtensionProperties(device, nullptr,
                                                             &count, nullptr);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not get device extension property count due to error, code: %d.", vkResult);
        return false;
    }

    result->resize(count);
    vkResult = vkEnumerateDeviceExtensionProperties(device, nullptr,
                                                    &count, result->data());

    if (vkResult != VK_SUCCESS) {
        LogE("Could not get device extension properties due to error, code: %d.", vkResult);
        return false;
    }

    return true;
}

void VulkanRenderManager::loadVulkanFunctions(PFN_vkGetInstanceProcAddr getInstanceProcAddr,
                                              VkInstance instance) {

    if (!vkGetInstanceProcAddr && getInstanceProcAddr) {
        vkGetInstanceProcAddr = getInstanceProcAddr;
    }

#define LOAD_VULKAN_FUNCTION(func) if (!func) func = (PFN_##func)vkGetInstanceProcAddr(instance, #func)
    USED_VULKAN_FUNCTIONS(LOAD_VULKAN_FUNCTION)
#undef LOAD_VULKAN_FUNCTION
}