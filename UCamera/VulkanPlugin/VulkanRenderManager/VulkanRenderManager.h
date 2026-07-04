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
#include <utility>
#include <vector>
#include <memory>
#include <optional>
#include <unordered_map>
#include <vulkan/vulkan_core.h>
#include <vulkan/vulkan_android.h>
#include <android/hardware_buffer.h>
#include <android/data_space.h>
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
    apply(vkCmdPipelineBarrier);                        \
    apply(vkCmdBeginRenderingKHR);                      \
    apply(vkCmdEndRenderingKHR);                        \
    apply(vkCmdBindPipeline);                           \
    apply(vkCmdSetViewport);                            \
    apply(vkCmdSetScissor);                             \
    apply(vkCmdBindDescriptorSets);                     \
    apply(vkCmdDraw);

// endregion

#define EVENT_ID_RENDER 1

struct RenderData {
    AHardwareBuffer* srcHardwareBuffer;
    ADataSpace srcHardwareBufferDataSpace;
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

    // Extension name : Core version in which it got (fully) promoted, or 0
    static constexpr std::array<const std::pair<const char*, uint32_t>, 6> reqDeviceExtensions = {
            std::make_pair(VK_KHR_SAMPLER_YCBCR_CONVERSION_EXTENSION_NAME,                      VK_API_VERSION_1_1),
            std::make_pair(VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME,                          0),
            std::make_pair(VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME,   0),
            std::make_pair(VK_KHR_CREATE_RENDERPASS_2_EXTENSION_NAME,                           VK_API_VERSION_1_2),
            std::make_pair(VK_KHR_DEPTH_STENCIL_RESOLVE_EXTENSION_NAME,                         VK_API_VERSION_1_2),
            std::make_pair(VK_KHR_DYNAMIC_RENDERING_EXTENSION_NAME,                             VK_API_VERSION_1_3)
            // NOTE: VK_QCOM_ycbcr_degamma might help convert sampled YUV colors to linear RGB, but isn't support on Quest 3, as of HzOS v2.5
    };

    static VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL
        Hook_vkGetInstanceProcAddr(VkInstance device, const char* funcName);

    static VKAPI_ATTR VkResult VKAPI_CALL
        Hook_vkCreateInstance(const VkInstanceCreateInfo* pCreateInfo,
                              const VkAllocationCallbacks* pAllocator, VkInstance* pInstance);

    static VKAPI_ATTR VkResult VKAPI_CALL
        Hook_vkCreateDevice(VkPhysicalDevice physicalDevice, const VkDeviceCreateInfo* pCreateInfo,
                            const VkAllocationCallbacks* pAllocator, VkDevice* pDevice);

    static bool verifyDeviceExtensionSupport(VkPhysicalDevice physicalDevice);
    static bool verifyDeviceExtensionFeatureSupport(VkPhysicalDevice physicalDevice);
    static std::vector<const char*> getDeduplicatedExtensions(const VkDeviceCreateInfo* createInfo);

    template<typename T> static void
        appendFeatureToCreateDeviceInfoChain(VkBaseInStructure*& newLastNode,
                                             const VkBaseInStructure* lastCallerProvidedNode,
                                             bool& didModifyLastCallerProvidedNode,
                                             T* override, const char* featureName);

    static void loadVulkanFunctions(PFN_vkGetInstanceProcAddr getInstanceProcAddr,
                                    VkInstance instance);

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

    struct RenderPipelineKey {
    private:
        VkFormat srcFormat;
        uint64_t srcExternalFormat;

        VkComponentMapping srcComponentMapping;
        VkSamplerYcbcrModelConversion srcModel;
        VkSamplerYcbcrRange srcRange;
        VkChromaLocation srcXChromaOffset;
        VkChromaLocation srcYChromaOffset;

        VkFormat targetFormat;

    public:

