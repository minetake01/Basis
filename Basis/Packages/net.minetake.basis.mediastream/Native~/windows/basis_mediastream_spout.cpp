#include "../basis_mediastream_internal.h"

#if defined(_WIN32)

#include "SpoutDX.h"
#include <d3d11.h>
#include <cstring>

extern void* basis_mediastream_get_d3d11_device(void);

int basis_mediastream_spout_create(basis_mediastream_publisher_t* pub) {
    if (!pub) return 0;

    auto* spout = new spoutDX();
    ID3D11Device* device = (ID3D11Device*)basis_mediastream_get_d3d11_device();
    if (!device || !spout->OpenDirectX11(device)) {
        delete spout;
        strncpy(pub->last_error, "Spout2: failed to open D3D11 device", sizeof(pub->last_error) - 1);
        return 0;
    }

    if (pub->stream_name[0])
        spout->SetSenderName(pub->stream_name);

    pub->spout_sender = spout;
    return 1;
}

void basis_mediastream_spout_destroy(basis_mediastream_publisher_t* pub) {
    if (!pub || !pub->spout_sender) return;
    auto* spout = (spoutDX*)pub->spout_sender;
    spout->ReleaseSender();
    spout->CloseDirectX11();
    delete spout;
    pub->spout_sender = nullptr;
}

void basis_mediastream_spout_update(basis_mediastream_publisher_t* pub, void* texture, int w, int h) {
    if (!pub || !pub->spout_sender || !texture) return;
    auto* spout = (spoutDX*)pub->spout_sender;
    auto* tex = (ID3D11Texture2D*)texture;
    if (!spout->SendTexture(tex)) {
        strncpy(pub->last_error, "Spout2: SendTexture failed", sizeof(pub->last_error) - 1);
    } else {
        pub->last_error[0] = '\0';
    }
    pub->width = w > 0 ? w : pub->width;
    pub->height = h > 0 ? h : pub->height;
}

#endif /* _WIN32 */
