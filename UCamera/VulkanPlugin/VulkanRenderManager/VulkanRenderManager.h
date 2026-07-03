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

#ifndef UXR_QUESTCAMERA_VULKANRENDERMANAGER_H
#define UXR_QUESTCAMERA_VULKANRENDERMANAGER_H

#include <array>
#include <memory>
#include <optional>
#include <unordered_map>
#include <vulkan/vulkan_core.h>
#include <vulkan/vulkan_android.h>
#include <android/hardware_buffer.h>
#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include "../External/BoostCombineHash.h"

// region Used Vulkan Functions
#define USED_VULKAN_FUNCTIONS(apply)                    \
    apply(vkCreateDevice);                              \
    apply(vkEnumerateDeviceExtensionProperties);        \
    apply(vkGetPhysicalDeviceFeatures2);                \
    apply(vkGetPhysicalDeviceProperties2);              \
    apply(vkGetPhysicalDeviceMemoryProperties2);        \
    apply(vkGetPhysicalDeviceFormatProperties2);        \
    apply(vkGetPhysicalDeviceImageFormatProperties2);   \
    apply(vkGetAndroidHardwareBufferPropertiesANDROID); \
    apply(vkCreateImage);                               \
    apply(vkDestroyImage);                              \
    apply(vkAllocateMemory);                            \
    apply(vkFreeMemory);                                \
    apply(vkBindImageMemory2);                          \
    apply(vkCreateSamplerYcbcrConversion);              \
    apply(vkDestroySamplerYcbcrConversion);             \
    apply(vkCreateSampler);                             \
    apply(vkDestroySampler);                            \
    apply(vkCreateImageView);                           \
    apply(vkDestroyImageView);                          \
    apply(vkCreateShaderModule);                        \
    apply(vkDestroyShaderModule);                       \
    apply(vkGetDescriptorSetLayoutSupport);             \
    apply(vkCreateDescriptorSetLayout);                 \
    apply(vkDestroyDescriptorSetLayout);                \
    apply(vkCreatePipelineLayout);                      \
    apply(vkDestroyPipelineLayout);                     \
    apply(vkCreateGraphicsPipelines);                   \
    apply(vkDestroyPipeline);                           \
    apply(vkCreateDescriptorPool);                      \
    apply(vkDestroyDescriptorPool);                     \
    apply(vkAllocateDescriptorSets);                    \
    apply(vkFreeDescriptorSets);                        \
    apply(vkUpdateDescriptorSets);                      \
    apply(vkCreateFence);                               \
    apply(vkDestroyFence);                              \
    apply(vkWaitForFences);                             \
    apply(vkCmdPipelineBarrier);

// endregion

#define EVENT_ID_RENDER 1

struct RenderData {
    AHardwareBuffer* srcHardwareBuffer;
    uint64_t srcHardwareBufferId;
    VkImage* dstImage;
    void (*onDone)(int64_t hardwareBufferId);
};

class VulkanRenderManager {

public:

    // region Static
    static PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API
        hookVulkanInitialization(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void*);

    static bool isRuntimeSupported();

    // endregion

    /// <summary>Must be called after Vulkan device initialization.</summary>
    VulkanRenderManager(IUnityGraphicsVulkan* unityVulkan);

    void onDeviceInitialized();
    void onDeviceShutdown();

    void render(RenderData* data);

private:

    // region Static
#define DEFINE_VULKAN_FUNCTIONPTR(func) static PFN_##func func
    DEFINE_VULKAN_FUNCTIONPTR(vkGetInstanceProcAddr);
    USED_VULKAN_FUNCTIONS(DEFINE_VULKAN_FUNCTIONPTR);
#undef DEFINE_VULKAN_FUNCTIONPTR

    static constexpr const char* TAG = "UXRQC.VkRenderMgr";
    static void LogI(const char* msg, ...);
    static void LogD(const char* msg, ...);
    static void LogE(const char* msg, ...);

    static constexpr uint32_t minimumVulkanAPI = VK_VERSION_1_1;
    static bool isVulkanInstanceSupported;
    static bool isVulkanDeviceSupported;

    static constexpr std::array<const char*, 6> reqDeviceExtensions = {
            VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME,
            VK_KHR_EXTERNAL_MEMORY_EXTENSION_NAME,
            VK_KHR_DEDICATED_ALLOCATION_EXTENSION_NAME,
            VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME,
            VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME,
            VK_KHR_MAINTENANCE_6_EXTENSION_NAME
    };

