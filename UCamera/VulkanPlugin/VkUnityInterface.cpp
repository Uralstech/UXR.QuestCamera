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

#include <android/log.h>
#include <memory>

#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsVulkan.h"
#include "VkRenderer/VkRenderer.h"

#define TAG "UXRQC.VkInterface"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)
#define LOGD(...) __android_log_print(ANDROID_LOG_DEBUG, TAG, __VA_ARGS__)

static std::unique_ptr<VkRenderer> s_renderer = nullptr;

static IUnityGraphics* s_unityGraphics = nullptr;
static IUnityGraphicsVulkanV2* s_unityVulkanV2 = nullptr;
static IUnityGraphicsVulkan* s_unityVulkanV1 = nullptr;
static bool s_isDeviceEventRegistered = false;

static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType);

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
    UnityPluginLoad(IUnityInterfaces* unityInterfaces) {

    LOGD("Loading plugin.");

    s_unityGraphics = unityInterfaces->Get<IUnityGraphics>();
    if (s_unityGraphics->GetRenderer() != kUnityGfxRendererNull) {
        LOGE("Missed graphics initialization, cannot continue.");
        return;
    }

    s_unityVulkanV2 = unityInterfaces->Get<IUnityGraphicsVulkanV2>();
    s_unityVulkanV1 = unityInterfaces->Get<IUnityGraphicsVulkan>();

    if (!s_unityVulkanV1) {
        LOGD("Could not retrieve Unity Vulkan interface, cannot continue.");
        return;
    }

    bool interceptAdded = false;
    if (s_unityVulkanV2) {
        interceptAdded = s_unityVulkanV2->AddInterceptInitialization(VkRenderer::hookVulkanInitialization, nullptr, 0);
    } else {
        interceptAdded = s_unityVulkanV1->InterceptInitialization(VkRenderer::hookVulkanInitialization, nullptr);
    }

    if (!interceptAdded) {
        LOGD("Could not register Vulkan initialization intercept, cannot continue.");
        return;
    }

    s_unityGraphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
    s_isDeviceEventRegistered = true;

    LOGD("Plugin loaded successfully.");
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
    UnityPluginUnload() {

    LOGD("Unloading plugin.");

    if (s_isDeviceEventRegistered) {
        s_unityGraphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
        s_isDeviceEventRegistered = false;
    }

    if (s_unityVulkanV2) {
        s_unityVulkanV2->RemoveInterceptInitialization(VkRenderer::hookVulkanInitialization);
    }

    s_unityVulkanV2 = nullptr;
    s_unityVulkanV1 = nullptr;
    s_unityGraphics = nullptr;
    LOGD("Plugin unloaded successfully.");
}

static void UNITY_INTERFACE_API
    OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType) {

    if (s_unityGraphics->GetRenderer() != kUnityGfxRendererVulkan) {
        LOGD("Received event for non-Vulkan graphics device, ignoring.");

        // Unity may invoke device events before the Vulkan renderer has been
        // fully initialized (GetRenderer() is still not Vulkan). During startup,
        // Unity also creates and destroys a temporary VkInstance before creating
        // the final renderer instance. We therefore ignore device events until
        // the active renderer reports Vulkan.
        return;
    }

    if (!VkRenderer::isVulkanSetup()) {
        LOGD("Renderer hooks not setup, cannot continue.");
        return;
    }

    switch (eventType) {
        case kUnityGfxDeviceEventInitialize:
            LOGD("Graphics device initialized.");
            if (s_renderer) return;

            s_renderer = std::make_unique<VkRenderer>(s_unityVulkanV1);
            s_renderer->onDeviceInitialized();
            break;

        case kUnityGfxDeviceEventShutdown:
            LOGD("Graphics device shut down.");
            if (!s_renderer) return;

            s_renderer->onDeviceShutdown();
            s_renderer.reset();
            break;

        default: break;
    }
}

static void UNITY_INTERFACE_API
    renderEvent(int eventId, void* data) {

    if (!s_renderer) {
        LOGE("Render event invoked with uninitialized renderer! (eventId: %i)", eventId);
        return;
    }

    switch (eventId) {
        case EVENT_ID_RENDER:
            s_renderer->render(reinterpret_cast<RenderData*>(data));
            break;

        default:
            LOGE("Unknown eventId '%i'.", eventId);
            break;
    }
}

extern "C" UnityRenderingEventAndData UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
    getVulkanRenderEvent() {
    return renderEvent;
}