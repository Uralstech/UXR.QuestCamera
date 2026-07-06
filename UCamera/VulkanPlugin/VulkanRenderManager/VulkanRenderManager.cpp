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

#include "../CompiledShaders/render_vert.h"
#include "../CompiledShaders/render_frag.h"

static bool useExternalFormat(const VkAndroidHardwareBufferFormatPropertiesANDROID& formatProps) {
    return formatProps.format == VK_FORMAT_UNDEFINED;
}

VulkanRenderManager::VulkanRenderManager(IUnityGraphicsVulkan* unityVulkan_)
        : unityVulkan(unityVulkan_), unityVulkanInstance(unityVulkan_->Instance()) {
    ongoingSubmissions.reserve(5);
}

void VulkanRenderManager::onDeviceInitialized() {

    UnityVulkanPluginEventConfig config = {
            .renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside,
            .graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare,
            .flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission
                   | kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState
    };

    unityVulkan->ConfigureEvent(EVENT_ID_RENDER, &config);
    LogD("Events configured.");
}

void VulkanRenderManager::onDeviceShutdown() {

    ongoingSubmissions.clear();

    if (submissionsDescriptorPool != VK_NULL_HANDLE) {
        vkDestroyDescriptorPool(unityVulkanInstance.device, submissionsDescriptorPool, nullptr);
        submissionsDescriptorPool = VK_NULL_HANDLE;
    }

    graphicsPipelines.clear();
    deviceMemoryProperties.reset();
    externalFormatProperties.clear();
    LogD("Cleaned up resources.");
}

