/*
 * basis_mediastream_native — flat C ABI for publishing GPU textures to external apps.
 *
 * Backends (selected at create time):
 *   BASIS_MEDIASTREAM_SPOUT2   — Windows Spout2 / DXGI shared texture (v1)
 *   BASIS_MEDIASTREAM_SYPHON   — macOS Syphon (stub / future)
 *   BASIS_MEDIASTREAM_PIPEWIRE — Linux PipeWire (stub / future)
 *
 * Threading:
 *   - create/destroy/set_texture are main-thread safe.
 *   - publisher_update runs on the Unity render thread only (via render event).
 *
 * License: MIT (this wrapper). Spout2 is BSD-2-Clause when the Spout2 backend is linked.
 */

#ifndef BASIS_MEDIASTREAM_NATIVE_H
#define BASIS_MEDIASTREAM_NATIVE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) || defined(_WIN64)
  #define BASIS_MS_API __declspec(dllexport)
  #define BASIS_MS_CALL __stdcall
#else
  #define BASIS_MS_API __attribute__((visibility("default")))
  #define BASIS_MS_CALL
#endif

typedef struct basis_mediastream_publisher basis_mediastream_publisher_t;

typedef enum basis_mediastream_backend {
    BASIS_MEDIASTREAM_BACKEND_AUTO     = 0,
    BASIS_MEDIASTREAM_BACKEND_SPOUT2   = 1,
    BASIS_MEDIASTREAM_BACKEND_SYPHON   = 2,
    BASIS_MEDIASTREAM_BACKEND_PIPEWIRE = 3
} basis_mediastream_backend_t;

typedef enum basis_mediastream_render_op {
    BASIS_MEDIASTREAM_RENDER_UPDATE  = 1,
    BASIS_MEDIASTREAM_RENDER_RELEASE = 2
} basis_mediastream_render_op_t;

#define BASIS_MEDIASTREAM_BACKEND_SPOUT2_BIT   (1u << 0)
#define BASIS_MEDIASTREAM_BACKEND_SYPHON_BIT   (1u << 1)
#define BASIS_MEDIASTREAM_BACKEND_PIPEWIRE_BIT (1u << 2)

/* Bitmask of backends compiled into this binary. */
BASIS_MS_API uint32_t BASIS_MS_CALL basis_mediastream_get_available_backends(void);

/* Resolve Auto to the best backend for the current platform/GPU. Returns 0 if none. */
BASIS_MS_API int BASIS_MS_CALL basis_mediastream_resolve_backend(int requested_backend);

BASIS_MS_API basis_mediastream_publisher_t* BASIS_MS_CALL
basis_mediastream_publisher_create(int backend, const char* stream_name, int width, int height);

BASIS_MS_API void BASIS_MS_CALL basis_mediastream_publisher_destroy(basis_mediastream_publisher_t* publisher);

/* Main thread: queue the native texture pointer for the next render-thread update. */
BASIS_MS_API void BASIS_MS_CALL
basis_mediastream_publisher_set_texture(basis_mediastream_publisher_t* publisher,
                                        void* native_texture, int width, int height);

/* Render thread: publish the queued texture. */
BASIS_MS_API void BASIS_MS_CALL
basis_mediastream_publisher_update(basis_mediastream_publisher_t* publisher);

BASIS_MS_API int BASIS_MS_CALL
basis_mediastream_publisher_get_active_backend(basis_mediastream_publisher_t* publisher);

BASIS_MS_API int BASIS_MS_CALL
basis_mediastream_publisher_get_last_error(basis_mediastream_publisher_t* publisher,
                                           char* buf, int buf_size);

typedef void (BASIS_MS_CALL *basis_mediastream_render_event_func)(int event_id, void* data);

BASIS_MS_API basis_mediastream_render_event_func BASIS_MS_CALL
basis_mediastream_get_render_event_func(void);

#ifdef __cplusplus
}
#endif

#endif /* BASIS_MEDIASTREAM_NATIVE_H */
