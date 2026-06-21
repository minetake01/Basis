#include "basis_luau_internal.h"

#include <stdlib.h>
#include <string.h>

typedef struct basis_luau_buffer_slot {
    uint32_t generation;
    uint32_t host_id;
    uint32_t length;
    uint32_t pin_count;
    uint8_t* bytes;
} basis_luau_buffer_slot;

typedef struct basis_luau_buffer_pool {
    basis_luau_buffer_slot* slots;
    uint32_t capacity;
    uint32_t host_buffer_bytes;
    uint32_t host_buffer_count;
    basis_luau_host_quota quota;
} basis_luau_buffer_pool;

int basis_luau_buffer_pool_create(basis_luau_buffer_pool** out_pool, uint32_t capacity, const basis_luau_host_quota* quota)
{
    if (!out_pool || capacity == 0) {
        return 0;
    }
    basis_luau_buffer_pool* pool = (basis_luau_buffer_pool*)calloc(1, sizeof(*pool));
    if (!pool) {
        return 0;
    }
    pool->slots = (basis_luau_buffer_slot*)calloc(capacity, sizeof(basis_luau_buffer_slot));
    if (!pool->slots) {
        free(pool);
        return 0;
    }
    pool->capacity = capacity;
    if (quota) {
        pool->quota = *quota;
    }
    *out_pool = pool;
    return 1;
}

void basis_luau_buffer_pool_destroy(basis_luau_buffer_pool* pool)
{
    if (!pool) {
        return;
    }
    for (uint32_t i = 0; i < pool->capacity; ++i) {
        free(pool->slots[i].bytes);
    }
    free(pool->slots);
    free(pool);
}

int basis_luau_buffer_pool_alloc(
    basis_luau_buffer_pool* pool,
    uint32_t host_id,
    uint32_t length,
    basis_luau_buffer_ref* out_ref,
    uint8_t** out_bytes)
{
    if (!pool || !out_ref || !out_bytes || length == 0) {
        return 0;
    }
    if (length > pool->quota.max_payload_bytes) {
        return 0;
    }
    if (pool->host_buffer_bytes + length > pool->quota.max_buffer_bytes ||
        pool->host_buffer_count + 1 > pool->quota.max_buffer_count) {
        return 0;
    }

    for (uint32_t i = 0; i < pool->capacity; ++i) {
        basis_luau_buffer_slot* slot = &pool->slots[i];
        if (slot->bytes != NULL && slot->pin_count > 0) {
            continue;
        }

        if (slot->bytes == NULL) {
            slot->bytes = (uint8_t*)malloc(length);
            if (!slot->bytes) {
                return 0;
            }
            slot->length = length;
        } else if (slot->length < length) {
            uint8_t* next = (uint8_t*)realloc(slot->bytes, length);
            if (!next) {
                return 0;
            }
            slot->bytes = next;
            slot->length = length;
        }

        slot->generation += 1u;
        slot->host_id = host_id;
        slot->pin_count = 1;
        pool->host_buffer_bytes += length;
        pool->host_buffer_count += 1;

        out_ref->pool_id = 0;
        out_ref->slot = i;
        out_ref->generation = slot->generation;
        out_ref->host_id = host_id;
        out_ref->length = length;
        *out_bytes = slot->bytes;
        return 1;
    }
    return 0;
}

void basis_luau_buffer_pool_release(basis_luau_buffer_pool* pool, const basis_luau_buffer_ref* ref)
{
    if (!pool || !ref) {
        return;
    }
    if (ref->slot >= pool->capacity) {
        return;
    }
    basis_luau_buffer_slot* slot = &pool->slots[ref->slot];
    if (slot->generation != ref->generation || slot->host_id != ref->host_id) {
        return;
    }
    if (slot->pin_count > 0) {
        slot->pin_count -= 1;
    }
    if (slot->pin_count == 0 && pool->host_buffer_bytes >= slot->length) {
        pool->host_buffer_bytes -= slot->length;
        pool->host_buffer_count -= 1;
    }
}
