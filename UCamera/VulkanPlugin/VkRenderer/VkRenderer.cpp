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
#include <android/hardware_buffer.h>

VkRenderer::VkRenderer(IUnityGraphicsVulkan* unityVulkan) {
    this->unityVulkan = unityVulkan;
}

void VkRenderer::onDeviceInitialized() {

    UnityVulkanPluginEventConfig config = { };
    config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureInside;
    config.flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission | kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState;

    unityVulkan->ConfigureEvent(EVENT_ID_RENDER, &config);
    LogD("Events configured.");
}

void VkRenderer::onDeviceShutdown() {

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

    UnityVulkanInstance vkInstance = unityVulkan->Instance();
    VkAndroidHardwareBufferFormatProperties2ANDROID bufferFormatProperties;
    VkAndroidHardwareBufferPropertiesANDROID bufferProperties;
    VkResult vkResult;

    UnityVulkanImage uvkImage;
    if (data->dstImage == nullptr) {
        LogE("Provided dstImage is a nullptr, cannot render.");
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

    bufferFormatProperties = {
        .sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_2_ANDROID,
        .pNext = nullptr
    };

    bufferProperties = {
        .sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID,
        .pNext = &bufferFormatProperties
    };

    vkResult = vkGetAndroidHardwareBufferPropertiesANDROID(vkInstance.device, hardwareBuffer, &bufferProperties);
    if (vkResult != VK_SUCCESS) {
        LogE("Could not access srcHardwareBuffer properties due to error, code: %d", vkResult);
        goto renderEnd;
    }

renderEnd:
    AHardwareBuffer_release(hardwareBuffer);
    if (data->onDone) {
        data->onDone(data->srcHardwareBufferId);
    }
}