void VulkanRenderManager::render(RenderData* data) {

    if (data == nullptr) {
        LogE("Data is null.");
        return;
    }

    struct ScopeExit {
        RenderData* data = nullptr;
        AHardwareBuffer* buffer = nullptr;
        uint8_t success = false;
        ~ScopeExit() {
            if (buffer)
                AHardwareBuffer_release(buffer);

            if (data->onDone)
                data->onDone(success, data->srcHardwareBufferId);
        }
    } scopeExit{data};

    AHardwareBuffer* hardwareBuffer = data->srcHardwareBuffer;
    if (hardwareBuffer == nullptr) {
        LogE("Provided hardware Buffer is null.");
        return;
    }

    scopeExit.buffer = hardwareBuffer;
    if (data->dstImage == nullptr) {
        LogE("Provided target image is null.");
        return;
    }

    unityVulkan->EnsureOutsideRenderPass();

    VkAndroidHardwareBufferPropertiesANDROID hardwareBufferProperties;
    VkAndroidHardwareBufferFormatPropertiesANDROID hardwareBufferFormatProperties;
    if (!getHardwareBufferProperties(hardwareBuffer, &hardwareBufferProperties, &hardwareBufferFormatProperties)) {
        return;
    }

    VkFormatFeatureFlags hardwareBufferFormatFeatures = hardwareBufferFormatProperties.formatFeatures;
    AHardwareBuffer_Desc hardwareBufferDesc;

    // region Ensure HardwareBuffer Support
    if (hardwareBufferFormatProperties.format != VK_FORMAT_UNDEFINED) {

        ExternalFormatProperties hardwareBufferExternalFormatProperties;
        if (!getExternalFormatProperties(hardwareBufferFormatProperties.format, &hardwareBufferExternalFormatProperties)) {
            return;
        }

        if ((hardwareBufferExternalFormatProperties.memoryProperties.compatibleHandleTypes
                & VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID)
                    != VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID) {
            LogE("Cannot use hardware buffer as it is not of a device-compatible external memory handle type.");
            // TODO: Signal fatal error to C#
            return;
        }

        if ((hardwareBufferExternalFormatProperties.memoryProperties.externalMemoryFeatures
                & VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT)
                    != VK_EXTERNAL_MEMORY_FEATURE_IMPORTABLE_BIT) {
            LogE("Cannot use hardware buffer as it cannot be imported.");
            // TODO: Signal fatal error to C#
            return;
        }

        hardwareBufferFormatFeatures = hardwareBufferExternalFormatProperties.formatFeatures;
    }

    if ((hardwareBufferFormatFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT) != VK_FORMAT_FEATURE_SAMPLED_IMAGE_BIT) {
        LogE("Cannot use hardware buffer as it does not support sampling.");
        // TODO: Signal fatal error to C#
        return;
    }

    AHardwareBuffer_describe(hardwareBuffer, &hardwareBufferDesc);
    if ((hardwareBufferDesc.usage & AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE)
            != AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE) {

        LogE("Cannot use hardware buffer as it is not configured for GPU sampling.");
        // TODO: Signal fatal error to C#
        return;
    }

    // endregion

    // region Acquire Vulkan Resources, Release HardwareBuffer
    UnityVulkanImage targetImage;
    if (!unityVulkan->AccessTexture(
            data->dstImage, UnityVulkanWholeImage,
            VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL, VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
            VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT, kUnityVulkanResourceAccess_PipelineBarrier, &targetImage)) {

        LogE("Could not get UnityVulkanImage from target image.");
        return;
    }

    const YuvGraphicsPipeline* graphicsPipeline = getGraphicsPipeline(
            hardwareBufferFormatProperties,
            hardwareBufferFormatFeatures,
            targetImage.format
    );

    if (graphicsPipeline == nullptr) {
        return;
    }

    auto submission = std::make_unique<RenderSubmission>(unityVulkanInstance.device);
    submission->srcImage = importHardwareBufferImage(
            hardwareBufferProperties,
            hardwareBufferFormatProperties,
            hardwareBufferDesc,
            hardwareBuffer,
            graphicsPipeline->conversion
    );

    if (!submission->srcImage) {
        return;
    }

    scopeExit.buffer = nullptr;
    AHardwareBuffer_release(hardwareBuffer);
    // endregion

    // Start recording CommandBuffer
    UnityVulkanRecordingState recording;
    if (!unityVulkan->CommandRecordingState(&recording, kUnityVulkanGraphicsQueueAccess_DontCare)) {
        LogE("Could not access Unity CommandBuffer.");
        return;
    }

    pruneOngoingSubmissions(recording.safeFrameNumber);

    // region Update Image Layout
    VkImageMemoryBarrier imageMemoryBarrier = {
            .sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
            .pNext = nullptr,
            .srcAccessMask = 0,
            .dstAccessMask = VK_ACCESS_SHADER_READ_BIT,
            .oldLayout = VK_IMAGE_LAYOUT_UNDEFINED,
            .newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
            .srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            .dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            .image = submission->srcImage->image,
            .subresourceRange = VkImageSubresourceRange {
                    .aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                    .baseMipLevel = 0, .levelCount = 1,
                    .baseArrayLayer = 0, .layerCount = 1,
            }
    };

    vkCmdPipelineBarrier(recording.commandBuffer,
                         VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                         VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0,
                         0, nullptr,
                         0, nullptr,
                         1, &imageMemoryBarrier);
    // endregion

    // region Allocate and Write Descriptors
    VkDescriptorPool descriptorPool;
    if (!getDescriptorPool(&descriptorPool)) {
        return;
    }

    if (!allocateDescriptorSet(descriptorPool, graphicsPipeline->descriptorSetLayout, &submission->descriptorSet)) {
        return;
    }

    VkDescriptorImageInfo descriptorImageInfo = {
            .sampler = VK_NULL_HANDLE,
            .imageView = submission->srcImage->imageView,
            .imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
    };

    VkWriteDescriptorSet writeDescriptorSet = {
            .sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,
            .pNext = nullptr,
            .dstSet = submission->descriptorSet,
            .dstBinding = 0,
            .dstArrayElement = 0,
            .descriptorCount = 1,
            .descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,
            .pImageInfo = &descriptorImageInfo,
            .pBufferInfo = nullptr,
            .pTexelBufferView = nullptr,
    };

    vkUpdateDescriptorSets(unityVulkanInstance.device,
                           1, &writeDescriptorSet,
                           0, nullptr);
    // endregion

    // region Render

    if (!constructTargetImageView(targetImage, &submission->targetImageView)) {
        return;
    }

    VkRenderingAttachmentInfoKHR renderingAttachmentInfo = {
            .sType = VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO,
            .pNext = nullptr,
            .imageView = submission->targetImageView,
            .imageLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
            .resolveMode = VK_RESOLVE_MODE_NONE_KHR,
            .resolveImageView = VK_NULL_HANDLE,
            .resolveImageLayout = VK_IMAGE_LAYOUT_UNDEFINED,
            .loadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE,
            .storeOp = VK_ATTACHMENT_STORE_OP_STORE,
            .clearValue = VkClearValue {
                0.0f,
                0.0f,
                0.0f,
                1.0f
            }
    };

    VkRect2D viewRect = {
            .offset = VkOffset2D { .x = 0, .y = 0 },
            .extent = VkExtent2D {
                    .width = targetImage.extent.width,
                    .height = targetImage.extent.height
            }
    };

    VkRenderingInfoKHR renderingInfo = {
            .sType = VK_STRUCTURE_TYPE_RENDERING_INFO,
            .pNext = nullptr,
            .flags = 0,
            .renderArea = viewRect,
            .layerCount = 1,
            .viewMask = 0,
            .colorAttachmentCount = 1,
            .pColorAttachments = &renderingAttachmentInfo,
            .pDepthAttachment = nullptr,
            .pStencilAttachment = nullptr
    };

    vkCmdBeginRenderingKHR(recording.commandBuffer, &renderingInfo);
    vkCmdBindPipeline(recording.commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, graphicsPipeline->pipeline);

    VkViewport viewport = {
            .x = 0.0f, .y = 0.0f,
            .width = static_cast<float>(targetImage.extent.width),
            .height = static_cast<float>(targetImage.extent.height),
            .minDepth = 0.0f, .maxDepth = 1.0f
    };

    vkCmdSetViewport(recording.commandBuffer, 0,
                     1, &viewport);

    vkCmdSetScissor(recording.commandBuffer, 0,
                    1, &viewRect);

    vkCmdBindDescriptorSets(recording.commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS,
                            graphicsPipeline->pipelineLayout, 0,
                            1, &submission->descriptorSet,
                            0, nullptr);

    vkCmdDraw(recording.commandBuffer,
              3, 1,
              0, 0);

    vkCmdEndRenderingKHR(recording.commandBuffer);

    // endregion

    submission->frameNumber = recording.currentFrameNumber;
    ongoingSubmissions.push_back(std::move(submission));

    scopeExit.success = true;
}

