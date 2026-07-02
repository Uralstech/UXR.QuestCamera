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
#include "../CompiledShaders/render_vert.h"
#include "../CompiledShaders/render_frag.h"
#include <android/hardware_buffer.h>

VkRenderer::VkRenderer(IUnityGraphicsVulkan* unityVulkan_)
    : unityVulkan(unityVulkan_), unityVulkanInstance(unityVulkan_->Instance()) { }

void VkRenderer::onDeviceInitialized() {

    UnityVulkanPluginEventConfig config = { };
    config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureInside;
    config.flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission | kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState;

    unityVulkan->ConfigureEvent(EVENT_ID_RENDER, &config);
    LogD("Events configured.");
}

void VkRenderer::onDeviceShutdown() {

    if (graphicsPipeline != VK_NULL_HANDLE) {
        vkDestroyPipeline(unityVulkanInstance.device, graphicsPipeline, nullptr);
        graphicsPipeline = VK_NULL_HANDLE;
    }

    if (graphicsPipelineLayout != VK_NULL_HANDLE) {
        vkDestroyPipelineLayout(unityVulkanInstance.device, graphicsPipelineLayout, nullptr);
        graphicsPipelineLayout = VK_NULL_HANDLE;
    }

    if (graphicsPipelineDescriptorSetLayout != VK_NULL_HANDLE) {
        vkDestroyDescriptorSetLayout(unityVulkanInstance.device, graphicsPipelineDescriptorSetLayout, nullptr);
        graphicsPipelineDescriptorSetLayout = VK_NULL_HANDLE;
    }

    for (auto it = samplerYuvConversions.begin(); it != samplerYuvConversions.end();) {
        vkDestroySamplerYcbcrConversion(unityVulkanInstance.device, it->second, nullptr);
        it = samplerYuvConversions.erase(it);
    }

    graphicsPipelineRenderPass = VK_NULL_HANDLE;
    deviceMemoryProperties.reset();
    externalFormatSupport.clear();

    LogD("Cleaned up resources.");
}

void VkRenderer::render(RenderData* data) {

    if (data == nullptr) {
        LogE("Data is null.");
        return;
    }

    AHardwareBuffer* hardwareBuffer = data->srcHardwareBuffer;
    if (hardwareBuffer == nullptr) {
        LogE("Provided srcHardwareBuffer is a nullptr, cannot render.");
        return;
    }

    UnityVulkanImage uvkImage;
    std::unique_ptr<ImportedImage> importedImage;
    if (data->dstImage == nullptr) {
        LogE("Provided dstImage is a nullptr, cannot render.");
        goto renderEnd;
    }

    importedImage = processHardwareBuffer(hardwareBuffer);
    if (!importedImage) {
        goto renderEnd;
    }

    if (!unityVulkan->AccessTexture(
            data->dstImage, UnityVulkanWholeImage,
            VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, VK_PIPELINE_STAGE_TRANSFER_BIT,
            VK_ACCESS_TRANSFER_WRITE_BIT, kUnityVulkanResourceAccess_PipelineBarrier, &uvkImage)) {

        LogE("Could not get UnityVulkanImage from dstImage!");
        goto renderEnd;
    }

    UnityVulkanRecordingState recording;
    if (!unityVulkan->CommandRecordingState(&recording, kUnityVulkanGraphicsQueueAccess_DontCare)) {
        LogE("Could not get Unity command recording state!");
        goto renderEnd;
    }

    VkPipeline pipeline;
    if (!getGraphicsPipeline(recording.renderPass, &pipeline)) {
        goto renderEnd;
    }

renderEnd:
    AHardwareBuffer_release(hardwareBuffer);
    if (data->onDone) {
        data->onDone(data->srcHardwareBufferId);
    }
}