    static VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL
        Hook_vkGetInstanceProcAddr(VkInstance device, const char* funcName);

    static VKAPI_ATTR VkResult VKAPI_CALL
        Hook_vkCreateInstance(const VkInstanceCreateInfo* pCreateInfo,
                              const VkAllocationCallbacks* pAllocator, VkInstance* pInstance);

    static VKAPI_ATTR VkResult VKAPI_CALL
        Hook_vkCreateDevice(VkPhysicalDevice physicalDevice, const VkDeviceCreateInfo* pCreateInfo,
                            const VkAllocationCallbacks* pAllocator, VkDevice* pDevice);

    static void loadVulkanFunctions(PFN_vkGetInstanceProcAddr getInstanceProcAddr,
                                    VkInstance instance);

    static bool tryGetDeviceSupportedExtensions(VkPhysicalDevice device,
                                                std::vector<VkExtensionProperties>* result);

    // endregion

    // region Embedded Structs
    struct ExternalFormatProperties {
        VkFormatFeatureFlags formatFeatures = 0;
        VkExternalMemoryProperties memoryProperties = {};
    };

    struct YuvGraphicsPipeline {
        VkPipeline pipeline                         = VK_NULL_HANDLE;
        VkPipelineLayout pipelineLayout             = VK_NULL_HANDLE;
        VkDescriptorSetLayout descriptorSetLayout   = VK_NULL_HANDLE;
        VkRenderPass pipelineRenderPass             = VK_NULL_HANDLE;
        VkSampler sampler                           = VK_NULL_HANDLE;
        VkSamplerYcbcrConversion conversion         = VK_NULL_HANDLE;

        YuvGraphicsPipeline(VkDevice device_)
            : device(device_) { }

        YuvGraphicsPipeline(const YuvGraphicsPipeline&) = delete;
        YuvGraphicsPipeline& operator=(const YuvGraphicsPipeline&) = delete;

        YuvGraphicsPipeline(YuvGraphicsPipeline&&) = delete;
        YuvGraphicsPipeline& operator=(YuvGraphicsPipeline&&) = delete;

        ~YuvGraphicsPipeline() {

            if (pipeline != VK_NULL_HANDLE) {
                vkDestroyPipeline(device, pipeline, nullptr);
            }

            if (pipelineLayout != VK_NULL_HANDLE) {
                vkDestroyPipelineLayout(device, pipelineLayout, nullptr);
            }

            if (descriptorSetLayout != VK_NULL_HANDLE) {
                vkDestroyDescriptorSetLayout(device, descriptorSetLayout, nullptr);
            }

            pipelineRenderPass = VK_NULL_HANDLE;

            if (sampler != VK_NULL_HANDLE) {
                vkDestroySampler(device, sampler, nullptr);
            }

            if (conversion != VK_NULL_HANDLE) {
                vkDestroySamplerYcbcrConversion(device, conversion, nullptr);
            }
        }

    private:
        VkDevice device;
    };

    struct ImportedYuvImage {
        VkImage image           = VK_NULL_HANDLE;
        VkDeviceMemory memory   = VK_NULL_HANDLE;
        VkImageView imageView   = VK_NULL_HANDLE;

        ImportedYuvImage(VkDevice device_)
            : device(device_) { }

        ImportedYuvImage(const ImportedYuvImage&) = delete;
        ImportedYuvImage& operator=(const ImportedYuvImage&) = delete;

        ImportedYuvImage(ImportedYuvImage&&) = delete;
        ImportedYuvImage& operator=(ImportedYuvImage&&) = delete;