bool VulkanRenderManager::getHardwareBufferProperties(AHardwareBuffer* hardwareBuffer,
                                                      VkAndroidHardwareBufferPropertiesANDROID* bufferProperties,
                                                      VkAndroidHardwareBufferFormatPropertiesANDROID* bufferFormatProperties) {

    VkAndroidHardwareBufferFormatPropertiesANDROID vkAndroidHardwareBufferFormatProperties = {
            .sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID,
            .pNext = nullptr
    };

    VkAndroidHardwareBufferPropertiesANDROID vkAndroidHardwareBufferProperties = {
            .sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID,
            .pNext = &vkAndroidHardwareBufferFormatProperties
    };

    VkResult vkResult = vkGetAndroidHardwareBufferPropertiesANDROID(unityVulkanInstance.device,
                                                                    hardwareBuffer,
                                                                    &vkAndroidHardwareBufferProperties);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not access hardware buffer properties due to error, code: %d", vkResult);
        return false;
    }

    *bufferProperties = vkAndroidHardwareBufferProperties;
    *bufferFormatProperties = vkAndroidHardwareBufferFormatProperties;
    return true;
}

bool VulkanRenderManager::getExternalFormatProperties(VkFormat format, ExternalFormatProperties* formatProperties) {

    auto cached = externalFormatProperties.find(format);
    if (cached != externalFormatProperties.end()) {
        *formatProperties = cached->second;
        return true;
    }

    VkFormatProperties2 vkFormatProperties = {
            .sType = VK_STRUCTURE_TYPE_FORMAT_PROPERTIES_2,
            .pNext = nullptr,
    };

    vkGetPhysicalDeviceFormatProperties2(unityVulkanInstance.physicalDevice, format, &vkFormatProperties);

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
        LogE("Could not get hardware buffer image format properties due to error, code: %d.", vkResult);
        return false;
    }

    auto it = (externalFormatProperties.emplace(format, ExternalFormatProperties{
        .formatFeatures = vkFormatProperties.formatProperties.optimalTilingFeatures,
        .memoryProperties = externalImageFormatProperties.externalMemoryProperties
    })).first;

    *formatProperties = it->second;
    return true;
}