bool VkRenderer::getGraphicsPipeline(VkRenderPass renderPass, VkPipeline* pipeline) {

    if (graphicsPipeline != VK_NULL_HANDLE && graphicsPipelineRenderPass == renderPass) {
        *pipeline = graphicsPipeline;
        return true;
    }

    LogD("Creating graphics pipeline.");
    if (graphicsPipeline != VK_NULL_HANDLE) {
        vkDestroyPipeline(unityVulkanInstance.device, graphicsPipeline, nullptr);
        graphicsPipeline = VK_NULL_HANDLE;
    }

    VkPipelineLayout pipelineLayout;
    if (!getGraphicsPipelineLayout(&pipelineLayout)) {
        return false;
    }

    VkShaderModuleCreateInfo moduleCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
    };

    moduleCreateInfo.codeSize = render_vert_size;
    moduleCreateInfo.pCode = render_vert;

    VkShaderModule vertexShaderModule;
    VkResult vkResult = vkCreateShaderModule(unityVulkanInstance.device, &moduleCreateInfo, nullptr, &vertexShaderModule);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not create vertex shader module due to error, code: %d.", vkResult);
        return false;
    }

    moduleCreateInfo.codeSize = render_frag_size;
    moduleCreateInfo.pCode = render_frag;

    VkShaderModule fragmentShaderModule;
    vkResult = vkCreateShaderModule(unityVulkanInstance.device, &moduleCreateInfo, nullptr, &fragmentShaderModule);
    if (vkResult != VK_SUCCESS) {
        vkDestroyShaderModule(unityVulkanInstance.device, vertexShaderModule, nullptr);
        LogE("Could not create fragment shader module due to error, code: %d.", vkResult);
        return false;
    }

    VkPipelineShaderStageCreateInfo shaderStageCreateInfos[2] {
            VkPipelineShaderStageCreateInfo {
                    .sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,
                    .pNext = nullptr,
                    .flags = 0,
                    .stage = VK_SHADER_STAGE_VERTEX_BIT,
                    .module = vertexShaderModule,
                    .pName = "main",
                    .pSpecializationInfo = nullptr
            },
            VkPipelineShaderStageCreateInfo {
                    .sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,
                    .pNext = nullptr,
                    .flags = 0,
                    .stage = VK_SHADER_STAGE_FRAGMENT_BIT,
                    .module = fragmentShaderModule,
                    .pName = "main",
                    .pSpecializationInfo = nullptr
            }
    };

    VkPipelineVertexInputStateCreateInfo vertexInputStateCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .vertexBindingDescriptionCount = 0,
            .pVertexBindingDescriptions = nullptr,
            .vertexAttributeDescriptionCount = 0,
            .pVertexAttributeDescriptions = nullptr
    };

    VkPipelineInputAssemblyStateCreateInfo inputAssemblyStateCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST,
            .primitiveRestartEnable = VK_FALSE
    };

    VkPipelineViewportStateCreateInfo viewportStateCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .viewportCount = 1,
            .pViewports = nullptr, // dynamic
            .scissorCount = 1,
            .pScissors = nullptr // dynamic
    };

    VkPipelineRasterizationStateCreateInfo rasterizationStateCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .depthClampEnable = VK_FALSE,
            .rasterizerDiscardEnable = VK_FALSE,
            .polygonMode = VK_POLYGON_MODE_FILL,
            .cullMode = VK_CULL_MODE_BACK_BIT,
            .frontFace = VK_FRONT_FACE_CLOCKWISE,
            .depthBiasEnable = VK_FALSE,
            .depthBiasConstantFactor = 0.0f,
            .depthBiasClamp = 0.0f,
            .depthBiasSlopeFactor = 0.0f,
            .lineWidth = 1.0f,
    };

    VkPipelineMultisampleStateCreateInfo multisampleStateCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .rasterizationSamples = VK_SAMPLE_COUNT_1_BIT,
            .sampleShadingEnable = VK_FALSE,
            .minSampleShading = 0.0f,
            .pSampleMask = nullptr,
            .alphaToCoverageEnable = VK_FALSE,
            .alphaToOneEnable = VK_FALSE,
    };

    VkPipelineColorBlendAttachmentState colorBlendAttachmentState = {
            .blendEnable = VK_FALSE,
            .srcColorBlendFactor = VK_BLEND_FACTOR_ONE,
            .dstColorBlendFactor = VK_BLEND_FACTOR_ZERO,
            .colorBlendOp = VK_BLEND_OP_ADD,
            .srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE,
            .dstAlphaBlendFactor = VK_BLEND_FACTOR_ZERO,
            .alphaBlendOp = VK_BLEND_OP_ADD,
            .colorWriteMask = VK_COLOR_COMPONENT_R_BIT
                              | VK_COLOR_COMPONENT_G_BIT
                              | VK_COLOR_COMPONENT_B_BIT
                              | VK_COLOR_COMPONENT_A_BIT
    };

    VkPipelineColorBlendStateCreateInfo colorBlendStateCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .logicOpEnable = VK_FALSE,
            .logicOp = VK_LOGIC_OP_COPY,
            .attachmentCount = 1,
            .pAttachments = &colorBlendAttachmentState,
            .blendConstants = { 0.0f, 0.0f, 0.0f, 0.0f }
    };

    VkDynamicState dynamicStates[2] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
    };

    VkPipelineDynamicStateCreateInfo dynamicStateCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .dynamicStateCount = 2,
            .pDynamicStates = dynamicStates
    };

    VkGraphicsPipelineCreateInfo pipelineCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .stageCount = 2,
            .pStages = shaderStageCreateInfos,
            .pVertexInputState = &vertexInputStateCreateInfo,
            .pInputAssemblyState = &inputAssemblyStateCreateInfo,
            .pTessellationState = nullptr,
            .pViewportState = &viewportStateCreateInfo,
            .pRasterizationState = &rasterizationStateCreateInfo,
            .pMultisampleState = &multisampleStateCreateInfo,
            .pDepthStencilState = nullptr,
            .pColorBlendState = &colorBlendStateCreateInfo,
            .pDynamicState = &dynamicStateCreateInfo,
            .layout = pipelineLayout,
            .renderPass = renderPass,
            .subpass = 0,
            .basePipelineHandle = VK_NULL_HANDLE,
            .basePipelineIndex = -1,
    };

    VkPipeline vkCreatedPipeline;
    vkResult = vkCreateGraphicsPipelines(unityVulkanInstance.device, unityVulkanInstance.pipelineCache,
                                         1, &pipelineCreateInfo, nullptr, &vkCreatedPipeline);

    vkDestroyShaderModule(unityVulkanInstance.device, fragmentShaderModule, nullptr);
    vkDestroyShaderModule(unityVulkanInstance.device, vertexShaderModule, nullptr);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not create graphics pipeline due to error, code: %d.", vkResult);
        return false;
    }

    *pipeline = graphicsPipeline = vkCreatedPipeline;
    graphicsPipelineRenderPass = renderPass;
    return true;
}

