#include "basis_mediastream_internal.h"
#include <stdlib.h>
#include <string.h>

static void set_error(basis_mediastream_publisher_t* pub, const char* msg) {
    if (!pub) return;
    if (!msg) { pub->last_error[0] = '\0'; return; }
    strncpy(pub->last_error, msg, sizeof(pub->last_error) - 1);
    pub->last_error[sizeof(pub->last_error) - 1] = '\0';
}

BASIS_MS_API uint32_t BASIS_MS_CALL basis_mediastream_get_available_backends(void) {
    uint32_t mask = 0;
#if defined(_WIN32)
    mask |= BASIS_MEDIASTREAM_BACKEND_SPOUT2_BIT;
#endif
    return mask;
}

BASIS_MS_API int BASIS_MS_CALL basis_mediastream_resolve_backend(int requested_backend) {
    uint32_t available = basis_mediastream_get_available_backends();
    if (requested_backend == BASIS_MEDIASTREAM_BACKEND_AUTO) {
        if (available & BASIS_MEDIASTREAM_BACKEND_SPOUT2_BIT)
            return BASIS_MEDIASTREAM_BACKEND_SPOUT2;
        return 0;
    }
    switch (requested_backend) {
        case BASIS_MEDIASTREAM_BACKEND_SPOUT2:
            return (available & BASIS_MEDIASTREAM_BACKEND_SPOUT2_BIT) ? BASIS_MEDIASTREAM_BACKEND_SPOUT2 : 0;
        case BASIS_MEDIASTREAM_BACKEND_SYPHON:
            return (available & BASIS_MEDIASTREAM_BACKEND_SYPHON_BIT) ? BASIS_MEDIASTREAM_BACKEND_SYPHON : 0;
        case BASIS_MEDIASTREAM_BACKEND_PIPEWIRE:
            return (available & BASIS_MEDIASTREAM_BACKEND_PIPEWIRE_BIT) ? BASIS_MEDIASTREAM_BACKEND_PIPEWIRE : 0;
        default:
            return 0;
    }
}

BASIS_MS_API basis_mediastream_publisher_t* BASIS_MS_CALL
basis_mediastream_publisher_create(int backend, const char* stream_name, int width, int height) {
    int resolved = basis_mediastream_resolve_backend(backend);
    if (resolved == 0) return NULL;

    basis_mediastream_publisher_t* pub = (basis_mediastream_publisher_t*)calloc(1, sizeof(*pub));
    if (!pub) return NULL;

    pub->requested_backend = backend;
    pub->active_backend = resolved;
    pub->width = width > 0 ? width : 1;
    pub->height = height > 0 ? height : 1;
    if (stream_name && stream_name[0])
        strncpy(pub->stream_name, stream_name, sizeof(pub->stream_name) - 1);

    int ok = 0;
    switch (resolved) {
        case BASIS_MEDIASTREAM_BACKEND_SPOUT2:
            ok = basis_mediastream_spout_create(pub);
            break;
        default:
            ok = 0;
            break;
    }

    if (!ok) {
        set_error(pub, "backend create failed");
        basis_mediastream_publisher_destroy(pub);
        return NULL;
    }
    return pub;
}

BASIS_MS_API void BASIS_MS_CALL basis_mediastream_publisher_destroy(basis_mediastream_publisher_t* publisher) {
    if (!publisher) return;
    switch (publisher->active_backend) {
        case BASIS_MEDIASTREAM_BACKEND_SPOUT2:
            basis_mediastream_spout_destroy(publisher);
            break;
        default:
            break;
    }
    free(publisher);
}

BASIS_MS_API void BASIS_MS_CALL
basis_mediastream_publisher_set_texture(basis_mediastream_publisher_t* publisher,
                                        void* native_texture, int width, int height) {
    if (!publisher) return;
    publisher->pending_texture = native_texture;
    publisher->pending_width = width > 0 ? width : publisher->width;
    publisher->pending_height = height > 0 ? height : publisher->height;
    if (width > 0) publisher->width = width;
    if (height > 0) publisher->height = height;
}

BASIS_MS_API void BASIS_MS_CALL
basis_mediastream_publisher_update(basis_mediastream_publisher_t* publisher) {
    if (!publisher || !publisher->pending_texture) return;
    switch (publisher->active_backend) {
        case BASIS_MEDIASTREAM_BACKEND_SPOUT2:
            basis_mediastream_spout_update(publisher,
                publisher->pending_texture,
                publisher->pending_width,
                publisher->pending_height);
            break;
        default:
            break;
    }
}

BASIS_MS_API int BASIS_MS_CALL
basis_mediastream_publisher_get_active_backend(basis_mediastream_publisher_t* publisher) {
    return publisher ? publisher->active_backend : 0;
}

BASIS_MS_API int BASIS_MS_CALL
basis_mediastream_publisher_get_last_error(basis_mediastream_publisher_t* publisher,
                                           char* buf, int buf_size) {
    if (!publisher || !buf || buf_size <= 0) return 0;
    strncpy(buf, publisher->last_error, (size_t)buf_size - 1);
    buf[buf_size - 1] = '\0';
    return (int)strlen(buf);
}