const VulkanRenderManager::YuvGraphicsPipeline* VulkanRenderManager::getGraphicsPipeline(
        const VkAndroidHardwareBufferFormatPropertiesANDROID& bufferFormatProperties,
        VkFormatFeatureFlags bufferFormatFeatures, VkFormat targetFormat) {

    RenderPipelineKey key = RenderPipelineKey {
        bufferFormatProperties,
        targetFormat
    };

    auto cached = graphicsPipelines.find(key);
    if (cached != graphicsPipelines.end()) {
        return cached->second.get();
    }

    LogD("Creating graphics pipeline.");
    auto graphicsPipeline = std::make_unique<YuvGraphicsPipeline>(unityVulkanInstance.device);

    if (!constructYuvSampler(bufferFormatProperties, bufferFormatFeatures,
                             &graphicsPipeline->conversion, &graphicsPipeline->sampler)) {
        return nullptr;
    }

    if (!constructPipelineDescriptorSetLayout(graphicsPipeline->sampler, &graphicsPipeline->descriptorSetLayout)) {
        return nullptr;
    }

    if (!constructPipelineLayout(graphicsPipeline->descriptorSetLayout, &graphicsPipeline->pipelineLayout)) {
        return nullptr;
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
        return nullptr;
    }

    moduleCreateInfo.codeSize = render_frag_size;
    moduleCreateInfo.pCode = render_frag;

    VkShaderModule fragmentShaderModule;
    vkResult = vkCreateShaderModule(unityVulkanInstance.device, &moduleCreateInfo, nullptr, &fragmentShaderModule);
    if (vkResult != VK_SUCCESS) {
        vkDestroyShaderModule(unityVulkanInstance.device, vertexShaderModule, nullptr);
        LogE("Could not create fragment shader module due to error, code: %d.", vkResult);
        return nullptr;
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

    VkPipelineRenderingCreateInfoKHR pipelineRenderingCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO_KHR,
            .pNext = nullptr,
            .viewMask = 0,
            .colorAttachmentCount = 1,
            .pColorAttachmentFormats = &targetFormat,
            .depthAttachmentFormat = VK_FORMAT_UNDEFINED,
            .stencilAttachmentFormat = VK_FORMAT_UNDEFINED
    };

    VkGraphicsPipelineCreateInfo pipelineCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO,
            .pNext = &pipelineRenderingCreateInfo,
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
            .layout = graphicsPipeline->pipelineLayout,
            .renderPass = VK_NULL_HANDLE,
            .subpass = 0,
            .basePipelineHandle = VK_NULL_HANDLE,
            .basePipelineIndex = -1,
    };

    vkResult = vkCreateGraphicsPipelines(unityVulkanInstance.device, unityVulkanInstance.pipelineCache,
                                         1, &pipelineCreateInfo,
                                         nullptr, &graphicsPipeline->pipeline);

    vkDestroyShaderModule(unityVulkanInstance.device, fragmentShaderModule, nullptr);
    vkDestroyShaderModule(unityVulkanInstance.device, vertexShaderModule, nullptr);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not create graphics pipeline due to error, code: %d.", vkResult);
        return nullptr;
    }

    auto it = (graphicsPipelines.emplace(key, std::move(graphicsPipeline))).first;
    return it->second.get();
}

