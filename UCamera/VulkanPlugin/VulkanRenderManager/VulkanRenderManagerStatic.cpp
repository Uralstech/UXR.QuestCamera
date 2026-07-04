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
        LogE("App targets unknown/unsupported Vulkan API version, cannot continue.");
        shouldLoadFunctions = isVulkanInstanceSupported = false;
    }

    auto vkEnumerateInstanceVersion = (PFN_vkEnumerateInstanceVersion)vkGetInstanceProcAddr(VK_NULL_HANDLE, "vkEnumerateInstanceVersion");
    if (vkEnumerateInstanceVersion == nullptr) {
        LogE("Current Vulkan API is version 1.0, cannot continue.");
        shouldLoadFunctions = isVulkanInstanceSupported = false;
    }

    auto vkCreateInstance = (PFN_vkCreateInstance)vkGetInstanceProcAddr(VK_NULL_HANDLE,
                                                                        "vkCreateInstance");
    VkResult vkResult = vkCreateInstance(pCreateInfo, pAllocator, pInstance);

    if (vkResult == VK_SUCCESS && shouldLoadFunctions) {
        loadVulkanFunctions(vkGetInstanceProcAddr, *pInstance);
        isVulkanInstanceSupported = true;

        LogD("Vulkan instance created.");
    }

    return vkResult;
}

static const VkBaseInStructure* getLastNode(const VkDeviceCreateInfo* createInfo) {

    auto result = reinterpret_cast<const VkBaseInStructure*>(createInfo);
    while (result->pNext) {
        result = result->pNext;
    }

    return result;
}