bool VkRenderer::getGraphicsPipelineLayout(VkPipelineLayout* layout) {

    if (graphicsPipelineLayout != VK_NULL_HANDLE) {
        *layout = graphicsPipelineLayout;
        return true;
    }

    VkDescriptorSetLayout descriptorSetLayout;
    if (!getGraphicsPipelineDescriptorSetLayout(&descriptorSetLayout)) {
        return false;
    }

    VkPipelineLayoutCreateInfo layoutCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .setLayoutCount = 1,
            .pSetLayouts = &descriptorSetLayout,
            .pushConstantRangeCount = 0,
            .pPushConstantRanges = nullptr
    };

    VkPipelineLayout vkCreatedPipelineLayout;
    VkResult vkResult = vkCreatePipelineLayout(unityVulkanInstance.device, &layoutCreateInfo, nullptr, &vkCreatedPipelineLayout);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not create graphics pipeline layout due to error, code: %d.", vkResult);
        return false;
    }

    *layout = graphicsPipelineLayout = vkCreatedPipelineLayout;
    return true;
}

bool VkRenderer::getGraphicsPipelineDescriptorSetLayout(VkDescriptorSetLayout* descriptorSetLayout) {

    if (graphicsPipelineDescriptorSetLayout != VK_NULL_HANDLE) {
        *descriptorSetLayout = graphicsPipelineDescriptorSetLayout;
        return true;
    }

    VkDescriptorSetLayoutBinding descriptorSetLayoutBinding = {
            .binding = 0,
            .descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,
            .descriptorCount = 1,
            .stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT,
    };

    VkDescriptorSetLayoutCreateInfo descriptorSetLayoutCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .bindingCount = 1,
            .pBindings = &descriptorSetLayoutBinding
    };

    VkDescriptorSetLayoutSupport descriptorSetLayoutSupport = {
            .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_SUPPORT,
            .pNext = nullptr,
    };

    vkGetDescriptorSetLayoutSupport(unityVulkanInstance.device, &descriptorSetLayoutCreateInfo, &descriptorSetLayoutSupport);
    if (descriptorSetLayoutSupport.supported != VK_TRUE) {
        LogE("Could not create descriptor set layout for graphics pipeline as the device does not support it.");
        return false;
    }

    VkDescriptorSetLayout vkCreatedDescriptorSetLayout;
    VkResult vkResult = vkCreateDescriptorSetLayout(unityVulkanInstance.device, &descriptorSetLayoutCreateInfo,
                                                    nullptr, &vkCreatedDescriptorSetLayout);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not create descriptor set layout for graphics pipeline due to error, code: %d.", vkResult);
        return false;
    }

    *descriptorSetLayout = graphicsPipelineDescriptorSetLayout = vkCreatedDescriptorSetLayout;
    return true;
}