        RenderPipelineKey(const VkAndroidHardwareBufferFormatPropertiesANDROID& src, VkFormat targetFormat_)
            : srcFormat(src.format),
              srcExternalFormat(src.externalFormat),
              srcComponentMapping(src.samplerYcbcrConversionComponents),
              srcModel(src.suggestedYcbcrModel),
              srcRange(src.suggestedYcbcrRange),
              srcXChromaOffset(src.suggestedXChromaOffset),
              srcYChromaOffset(src.suggestedYChromaOffset),
              targetFormat(targetFormat_) { }

        struct Hasher {
            size_t operator()(const RenderPipelineKey& val) const {
                size_t seed = 0;

                hash_combine(seed, val.srcFormat);
                hash_combine(seed, val.srcExternalFormat);

                hash_combine(seed, val.srcComponentMapping.r);
                hash_combine(seed, val.srcComponentMapping.g);
                hash_combine(seed, val.srcComponentMapping.b);
                hash_combine(seed, val.srcComponentMapping.a);

                hash_combine(seed, val.srcModel);
                hash_combine(seed, val.srcRange);
                hash_combine(seed, val.srcXChromaOffset);
                hash_combine(seed, val.srcYChromaOffset);

                hash_combine(seed, val.targetFormat);
                return seed;
            }
        };

        bool operator==(const RenderPipelineKey& rhs) const {
            return srcFormat == rhs.srcFormat
                   && srcExternalFormat == rhs.srcExternalFormat
                   && srcComponentMapping.r == rhs.srcComponentMapping.r
                   && srcComponentMapping.g == rhs.srcComponentMapping.g
                   && srcComponentMapping.b == rhs.srcComponentMapping.b
                   && srcComponentMapping.a == rhs.srcComponentMapping.a
                   && srcModel == rhs.srcModel
                   && srcRange == rhs.srcRange
                   && srcXChromaOffset == rhs.srcXChromaOffset
                   && srcYChromaOffset == rhs.srcYChromaOffset
                   && targetFormat == rhs.targetFormat;
        }
    };

    struct RenderSubmission {
        std::unique_ptr<ImportedYuvImage> srcImage  = nullptr;
        VkDescriptorSet descriptorSet               = VK_NULL_HANDLE;
        VkImageView targetImageView                 = VK_NULL_HANDLE;
        unsigned long long frameNumber              = 0;

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

            if (targetImageView != VK_NULL_HANDLE) {
                vkDestroyImageView(device, targetImageView, nullptr);
            }
        }

    private:
        VkDevice device;
    };

    // endregion

    IUnityGraphicsVulkan*   unityVulkan;
    UnityVulkanInstance     unityVulkanInstance;

    std::unordered_map<VkFormat, ExternalFormatProperties> externalFormatProperties;
    std::optional<VkPhysicalDeviceMemoryProperties2> deviceMemoryProperties;

    std::unordered_map<RenderPipelineKey, std::unique_ptr<YuvGraphicsPipeline>,
                       RenderPipelineKey::Hasher> graphicsPipelines;

    VkDescriptorPool submissionsDescriptorPool = VK_NULL_HANDLE;
    std::vector<std::unique_ptr<RenderSubmission>> ongoingSubmissions;

    bool getHardwareBufferProperties(AHardwareBuffer* hardwareBuffer,
                                     VkAndroidHardwareBufferPropertiesANDROID* bufferProperties,
                                     VkAndroidHardwareBufferFormatPropertiesANDROID* bufferFormatProperties);

    bool getExternalFormatProperties(VkFormat format, ExternalFormatProperties* formatProperties);

    const YuvGraphicsPipeline*
        getGraphicsPipeline(const VkAndroidHardwareBufferFormatPropertiesANDROID& bufferFormatProperties,
                            VkFormatFeatureFlags bufferFormatFeatures, VkFormat targetFormat);
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

    void pruneOngoingSubmissions(unsigned long long safeFrame);

    bool getDescriptorPool(VkDescriptorPool* descriptorPool);
    bool allocateDescriptorSet(VkDescriptorPool descriptorPool, VkDescriptorSetLayout descriptorSetLayout,
                               VkDescriptorSet* descriptorSet);

    bool constructTargetImageView(const UnityVulkanImage& image, VkImageView* imageView);
};


#endif //UXR_QUESTCAMERA_VULKANRENDERMANAGER_H