bool VulkanRenderManager::constructYuvSampler(
        const VkAndroidHardwareBufferFormatPropertiesANDROID& bufferFormatProperties,
        VkFormatFeatureFlags bufferFormatFeatures, VkSamplerYcbcrConversion* conversion,
        VkSampler* sampler) {

    bool useLinearFiltering = (bufferFormatFeatures & VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT)
                                    == VK_FORMAT_FEATURE_SAMPLED_IMAGE_YCBCR_CONVERSION_LINEAR_FILTER_BIT;

    VkFilter chromaFilter = useLinearFiltering ? VK_FILTER_LINEAR : VK_FILTER_NEAREST;

    VkExternalFormatANDROID externalFormatAndroid = {
            .sType = VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID,
            .pNext = nullptr,
            .externalFormat = useExternalFormat(bufferFormatProperties)
                    ? bufferFormatProperties.externalFormat : 0
    };

    VkSamplerYcbcrConversionCreateInfo createInfo = {
            .sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_CREATE_INFO,
            .pNext = &externalFormatAndroid,
            .format = bufferFormatProperties.format,
            .ycbcrModel = bufferFormatProperties.suggestedYcbcrModel,
            .ycbcrRange = bufferFormatProperties.suggestedYcbcrRange,
            .components = bufferFormatProperties.samplerYcbcrConversionComponents,
            .xChromaOffset = bufferFormatProperties.suggestedXChromaOffset,
            .yChromaOffset = bufferFormatProperties.suggestedYChromaOffset,
            .chromaFilter = chromaFilter,
            .forceExplicitReconstruction = VK_FALSE,
    };

    VkSamplerYcbcrConversion vkCreatedConversion;
    VkResult vkResult = vkCreateSamplerYcbcrConversion(unityVulkanInstance.device, &createInfo,
                                                       nullptr, &vkCreatedConversion);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not create sampler YUV conversion due to error, code: %d.", vkResult);
        return false;
    }

    VkSamplerYcbcrConversionInfo yuvConversionInfo = {
            .sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO,
            .pNext = nullptr,
            .conversion = vkCreatedConversion
    };

    VkSamplerCreateInfo samplerCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO,
            .pNext = &yuvConversionInfo,
            .flags = 0,
            .magFilter = chromaFilter,
            .minFilter = chromaFilter,
            .mipmapMode = useLinearFiltering ? VK_SAMPLER_MIPMAP_MODE_LINEAR
                                             : VK_SAMPLER_MIPMAP_MODE_NEAREST,
            .addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
            .addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
            .addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE,
            .mipLodBias = 0.0f,
            .anisotropyEnable = VK_FALSE,
            .maxAnisotropy = 1.0f,
            .compareEnable = VK_FALSE,
            .compareOp = VK_COMPARE_OP_NEVER,
            .minLod = 0.0f,
            .maxLod = 0.0f,
            .borderColor = VK_BORDER_COLOR_FLOAT_OPAQUE_WHITE,
            .unnormalizedCoordinates = VK_FALSE
    };

    vkResult = vkCreateSampler(unityVulkanInstance.device, &samplerCreateInfo,
                               nullptr, sampler);

    if (vkResult != VK_SUCCESS) {
        vkDestroySamplerYcbcrConversion(unityVulkanInstance.device, vkCreatedConversion, nullptr);
        LogE("Could not create sampler due to error, code: %d.", vkResult);
        return false;
    }

    *conversion = vkCreatedConversion;
    return true;
}

bool VulkanRenderManager::constructPipelineDescriptorSetLayout(VkSampler sampler,
                                                               VkDescriptorSetLayout* descriptorSetLayout) {

    VkDescriptorSetLayoutBinding descriptorSetLayoutBinding = {
            .binding = 0,
            .descriptorType = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,
            .descriptorCount = 1,
            .stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT,
            .pImmutableSamplers = &sampler
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

    VkResult vkResult = vkCreateDescriptorSetLayout(unityVulkanInstance.device, &descriptorSetLayoutCreateInfo,
                                                    nullptr, descriptorSetLayout);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not create descriptor set layout for graphics pipeline due to error, code: %d.", vkResult);
        return false;
    }

    return true;
}

