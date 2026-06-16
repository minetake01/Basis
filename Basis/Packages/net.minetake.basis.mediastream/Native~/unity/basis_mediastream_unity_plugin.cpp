/*
 * basis_mediastream_unity_plugin.cpp — Unity native plugin glue for media stream publishing.
 */

#include "../basis_mediastream_native.h"
#include "../basis_mediastream_internal.h"

#include "IUnityInterface.h"
#include "IUnityGraphics.h"

#if defined(_WIN32)
  #include <d3d11.h>
  #include "IUnityGraphicsD3D11.h"
#endif

static IUnityInterfaces* s_unity = nullptr;
static IUnityGraphics* s_graphics = nullptr;
static void* s_d3d11_device = nullptr;

extern "C" void* basis_mediastream_get_d3d11_device(void) {
    return s_d3d11_device;
}

static void capture_devices() {
    if (!s_graphics) return;
#if defined(_WIN32)
    if (s_graphics->GetRenderer() == kUnityGfxRendererD3D11) {
        IUnityGraphicsD3D11* d3d = s_unity ? s_unity->Get<IUnityGraphicsD3D11>() : nullptr;
        if (d3d)
            s_d3d11_device = d3d->GetDevice();
    }
#endif
}

static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType type) {
    if (type == kUnityGfxDeviceEventInitialize)
        capture_devices();
    if (type == kUnityGfxDeviceEventShutdown)
        s_d3d11_device = nullptr;
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
UnityPluginLoad(IUnityInterfaces* interfaces) {
    s_unity = interfaces;
    s_graphics = interfaces->Get<IUnityGraphics>();
    if (s_graphics) {
        s_graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
        OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
    }
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API
UnityPluginUnload() {
    if (s_graphics)
        s_graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
    s_graphics = nullptr;
    s_unity = nullptr;
    s_d3d11_device = nullptr;
}

static void BASIS_MS_CALL OnRenderEvent(int event_id, void* data) {
    auto* pub = (basis_mediastream_publisher_t*)data;
    if (!pub) return;
    if (event_id == BASIS_MEDIASTREAM_RENDER_UPDATE)
        basis_mediastream_publisher_update(pub);
    else if (event_id == BASIS_MEDIASTREAM_RENDER_RELEASE)
        basis_mediastream_publisher_destroy(pub);
}

extern "C" BASIS_MS_API basis_mediastream_render_event_func BASIS_MS_CALL
basis_mediastream_get_render_event_func(void) {
    return OnRenderEvent;
}