std::unique_ptr<VkRenderer::ImportedImage> VkRenderer::processHardwareBuffer(AHardwareBuffer* hardwareBuffer) {

    auto importedImage = std::make_unique<ImportedImage>(unityVulkanInstance.device);
    VkAndroidHardwareBufferFormatPropertiesANDROID bufferFormatProperties = {
            .sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID,
            .pNext = nullptr
    };

    VkAndroidHardwareBufferPropertiesANDROID bufferProperties = {
            .sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID,
            .pNext = &bufferFormatProperties
    };

    VkResult vkResult = vkGetAndroidHardwareBufferPropertiesANDROID(unityVulkanInstance.device, hardwareBuffer, &bufferProperties);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not access srcHardwareBuffer properties due to error, code: %d", vkResult);
        return nullptr;
    }

    bool useExternalFormat = bufferFormatProperties.format == VK_FORMAT_UNDEFINED;
    bool useLinearFiltering = false;

    if (useExternalFormat) {
        if ((bufferFormatProperties.formatFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT) != VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT) {

            LogE("Cannot use srcHardwareBuffer as it does not support sampling (externalFormat).");
            // TODO: Signal fatal error to C#
            return nullptr;
        }

        useLinearFiltering = (bufferFormatProperties.formatFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT)
                                    == VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT;

    } else if (!useExternalFormat
                && !confirmExternalFormatSupport(bufferFormatProperties.format, &useLinearFiltering)) {

        // TODO: Signal fatal error to C#
        return nullptr;
    }

    AHardwareBuffer_Desc hardwareBufferDesc;
    AHardwareBuffer_describe(hardwareBuffer, &hardwareBufferDesc);

    if ((hardwareBufferDesc.usage & AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE)
            != AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE) {

        LogE("Cannot use srcHardwareBuffer as it is not configured for GPU sampling.");
        // TODO: Signal fatal error to C#
        return nullptr;
    }

    VkExternalFormatANDROID externalFormatAndroid = {
            .sType = VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID,
            .pNext = nullptr,
            .externalFormat = useExternalFormat ? bufferFormatProperties.externalFormat : 0
    };

    VkExternalMemoryImageCreateInfo extMemImageCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO,
            .pNext = &externalFormatAndroid,
            .handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID
    };

    VkImageCreateInfo imageCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO,
            .pNext = &extMemImageCreateInfo,
            .flags = 0,
            .imageType = VK_IMAGE_TYPE_2D,
            .format = bufferFormatProperties.format,
            .extent = VkExtent3D {
                .width = hardwareBufferDesc.width,
                .height = hardwareBufferDesc.height,
                .depth = 1
            },
            .mipLevels = 1,
            .arrayLayers = hardwareBufferDesc.layers,
            .samples = VK_SAMPLE_COUNT_1_BIT,
            .tiling = VK_IMAGE_TILING_OPTIMAL,
            .usage = VK_IMAGE_USAGE_SAMPLED_BIT,
            .sharingMode = VK_SHARING_MODE_EXCLUSIVE,
            .queueFamilyIndexCount = 0,
            .pQueueFamilyIndices = nullptr,
            .initialLayout = VK_IMAGE_LAYOUT_UNDEFINED
    };

    VkImage vkCreatedImage;
    vkResult = vkCreateImage(unityVulkanInstance.device, &imageCreateInfo, nullptr, &vkCreatedImage);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not create VkImage from srcHardwareBuffer due to error, code: %d", vkResult);
        return nullptr;
    }

    importedImage->image = vkCreatedImage;
    VkMemoryDedicatedAllocateInfo dedicatedAllocateInfo = {
            .sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO,
            .pNext = nullptr,
            .image = importedImage->image,
            .buffer = VK_NULL_HANDLE
    };

    VkImportAndroidHardwareBufferInfoANDROID importAndroidBufferInfo = {
        .sType = VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID,
        .pNext = &dedicatedAllocateInfo,
        .buffer = hardwareBuffer
    };

    VkMemoryAllocateInfo memoryAllocateInfo {
        .sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
        .pNext = &importAndroidBufferInfo,
        .allocationSize = bufferProperties.allocationSize
    };

    if (!getMemoryTypeIndex(bufferProperties.memoryTypeBits,
                            VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
                            &memoryAllocateInfo.memoryTypeIndex)) {

        LogE("Could not get memory type index for srcHardwareBuffer memory.");
        return nullptr;
    }

    VkDeviceMemory vkCreatedDeviceMemory;
    vkResult = vkAllocateMemory(unityVulkanInstance.device, &memoryAllocateInfo, nullptr, &vkCreatedDeviceMemory);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not allocate srcHardwareBuffer memory due to error, code: %d.", vkResult);
        return nullptr;
    }

    importedImage->memory = vkCreatedDeviceMemory;
    VkBindImageMemoryInfo bindImageMemoryInfo = {
            .sType = VK_STRUCTURE_TYPE_BIND_IMAGE_MEMORY_INFO,
            .pNext = nullptr,
            .image = importedImage->image,
            .memory = importedImage->memory,
            .memoryOffset = 0
    };

    vkResult = vkBindImageMemory2(unityVulkanInstance.device, 1, &bindImageMemoryInfo);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not bind srcHardwareBuffer image and memory due to error, code: %d", vkResult);
        return nullptr;
    }

    VkSamplerYcbcrConversion yuvSampler;
    if (!getSamplerYuvConversion(bufferFormatProperties, &externalFormatAndroid, useLinearFiltering, &yuvSampler)) {
        // TODO: Signal fatal error to C#
        return nullptr;
    }

    VkSamplerYcbcrConversionInfo yuvConversionInfo = {
            .sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO,
            .pNext = nullptr,
            .conversion = yuvSampler
    };

    VkImageViewCreateInfo imageViewCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,
            .pNext = &yuvConversionInfo,
            .flags = 0,
            .image = importedImage->image,
            .viewType = VK_IMAGE_VIEW_TYPE_2D,
            .format = bufferFormatProperties.format,
            .components = {
                    .r = VK_COMPONENT_SWIZZLE_IDENTITY,
                    .g = VK_COMPONENT_SWIZZLE_IDENTITY,
                    .b = VK_COMPONENT_SWIZZLE_IDENTITY,
                    .a = VK_COMPONENT_SWIZZLE_IDENTITY,
            },
            .subresourceRange = {
                    .aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                    .baseMipLevel = 0, .levelCount = 1,
                    .baseArrayLayer = 0, .layerCount = 1,
            }
    };

    VkImageView vkCreatedImageView;
    vkResult = vkCreateImageView(unityVulkanInstance.device, &imageViewCreateInfo, nullptr, &vkCreatedImageView);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not create image view for srcHardwareBuffer due to error, code: %d.", vkResult);
        return nullptr;
    }

    importedImage->imageView = vkCreatedImageView;
    return importedImage;
}