bool VulkanRenderManager::constructPipelineLayout(VkDescriptorSetLayout descriptorSetLayout,
                                                  VkPipelineLayout* pipelineLayout) {

    VkPipelineLayoutCreateInfo layoutCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO,
            .pNext = nullptr,
            .flags = 0,
            .setLayoutCount = 1,
            .pSetLayouts = &descriptorSetLayout,
            .pushConstantRangeCount = 0,
            .pPushConstantRanges = nullptr
    };

    VkResult vkResult = vkCreatePipelineLayout(unityVulkanInstance.device, &layoutCreateInfo,
                                               nullptr, pipelineLayout);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not create graphics pipeline layout due to error, code: %d.", vkResult);
        return false;
    }

    return true;
}

std::unique_ptr<VulkanRenderManager::ImportedYuvImage>
    VulkanRenderManager::importHardwareBufferImage(const VkAndroidHardwareBufferPropertiesANDROID& bufferProperties,
                                                   const VkAndroidHardwareBufferFormatPropertiesANDROID& bufferFormatProperties,
                                                   const AHardwareBuffer_Desc& hardwareBufferDesc,
                                                   AHardwareBuffer* hardwareBuffer,
                                                   VkSamplerYcbcrConversion samplerConversion) {

    auto importedImage = std::make_unique<ImportedYuvImage>(unityVulkanInstance.device);

    VkExternalFormatANDROID externalFormatAndroid = {
            .sType = VK_STRUCTURE_TYPE_EXTERNAL_FORMAT_ANDROID,
            .pNext = nullptr,
            .externalFormat = useExternalFormat(bufferFormatProperties)
                                ? bufferFormatProperties.externalFormat : 0
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

    VkResult vkResult = vkCreateImage(unityVulkanInstance.device, &imageCreateInfo,
                                      nullptr, &importedImage->image);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not create VkImage for hardware buffer due to error, code: %d", vkResult);
        return nullptr;
    }

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

        LogE("Could not get memory type index to allocate memory for hardware buffer.");
        return nullptr;
    }

    vkResult = vkAllocateMemory(unityVulkanInstance.device, &memoryAllocateInfo,
                                nullptr, &importedImage->memory);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not allocate memory for hardware buffer due to error, code: %d.", vkResult);
        return nullptr;
    }

    VkBindImageMemoryInfo bindImageMemoryInfo = {
            .sType = VK_STRUCTURE_TYPE_BIND_IMAGE_MEMORY_INFO,
            .pNext = nullptr,
            .image = importedImage->image,
            .memory = importedImage->memory,
            .memoryOffset = 0
    };

    vkResult = vkBindImageMemory2(unityVulkanInstance.device, 1, &bindImageMemoryInfo);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not bind hardware buffer image and memory due to error, code: %d", vkResult);
        return nullptr;
    }

    VkSamplerYcbcrConversionInfo yuvConversionInfo = {
            .sType = VK_STRUCTURE_TYPE_SAMPLER_YCBCR_CONVERSION_INFO,
            .pNext = nullptr,
            .conversion = samplerConversion
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

    vkResult = vkCreateImageView(unityVulkanInstance.device, &imageViewCreateInfo,
                                 nullptr, &importedImage->imageView);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not create image view for srcHardwareBuffer due to error, code: %d.", vkResult);
        return nullptr;
    }

    return importedImage;
}

