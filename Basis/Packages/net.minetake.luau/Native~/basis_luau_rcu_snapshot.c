#include "basis_luau_internal.h"

#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <stdatomic.h>
#endif

typedef struct basis_luau_rcu_snapshot {
    basis_luau_snapshot_slot* buffers[3];
    uint32_t capacity;
    uint64_t publish_epoch;
#if defined(_WIN32)
    volatile LONG reader_count;
#else
    atomic_uint_least32_t reader_count;
#endif
    int write_index;
    int read_index;
} basis_luau_rcu_snapshot;

int basis_luau_rcu_snapshot_create(basis_luau_rcu_snapshot** out_snap, uint32_t capacity)
{
    if (!out_snap || capacity == 0) {
        return 0;
    }
    basis_luau_rcu_snapshot* snap = (basis_luau_rcu_snapshot*)calloc(1, sizeof(*snap));
    if (!snap) {
        return 0;
    }
    snap->capacity = capacity;
    for (int i = 0; i < 3; ++i) {
        snap->buffers[i] = (basis_luau_snapshot_slot*)calloc(capacity, sizeof(basis_luau_snapshot_slot));
        if (!snap->buffers[i]) {
            for (int j = 0; j < i; ++j) {
                free(snap->buffers[j]);
            }
            free(snap);
            return 0;
        }
    }
    *out_snap = snap;
    return 1;
}

void basis_luau_rcu_snapshot_destroy(basis_luau_rcu_snapshot* snap)
{
    if (!snap) {
        return;
    }
    for (int i = 0; i < 3; ++i) {
        free(snap->buffers[i]);
    }
    free(snap);
}

int basis_luau_rcu_snapshot_publish_begin(basis_luau_rcu_snapshot* snap, uint64_t* out_epoch)
{
    if (!snap) {
        return 0;
    }
#if defined(_WIN32)
    if (snap->reader_count != 0) {
        return 0;
    }
#else
    if (atomic_load_explicit(&snap->reader_count, memory_order_acquire) != 0) {
        return 0;
    }
#endif
    snap->write_index = (snap->write_index + 1) % 3;
    snap->read_index = (snap->write_index + 2) % 3;
    snap->publish_epoch += 1;
    if (out_epoch) {
        *out_epoch = snap->publish_epoch;
    }
    return 1;
}

void basis_luau_rcu_snapshot_publish_end(basis_luau_rcu_snapshot* snap, uint64_t epoch)
{
    (void)snap;
    (void)epoch;
}

int basis_luau_rcu_snapshot_read_begin(basis_luau_rcu_snapshot* snap, uint64_t* out_epoch)
{
    if (!snap) {
        return 0;
    }
#if defined(_WIN32)
    InterlockedIncrement(&snap->reader_count);
#else
    atomic_fetch_add_explicit(&snap->reader_count, 1, memory_order_acq_rel);
#endif
    if (out_epoch) {
        *out_epoch = snap->publish_epoch;
    }
    return 1;
}

int basis_luau_rcu_snapshot_read_slot(basis_luau_rcu_snapshot* snap, uint32_t slot_index, basis_luau_snapshot_slot* out_slot)
{
    if (!snap || !out_slot || slot_index >= snap->capacity) {
        return 0;
    }
    memcpy(out_slot, &snap->buffers[snap->read_index][slot_index], sizeof(*out_slot));
    return 1;
}

void basis_luau_rcu_snapshot_read_end(basis_luau_rcu_snapshot* snap, uint64_t epoch)
{
    (void)epoch;
    if (!snap) {
        return;
    }
#if defined(_WIN32)
    InterlockedDecrement(&snap->reader_count);
#else
    atomic_fetch_sub_explicit(&snap->reader_count, 1, memory_order_acq_rel);
#endif
}

int basis_luau_rcu_snapshot_write_slot(basis_luau_rcu_snapshot* snap, uint32_t slot_index, const basis_luau_snapshot_slot* slot)
{
    if (!snap || !slot || slot_index >= snap->capacity) {
        return 0;
    }
    memcpy(&snap->buffers[snap->write_index][slot_index], slot, sizeof(*slot));
    return 1;
}