bool VkRenderer::getSamplerYuvConversion(const VkAndroidHardwareBufferFormatPropertiesANDROID& bufferFormatProperties,
                                         const VkExternalFormatANDROID* externalFormat, bool useLinearFiltering,
                                         VkSamplerYcbcrConversion* sampler) {

    YuvHardwareBufferFormat format = {
            .format = bufferFormatProperties.format,
            .externalFormat = bufferFormatProperties.externalFormat,

            .componentMapping = bufferFormatProperties.samplerYcbcrConversionComponents,
            .model = bufferFormatProperties.suggestedYcbcrModel,
            .range = bufferFormatProperties.suggestedYcbcrRange,
            .xChromaOffset = bufferFormatProperties.suggestedXChromaOffset,
            .yChromaOffset = bufferFormatProperties.suggestedYChromaOffset,
    };

    auto it = samplerYuvConversions.find(format);
    if (it != samplerYuvConversions.end()) {
        *sampler = it->second;
        return true;
    }

    VkSamplerYcbcrConversionCreateInfo createInfo = {
            .sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO,
            .pNext = externalFormat,
            .format = bufferFormatProperties.format,
            .ycbcrModel = bufferFormatProperties.suggestedYcbcrModel,
            .ycbcrRange = bufferFormatProperties.suggestedYcbcrRange,
            .components = bufferFormatProperties.samplerYcbcrConversionComponents,
            .xChromaOffset = bufferFormatProperties.suggestedXChromaOffset,
            .yChromaOffset = bufferFormatProperties.suggestedYChromaOffset,
            .chromaFilter = useLinearFiltering ? VK_FILTER_LINEAR : VK_FILTER_NEAREST,
            .forceExplicitReconstruction = VK_FALSE,
    };

    VkSamplerYcbcrConversion conversion;
    VkResult vkResult = vkCreateSamplerYcbcrConversion(unityVulkanInstance.device, &createInfo, nullptr, &conversion);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not create sampler YUV conversion due to error, code: %d.", vkResult);
        return false;
    }

    samplerYuvConversions[format] = conversion;
    *sampler = conversion;
    return true;
}