template<typename T, VkStructureType structureType>
static const T* tryGetLinkedStructure(const VkDeviceCreateInfo* createInfo) {

    for (auto data = reinterpret_cast<const VkBaseInStructure*>(createInfo->pNext);
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

    // region Feature Overrides

    VkPhysicalDeviceSamplerYcbcrConversionFeatures samplerYuvConversionFeaturesOverride = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES,
            .pNext = nullptr,
            .samplerYcbcrConversion = VK_TRUE,
    };

    VkPhysicalDeviceDynamicRenderingFeaturesKHR dynamicRenderingFeaturesOverride = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_FEATURES_KHR,
            .pNext = nullptr,
            .dynamicRendering = VK_TRUE
    };

    // endregion

    isVulkanDeviceSupported = false;
    if (!isVulkanInstanceSupported) {
        LogD("Ignoring device extension registration as Vulkan instance is plugin-unusable.");
        return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
    }

    if (!verifyDeviceExtensionSupport(physicalDevice)
        || !verifyDeviceExtensionFeatureSupport(physicalDevice)) {
        return vkCreateDevice(physicalDevice, pCreateInfo, pAllocator, pDevice);
    }

    std::vector<const char*> extensions = getDeduplicatedExtensions(pCreateInfo);

    VkDeviceCreateInfo createInfoOverride = *pCreateInfo;
    createInfoOverride.enabledExtensionCount = static_cast<uint32_t>(extensions.size());
    createInfoOverride.ppEnabledExtensionNames = extensions.data();

    const VkBaseInStructure* lastCallerProvidedNode = getLastNode(&createInfoOverride);
    bool hasModifiedLastCallerProvidedNode = false;
    VkBaseInStructure* newLastNode = nullptr;

    // region Vulkan 1.1 Feature Override Handling

    auto vulkan11FeaturesPtr
            = tryGetLinkedStructure<VkPhysicalDeviceVulkan11Features,
                    VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_1_FEATURES>(&createInfoOverride);

    if (vulkan11FeaturesPtr) {

        auto mutableCast = const_cast<VkPhysicalDeviceVulkan11Features*>(vulkan11FeaturesPtr);
        if (vulkan11FeaturesPtr->samplerYcbcrConversion != VK_TRUE) {
            mutableCast->samplerYcbcrConversion = VK_TRUE;
            LogD("Updated Vulkan11Features flag for samplerYcbcrConversion.");
        }
    } else {

        auto samplerYuvConversionFeaturesPtr
                = tryGetLinkedStructure<VkPhysicalDeviceSamplerYcbcrConversionFeatures,
                        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES>(&createInfoOverride);

        if (samplerYuvConversionFeaturesPtr) {

            auto mutableCast = const_cast<VkPhysicalDeviceSamplerYcbcrConversionFeatures*>(samplerYuvConversionFeaturesPtr);
            if (samplerYuvConversionFeaturesPtr->samplerYcbcrConversion != VK_TRUE) {
                mutableCast->samplerYcbcrConversion = VK_TRUE;
                LogD("Updated SamplerYcbcrConversionFeatures flag for samplerYcbcrConversion.");
            }
        } else {
            appendFeatureToCreateDeviceInfoChain(
                    newLastNode, lastCallerProvidedNode,
                    hasModifiedLastCallerProvidedNode,
                    &samplerYuvConversionFeaturesOverride,
                    "SamplerYcbcrConversionFeatures"
            );
        }
    }

    // endregion

    // region Vulkan 1.3 Feature Override Handling

    auto vulkan13FeaturesPtr
            = tryGetLinkedStructure<VkPhysicalDeviceVulkan13Features,
                    VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES>(&createInfoOverride);

    if (vulkan13FeaturesPtr) {

        auto mutableCast = const_cast<VkPhysicalDeviceVulkan13Features*>(vulkan13FeaturesPtr);
        if (vulkan13FeaturesPtr->dynamicRendering != VK_TRUE) {
            mutableCast->dynamicRendering = VK_TRUE;
            LogD("Updated Vulkan13Features flag for dynamicRendering.");
        }
    } else {

        auto dynamicRenderingFeaturesPtr
                = tryGetLinkedStructure<VkPhysicalDeviceDynamicRenderingFeaturesKHR,
                        VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_FEATURES_KHR>(&createInfoOverride);

        if (dynamicRenderingFeaturesPtr) {

            auto mutableCast = const_cast<VkPhysicalDeviceDynamicRenderingFeaturesKHR*>(dynamicRenderingFeaturesPtr);
            if (dynamicRenderingFeaturesPtr->dynamicRendering != VK_TRUE) {
                mutableCast->dynamicRendering = VK_TRUE;
                LogD("Updated DynamicRenderingFeaturesKHR flag for dynamicRendering.");
            }
        } else {
            appendFeatureToCreateDeviceInfoChain(
                    newLastNode, lastCallerProvidedNode,
                    hasModifiedLastCallerProvidedNode,
                    &dynamicRenderingFeaturesOverride,
                    "DynamicRenderingFeaturesKHR"
            );
        }
    }

    // endregion

    VkResult vkResult = vkCreateDevice(physicalDevice, &createInfoOverride, pAllocator, pDevice);
    if (hasModifiedLastCallerProvidedNode) {
        const_cast<VkBaseInStructure*>(lastCallerProvidedNode)->pNext = nullptr;
        LogD("Removed inserted structure(s) from features chain.");
    }

    if (vkResult == VK_SUCCESS) {
        isVulkanDeviceSupported = true;
        LogD("Vulkan device created.");
    }

    return vkResult;
}