bool VulkanRenderManager::getMemoryTypeIndex(uint32_t supportedMemTypes, VkFlags requiredMemProperties,
                                             uint32_t* memTypeIndex) {

    if (!deviceMemoryProperties) {

        VkPhysicalDeviceMemoryProperties2 properties = {
                .sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MEMORY_PROPERTIES_2,
                .pNext = nullptr,
        };

        vkGetPhysicalDeviceMemoryProperties2(unityVulkanInstance.physicalDevice, &properties);
        deviceMemoryProperties = properties;
    }

    VkPhysicalDeviceMemoryProperties* memoryProperties = &deviceMemoryProperties->memoryProperties;
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

void VulkanRenderManager::pruneOngoingSubmissions(unsigned long long safeFrame) {

    if (ongoingSubmissions.empty()) {
        return;
    }

    VkDescriptorPool descriptorPool;
    if (!getDescriptorPool(&descriptorPool)) {
        return;
    }

    for (auto it = ongoingSubmissions.begin(); it != ongoingSubmissions.end();) {

        RenderSubmission* submission = it->get();

        if (submission->frameNumber <= safeFrame) {
            if (submission->freeDescriptorSet(descriptorPool)) {
                it = ongoingSubmissions.erase(it);
                continue;
            }
        }

        ++it;
    }
}

bool VulkanRenderManager::getDescriptorPool(VkDescriptorPool* descriptorPool) {

    if (submissionsDescriptorPool != VK_NULL_HANDLE) {
        *descriptorPool = submissionsDescriptorPool;
        return true;
    }

    constexpr uint32_t maxSamplers = 60;
    VkDescriptorPoolSize poolSize = {
            .type = VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER,
            .descriptorCount = 3 * maxSamplers
    };

    VkDescriptorPoolCreateInfo poolCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO,
            .pNext = nullptr,
            .flags = VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT,
            .maxSets = maxSamplers,
            .poolSizeCount = 1,
            .pPoolSizes = &poolSize
    };

    VkDescriptorPool vkCreatedDescriptorPool;
    VkResult vkResult = vkCreateDescriptorPool(unityVulkanInstance.device, &poolCreateInfo,
                                               nullptr, &vkCreatedDescriptorPool);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not create descriptor pool due to error, code: %d.", vkResult);
        return false;
    }

    *descriptorPool = submissionsDescriptorPool = vkCreatedDescriptorPool;
    return true;
}

bool VulkanRenderManager::allocateDescriptorSet(VkDescriptorPool descriptorPool,
                                                VkDescriptorSetLayout descriptorSetLayout,
                                                VkDescriptorSet* descriptorSet) {

    VkDescriptorSetAllocateInfo allocateInfo = {
            .sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO,
            .pNext = nullptr,
            .descriptorPool = descriptorPool,
            .descriptorSetCount = 1,
            .pSetLayouts = &descriptorSetLayout
    };

    VkResult vkResult = vkAllocateDescriptorSets(unityVulkanInstance.device, &allocateInfo, descriptorSet);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not allocate descriptor set due to error, code: %d.", vkResult);
        return false;
    }

    return true;
}

bool VulkanRenderManager::constructTargetImageView(const UnityVulkanImage& image, VkImageView* imageView) {

    VkImageViewUsageCreateInfo imageViewUsageCreateInfo = {
            .sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_USAGE_CREATE_INFO,
            .pNext = nullptr,
            .usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT
    };

    VkImageViewCreateInfo createInfo = {
            .sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,
            .pNext = &imageViewUsageCreateInfo,
            .flags = 0,
            .image = image.image,
            .viewType = VK_IMAGE_VIEW_TYPE_2D,
            .format = image.format,
            .components = {
                .r = VK_COMPONENT_SWIZZLE_IDENTITY,
                .g = VK_COMPONENT_SWIZZLE_IDENTITY,
                .b = VK_COMPONENT_SWIZZLE_IDENTITY,
                .a = VK_COMPONENT_SWIZZLE_IDENTITY,
            },
            .subresourceRange = VkImageSubresourceRange {
                    .aspectMask = VK_IMAGE_ASPECT_COLOR_BIT,
                    .baseMipLevel = 0, .levelCount = 1,
                    .baseArrayLayer = 0, .layerCount = 1,
            }
    };

    VkResult vkResult = vkCreateImageView(unityVulkanInstance.device, &createInfo,
                                          nullptr, imageView);

    if (vkResult != VK_SUCCESS) {
        LogE("Could not create image view for target image due to error, code: %d.", vkResult);
        return false;
    }

    return true;
}