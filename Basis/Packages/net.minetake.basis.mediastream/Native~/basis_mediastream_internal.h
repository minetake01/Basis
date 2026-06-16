#ifndef BASIS_MEDIASTREAM_INTERNAL_H
#define BASIS_MEDIASTREAM_INTERNAL_H

#include "basis_mediastream_native.h"

#ifdef __cplusplus
extern "C" {
#endif

#ifdef __cplusplus
extern "C" {
#endif

void* basis_mediastream_get_d3d11_device(void);

#ifdef __cplusplus
}
#endif

struct basis_mediastream_publisher {
    int requested_backend;
    int active_backend;
    int width;
    int height;
    char stream_name[256];
    void* pending_texture;
    int pending_width;
    int pending_height;
    char last_error[512];
#if defined(_WIN32)
    void* spout_sender; /* spoutDX* — opaque to C callers */
#endif
};

int basis_mediastream_spout_create(basis_mediastream_publisher_t* pub);
void basis_mediastream_spout_destroy(basis_mediastream_publisher_t* pub);
void basis_mediastream_spout_update(basis_mediastream_publisher_t* pub, void* texture, int w, int h);

#ifdef __cplusplus
}
#endif

#endif /* BASIS_MEDIASTREAM_INTERNAL_H */