bool VkRenderer::getMemoryTypeIndex(uint32_t supportedMemTypes, VkFlags requiredMemProperties, uint32_t* memTypeIndex) {

    if (!deviceMemoryProperties) {

        VkPhysicalDeviceMemoryProperties2 properties = {
                .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MEMORY_PROPERTIES_2,
                .pNext = nullptr,
        };

        vkGetPhysicalDeviceMemoryProperties2(unityVulkanInstance.physicalDevice, &properties);
        deviceMemoryProperties = properties;
    }

    auto* memoryProperties = &deviceMemoryProperties->memoryProperties;
    for (uint32_t i = 0; i < memoryProperties->memoryTypeCount; ++i) {
        if ((supportedMemTypes & 1) == 1
            && (memoryProperties->memoryTypes[i].propertyFlags & requiredMemProperties) == requiredMemProperties) {
            *memTypeIndex = i;
            return true;
        }

        supportedMemTypes >>= 1;
    }

    return false;
}

bool VkRenderer::confirmExternalFormatSupport(VkFormat format, bool* supportsLinearFiltering) {

    auto cached = externalFormatSupport.find(format);
    if (cached != externalFormatSupport.end()) {
        *supportsLinearFiltering = cached->second.supportsLinearSampling;
        return cached->second.isSupported;
    }

    VkFormatProperties2 formatProperties = {
            .sType = VK_STRUCTURE_TYPE_FORMAT_PROPERTIES_2,
            .pNext = nullptr,
    };

    vkGetPhysicalDeviceFormatProperties2(unityVulkanInstance.physicalDevice, format, &formatProperties);
    if ((formatProperties.formatProperties.optimalTilingFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT)
            != VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT) {

        LogE("Cannot use srcHardwareBuffer as it does not support sampling (VkFormat).");
        return (externalFormatSupport[format] = { }).isSupported;
    }

    VkPhysicalDeviceExternalImageFormatInfo externalImageFormatInfo = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_EXTERNAL_IMAGE_FORMAT_INFO,
            .pNext = nullptr,
            .handleType = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID
    };

    VkPhysicalDeviceImageFormatInfo2 imageFormatInfo = {
            .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_IMAGE_FORMAT_INFO_2,
            .pNext = &externalImageFormatInfo,
            .format = format,
            .type = VK_IMAGE_TYPE_2D,
            .tiling = VK_IMAGE_TILING_OPTIMAL,
            .usage = VK_IMAGE_USAGE_SAMPLED_BIT,
            .flags = 0,
    };

    VkExternalImageFormatProperties externalImageFormatProperties = {
            .sType = VK_STRUCTURE_TYPE_EXTERNAL_IMAGE_FORMAT_PROPERTIES,
            .pNext = nullptr
    };

    VkImageFormatProperties2 imageFormatProperties = {
            .sType = VK_STRUCTURE_TYPE_IMAGE_FORMAT_PROPERTIES_2,
            .pNext = &externalImageFormatProperties
    };

    VkResult vkResult = vkGetPhysicalDeviceImageFormatProperties2(unityVulkanInstance.physicalDevice,
                                                                &imageFormatInfo,
                                                                &imageFormatProperties);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not get image format properties for srcHardwareBuffer due to error, code: %d.", vkResult);
        return (externalFormatSupport[format] = { }).isSupported;
    }

    if ((externalImageFormatProperties.externalMemoryProperties.compatibleHandleTypes
            & VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID)
                != VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID) {

        LogE("Cannot use srcHardwareBuffer as this device does not support it.");
        return (externalFormatSupport[format] = { }).isSupported;
    }

    if ((externalImageFormatProperties.externalMemoryProperties.externalMemoryFeatures
            & VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT) != VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT) {

        LogE("Cannot use srcHardwareBuffer as this device cannot import it.");
        return (externalFormatSupport[format] = { }).isSupported;
    }

    ExtFormatSupport formatSupport = {
            .isSupported = true,
            .supportsLinearSampling = (formatProperties.formatProperties.optimalTilingFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT)
                                            == VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT,
    };

    *supportsLinearFiltering = formatSupport.supportsLinearSampling;
    return (externalFormatSupport[format] = formatSupport).isSupported;
}