bool VulkanRenderManager::verifyDeviceExtensionSupport(VkPhysicalDevice physicalDevice) {

    uint32_t extensionCount = 0;
    VkResult vkResult = vkEnumerateDeviceExtensionProperties(physicalDevice, nullptr,
                                                             &extensionCount, nullptr);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not get device extension property count due to error, code: %d.", vkResult);
        return false;
    }

    std::vector<VkExtensionProperties> extensionProperties(extensionCount);
    vkResult = vkEnumerateDeviceExtensionProperties(physicalDevice, nullptr,
                                                    &extensionCount, extensionProperties.data());

    if (vkResult != VK_SUCCESS) {
        LogE("Could not get device extension properties due to error, code: %d.", vkResult);
        return false;
    }

    VkPhysicalDeviceProperties2 physicalDeviceProperties = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2,
            .pNext = nullptr
    };

    vkGetPhysicalDeviceProperties2(physicalDevice, &physicalDeviceProperties);
    for (const auto& requiredExtension : reqDeviceExtensions) {

        // Is current driver-supported Vulkan version >= Vulkan version in which this extension was added to core?
        if (requiredExtension.second > 0
            && physicalDeviceProperties.properties.apiVersion >= requiredExtension.second) {
            continue;
        }

        bool isSupported = false;
        for (const VkExtensionProperties& extension : extensionProperties) {

            if (strcmp(extension.extensionName, requiredExtension.first) == 0) {
                isSupported = true; break;
            }
        }

        if (!isSupported) {
            LogE("Required extension '%s' not supported by device.", requiredExtension.first);
            return false;
        }
    }

    return true;
}


bool VulkanRenderManager::verifyDeviceExtensionFeatureSupport(VkPhysicalDevice physicalDevice) {

    VkPhysicalDeviceDynamicRenderingFeaturesKHR dynamicRenderingFeatures = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_DYNAMIC_RENDERING_FEATURES_KHR,
            .pNext = nullptr,
    };

    VkPhysicalDeviceSamplerYcbcrConversionFeatures samplerYuvConversionFeatures = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES,
            .pNext = &dynamicRenderingFeatures,
    };

    VkPhysicalDeviceFeatures2 physicalDeviceFeatures = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,
            .pNext = &samplerYuvConversionFeatures
    };

    vkGetPhysicalDeviceFeatures2(physicalDevice, &physicalDeviceFeatures);

    if (samplerYuvConversionFeatures.samplerYcbcrConversion != VK_TRUE) {
        LogE("Required extension feature (samplerYcbcrConversion) not supported by device.");
        return false;
    }

    if (dynamicRenderingFeatures.dynamicRendering != VK_TRUE) {
        LogE("Required extension feature (dynamicRendering) not supported by device.");
        return false;
    }

    return true;
}

std::vector<const char*>
    VulkanRenderManager::getDeduplicatedExtensions(const VkDeviceCreateInfo* createInfo) {

    std::vector<const char*> extensions;
    extensions.reserve(createInfo->enabledExtensionCount + reqDeviceExtensions.size());

    if (createInfo->enabledExtensionCount > 0) {
        extensions.insert(extensions.end(),
                          createInfo->ppEnabledExtensionNames,
                          createInfo->ppEnabledExtensionNames + createInfo->enabledExtensionCount);
    }

    for (const auto& requiredExtension : reqDeviceExtensions) {

        bool alreadyExists = false;
        for (const char* extension : extensions) {

            if (strcmp(extension, requiredExtension.first) == 0) {
                alreadyExists = true; break;
            }
        }

        if (!alreadyExists) {
            extensions.push_back(requiredExtension.first);
            LogD("Inserting extension: '%s'.", requiredExtension.first);
        }
    }

    return extensions;
}

template<typename T> void
    VulkanRenderManager::appendFeatureToCreateDeviceInfoChain(VkBaseInStructure*& newLastNode,
                                                              const VkBaseInStructure* lastCallerProvidedNode,
                                                              bool& didModifyLastCallerProvidedNode,
                                                              T* override, const char* featureName) {

    auto node = reinterpret_cast<VkBaseInStructure*>(override);
    if (!newLastNode) {
        const_cast<VkBaseInStructure*>(lastCallerProvidedNode)->pNext = node;
        didModifyLastCallerProvidedNode = true;
    } else {
        newLastNode->pNext = node;
    }

    newLastNode = node;
    LogD("Linked new %s to features chain.", featureName);
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