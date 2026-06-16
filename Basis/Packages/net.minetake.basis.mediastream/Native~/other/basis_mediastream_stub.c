#include "../basis_mediastream_internal.h"

int basis_mediastream_spout_create(basis_mediastream_publisher_t* pub) {
    if (pub)
        strncpy(pub->last_error, "Spout2 backend not built for this platform", sizeof(pub->last_error) - 1);
    return 0;
}

void basis_mediastream_spout_destroy(basis_mediastream_publisher_t* pub) { (void)pub; }

void basis_mediastream_spout_update(basis_mediastream_publisher_t* pub, void* texture, int w, int h) {
    (void)pub; (void)texture; (void)w; (void)h;
}
