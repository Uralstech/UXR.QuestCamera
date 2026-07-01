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
#include <optional>
#include <unordered_map>
#include <vulkan/vulkan_core.h>
#include <vulkan/vulkan_android.h>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include "../External/BoostCombineHash.h"

#define USED_VULKAN_FUNCTIONS(apply)                    \
    apply(vkCmdClearColorImage);                        \
    apply(vkCreateDevice);                              \
    apply(vkEnumerateDeviceExtensionProperties);        \
    apply(vkGetPhysicalDeviceFeatures2);                \
    apply(vkGetAndroidHardwareBufferPropertiesANDROID); \
    apply(vkGetPhysicalDeviceMemoryProperties2);        \
    apply(vkGetPhysicalDeviceFormatProperties2);        \
    apply(vkGetPhysicalDeviceImageFormatProperties2);   \
    apply(vkCreateImage);                               \
    apply(vkDestroyImage);                              \
    apply(vkAllocateMemory);                            \
    apply(vkFreeMemory);                                \
    apply(vkBindImageMemory2);                          \
    apply(vkCreateSamplerYcbcrConversion);              \
    apply(vkDestroySamplerYcbcrConversion);             \
    apply(vkCreateImageView);                           \
    apply(vkDestroyImageView);

#define EVENT_ID_RENDER 1

struct RenderData {
    AHardwareBuffer* srcHardwareBuffer;
    uint64_t srcHardwareBufferId;
    VkImage* dstImage;
    void (*onDone)(int64_t hardwareBufferId);
};

class VkRenderer {

public:
    static PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API
        hookVulkanInitialization(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void*);

    static bool isVulkanSetup();

    /// <summary>Must be called after Vulkan device initialization.</summary>
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

    class ImportedImage {

    public:
        VkImage image           = VK_NULL_HANDLE;
        VkDeviceMemory memory   = VK_NULL_HANDLE;
        VkImageView imageView   = VK_NULL_HANDLE;

        ImportedImage(VkDevice device_)
                : device(device_) { }

        ImportedImage(const ImportedImage&) = delete;
        ImportedImage& operator=(const ImportedImage&) = delete;

        ImportedImage(ImportedImage&&) = delete;
        ImportedImage& operator=(ImportedImage&&) = delete;

        ~ImportedImage() {

            if (imageView != VK_NULL_HANDLE) {
                vkDestroyImageView(device, imageView, nullptr);
            }

            if (image != VK_NULL_HANDLE) {
                vkDestroyImage(device, image, nullptr);
            }

            if (memory != VK_NULL_HANDLE) {
                vkFreeMemory(device, memory, nullptr);
            }
        }

    private:
        VkDevice device;
    };

    struct YuvHardwareBufferFormat {
        VkFormat format;
        uint64_t externalFormat;

        VkComponentMapping componentMapping;
        VkSamplerYcbcrModelConversion model;
        VkSamplerYcbcrRange range;
        VkChromaLocation xChromaOffset;
        VkChromaLocation yChromaOffset;

        struct Hasher
        {
            size_t operator()(const YuvHardwareBufferFormat& val) const {
                size_t seed = 0;

                hash_combine(seed, val.format);
                hash_combine(seed, val.externalFormat);

                hash_combine(seed, val.componentMapping.r);
                hash_combine(seed, val.componentMapping.g);
                hash_combine(seed, val.componentMapping.b);
                hash_combine(seed, val.componentMapping.a);

                hash_combine(seed, val.model);
                hash_combine(seed, val.range);
                hash_combine(seed, val.xChromaOffset);
                hash_combine(seed, val.yChromaOffset);

                return seed;
            }
        };

        bool operator==(const YuvHardwareBufferFormat& rhs) const {
            return format == rhs.format
                && externalFormat == rhs.externalFormat
                && componentMapping.r == rhs.componentMapping.r
                && componentMapping.g == rhs.componentMapping.g
                && componentMapping.b == rhs.componentMapping.b
                && componentMapping.a == rhs.componentMapping.a
                && model == rhs.model
                && range == rhs.range
                && xChromaOffset == rhs.xChromaOffset
                && yChromaOffset == rhs.yChromaOffset;
        }
    };

    IUnityGraphicsVulkan* unityVulkan;
    UnityVulkanInstance unityVulkanInstance;

    std::unordered_map<VkFormat, bool> externalFormatSupport;
    std::optional<VkPhysicalDeviceMemoryProperties2> deviceMemoryProperties;
    std::unordered_map<YuvHardwareBufferFormat, VkSamplerYcbcrConversion, YuvHardwareBufferFormat::Hasher> samplerYuvConversions;

    std::unique_ptr<ImportedImage> processHardwareBuffer(AHardwareBuffer* hardwareBuffer);
    bool getSamplerYuvConversion(const VkAndroidHardwareBufferFormatProperties2ANDROID& bufferFormatProperties, const VkExternalFormatANDROID* externalFormat, VkSamplerYcbcrConversion* sampler);
    bool getMemoryTypeIndex(uint32_t supportedMemTypes, VkFlags requiredMemProperties, uint32_t* memTypeIndex);
    bool confirmExternalFormatSupport(VkFormat format);
};


#endif //UXR_QUESTCAMERA_VKRENDERER_H