        ~ImportedYuvImage() {

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

        static YuvHardwareBufferFormat fromHardwareBufferFormat(const VkAndroidHardwareBufferFormatPropertiesANDROID& bufferFormatProperties) {
            return YuvHardwareBufferFormat {
                    .format = bufferFormatProperties.format,
                    .externalFormat = bufferFormatProperties.externalFormat,

                    .componentMapping = bufferFormatProperties.samplerYcbcrConversionComponents,
                    .model = bufferFormatProperties.suggestedYcbcrModel,
                    .range = bufferFormatProperties.suggestedYcbcrRange,
                    .xChromaOffset = bufferFormatProperties.suggestedXChromaOffset,
                    .yChromaOffset = bufferFormatProperties.suggestedYChromaOffset,
            };
        }

        struct Hasher {
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

    struct RenderSubmission {
        std::unique_ptr<ImportedYuvImage> srcImage  = nullptr;
        VkDescriptorSet descriptorSet               = VK_NULL_HANDLE;
        VkFence fence                               = VK_NULL_HANDLE;

        RenderSubmission(VkDevice device_)
            : device(device_) { }

        RenderSubmission(const RenderSubmission&) = delete;
        RenderSubmission& operator=(const RenderSubmission&) = delete;

        RenderSubmission(RenderSubmission&&) = delete;
        RenderSubmission& operator=(RenderSubmission&&) = delete;

        bool freeDescriptorSet(VkDescriptorPool descriptorPool) {

            if (descriptorSet == VK_NULL_HANDLE) {
                return true;
            }

            VkResult vkResult = vkFreeDescriptorSets(device, descriptorPool, 1,
                                                     &descriptorSet);

            if (vkResult != VK_SUCCESS) {
                LogE("Could not free descriptor set due to error, code: %d.", vkResult);
                return false;
            }

            descriptorSet = VK_NULL_HANDLE;
            return true;
        }

        ~RenderSubmission() {

            if (fence != VK_NULL_HANDLE) {
                vkDestroyFence(device, fence, nullptr);
            }

            // srcImage is auto-deleted
        }

    private:
        VkDevice device;
    };

    // endregion

    IUnityGraphicsVulkan*   unityVulkan;
    UnityVulkanInstance     unityVulkanInstance;

    std::unordered_map<VkFormat, ExternalFormatProperties> externalFormatProperties;
    std::optional<VkPhysicalDeviceMemoryProperties2> deviceMemoryProperties;

    std::unordered_map<YuvHardwareBufferFormat, std::unique_ptr<YuvGraphicsPipeline>,
                       YuvHardwareBufferFormat::Hasher> graphicsPipelines;

    VkDescriptorPool submissionsDescriptorPool = VK_NULL_HANDLE;
    std::vector<std::unique_ptr<RenderSubmission>> ongoingSubmissions;

    std::unordered_map<VkImage, VkImageView> targetImageViews;

    void pruneOngoingSubmissions();

    bool getDescriptorPool(VkDescriptorPool* descriptorPool);

    bool getHardwareBufferProperties(AHardwareBuffer* hardwareBuffer,
                                     VkAndroidHardwareBufferPropertiesANDROID* bufferProperties,
                                     VkAndroidHardwareBufferFormatPropertiesANDROID* bufferFormatProperties);

    bool getExternalFormatProperties(VkFormat format, ExternalFormatProperties* formatProperties);

    const YuvGraphicsPipeline*
        getGraphicsPipeline(const VkAndroidHardwareBufferFormatPropertiesANDROID& bufferFormatProperties,
                            VkFormatFeatureFlags bufferFormatFeatures, VkRenderPass renderPass);
    bool constructYuvSampler(const VkAndroidHardwareBufferFormatPropertiesANDROID& bufferFormatProperties,
                             VkFormatFeatureFlags bufferFormatFeatures, VkSamplerYcbcrConversion* conversion,
                             VkSampler* sampler);
    bool constructPipelineDescriptorSetLayout(VkSampler sampler,
                                              VkDescriptorSetLayout* descriptorSetLayout);
    bool constructPipelineLayout(VkDescriptorSetLayout descriptorSetLayout,
                                 VkPipelineLayout* pipelineLayout);

    std::unique_ptr<ImportedYuvImage>
        importHardwareBufferImage(const VkAndroidHardwareBufferPropertiesANDROID& bufferProperties,
                                  const VkAndroidHardwareBufferFormatPropertiesANDROID& bufferFormatProperties,
                                  const AHardwareBuffer_Desc& hardwareBufferDesc,
                                  AHardwareBuffer* hardwareBuffer,
                                  VkSamplerYcbcrConversion samplerConversion);
    bool getMemoryTypeIndex(uint32_t supportedMemTypes, VkFlags requiredMemProperties,
                            uint32_t* memTypeIndex);

    bool allocateDescriptorSet(VkDescriptorPool descriptorPool, VkDescriptorSetLayout descriptorSetLayout,
                               VkDescriptorSet* descriptorSet);

    bool getTargetImageView(const UnityVulkanImage& image, VkImageView* imageView);
};


#endif //UXR_QUESTCAMERA_VULKANRENDERMANAGER_H
