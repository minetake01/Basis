#include "basis_luau_runtime.h"
#include "basis_luau_internal.h"

#include "luau_ffi_exports.h"

#include <stdlib.h>
#include <string.h>

typedef struct basis_luau_command_ring {
    basis_luau_command* items;
    uint32_t capacity;
    uint32_t head;
    uint32_t tail;
    uint32_t count;
} basis_luau_command_ring;

struct basis_luau_runtime {
    lua_State* root;
    basis_luau_command_ring ring;
    basis_luau_buffer_pool* buffer_pool;
    basis_luau_rcu_snapshot* snapshot;
    basis_luau_host_quota quota;
    int scheduler_running;
};

static int basis_luau_command_ring_init(basis_luau_command_ring* ring, uint32_t capacity)
{
    ring->items = (basis_luau_command*)calloc(capacity, sizeof(basis_luau_command));
    if (!ring->items) {
        return 0;
    }
    ring->capacity = capacity;
    return 1;
}

static void basis_luau_command_ring_destroy(basis_luau_command_ring* ring)
{
    free(ring->items);
    memset(ring, 0, sizeof(*ring));
}

static int basis_luau_command_registered(uint16_t type)
{
    switch ((basis_luau_command_type)type) {
    case basis_luau_cmd_set_position:
    case basis_luau_cmd_set_rotation:
    case basis_luau_cmd_set_local_position:
    case basis_luau_cmd_rotate:
    case basis_luau_cmd_destroy_object:
    case basis_luau_cmd_get_position_snapshot:
    case basis_luau_cmd_get_rotation_snapshot:
    case basis_luau_cmd_ticket_clone:
    case basis_luau_cmd_ticket_download_image:
    case basis_luau_cmd_event_network_message:
    case basis_luau_cmd_event_osc_message:
    case basis_luau_cmd_event_trigger_enter:
        return 1;
    default:
        return 0;
    }
}

BASIS_LUAU_API basis_luau_runtime* basis_luau_runtime_create(const basis_luau_limits_config* config, basis_luau_init_error* out_error)
{
    basis_luau_runtime* rt = (basis_luau_runtime*)calloc(1, sizeof(*rt));
    if (!rt) {
        if (out_error) {
            *out_error = basis_luau_init_ctx_alloc_failed;
        }
        return NULL;
    }

    rt->root = basis_luau_newstate_with_limits(config, out_error);
    if (!rt->root) {
        free(rt);
        return NULL;
    }

    rt->quota.max_commands_per_slice = 8192;
    rt->quota.max_command_bytes_per_slice = 256 * 1024;
    rt->quota.max_buffer_bytes = 4 * 1024 * 1024;
    rt->quota.max_buffer_count = 256;
    rt->quota.max_payload_bytes = 1024 * 1024;

    if (!basis_luau_command_ring_init(&rt->ring, rt->quota.max_commands_per_slice)) {
        basis_luau_runtime_destroy(rt);
        if (out_error) {
            *out_error = basis_luau_init_ctx_alloc_failed;
        }
        return NULL;
    }

    if (!basis_luau_buffer_pool_create(&rt->buffer_pool, 256, &rt->quota)) {
        basis_luau_runtime_destroy(rt);
        if (out_error) {
            *out_error = basis_luau_init_ctx_alloc_failed;
        }
        return NULL;
    }

    if (!basis_luau_rcu_snapshot_create(&rt->snapshot, 4096)) {
        basis_luau_runtime_destroy(rt);
        if (out_error) {
            *out_error = basis_luau_init_ctx_alloc_failed;
        }
        return NULL;
    }

    return rt;
}

BASIS_LUAU_API void basis_luau_runtime_destroy(basis_luau_runtime* rt)
{
    if (!rt) {
        return;
    }
    basis_luau_scheduler_shutdown(rt);
    if (rt->snapshot) {
        basis_luau_rcu_snapshot_destroy(rt->snapshot);
    }
    if (rt->buffer_pool) {
        basis_luau_buffer_pool_destroy(rt->buffer_pool);
    }
    basis_luau_command_ring_destroy(&rt->ring);
    if (rt->root) {
        ffi_lua_close(rt->root);
    }
    free(rt);
}

BASIS_LUAU_API lua_State* basis_luau_runtime_root_state(basis_luau_runtime* rt)
{
    return rt ? rt->root : NULL;
}

BASIS_LUAU_API int basis_luau_verify_signature(
    const uint8_t* payload,
    size_t payload_size,
    const uint8_t* signature,
    const uint8_t* key,
    size_t key_size)
{
    (void)payload;
    (void)payload_size;
    (void)key;
    (void)key_size;
    if (!signature) {
        return 0;
    }
    /* HMAC verified in managed layer; native keeps constant-time compare hook for future. */
    return 0;
}

BASIS_LUAU_API basis_luau_ring_result basis_luau_ring_try_push_command(basis_luau_runtime* rt, uint32_t host_id, const basis_luau_command* cmd)
{
    (void)host_id;
    if (!rt || !cmd) {
        return basis_luau_ring_invalid;
    }
    if (!basis_luau_command_registered(cmd->type)) {
        return basis_luau_ring_invalid;
    }
    if (rt->ring.count >= rt->ring.capacity) {
        return basis_luau_ring_full;
    }
    rt->ring.items[rt->ring.tail] = *cmd;
    rt->ring.tail = (rt->ring.tail + 1) % rt->ring.capacity;
    rt->ring.count += 1;
    return basis_luau_ring_ok;
}

BASIS_LUAU_API basis_luau_ring_result basis_luau_ring_try_pop_command(basis_luau_runtime* rt, basis_luau_command* out_cmd)
{
    if (!rt || !out_cmd) {
        return basis_luau_ring_invalid;
    }
    if (rt->ring.count == 0) {
        return basis_luau_ring_empty;
    }
    *out_cmd = rt->ring.items[rt->ring.head];
    rt->ring.head = (rt->ring.head + 1) % rt->ring.capacity;
    rt->ring.count -= 1;
    return basis_luau_ring_ok;
}

BASIS_LUAU_API int basis_luau_snapshot_publish_begin(basis_luau_runtime* rt, uint64_t* out_epoch)
{
    return rt ? basis_luau_rcu_snapshot_publish_begin(rt->snapshot, out_epoch) : 0;
}

BASIS_LUAU_API void basis_luau_snapshot_publish_end(basis_luau_runtime* rt, uint64_t epoch)
{
    if (rt) {
        basis_luau_rcu_snapshot_publish_end(rt->snapshot, epoch);
    }
}

BASIS_LUAU_API int basis_luau_snapshot_read_begin(basis_luau_runtime* rt, uint64_t* out_epoch)
{
    return rt ? basis_luau_rcu_snapshot_read_begin(rt->snapshot, out_epoch) : 0;
}

BASIS_LUAU_API void basis_luau_snapshot_read_end(basis_luau_runtime* rt, uint64_t epoch)
{
    if (rt) {
        basis_luau_rcu_snapshot_read_end(rt->snapshot, epoch);
    }
}

BASIS_LUAU_API int basis_luau_snapshot_write_slot(basis_luau_runtime* rt, uint32_t slot_index, const basis_luau_snapshot_slot* slot)
{
    return rt ? basis_luau_rcu_snapshot_write_slot(rt->snapshot, slot_index, slot) : 0;
}

BASIS_LUAU_API int basis_luau_buffer_alloc(
    basis_luau_runtime* rt,
    uint32_t host_id,
    uint32_t length,
    basis_luau_buffer_ref* out_ref,
    uint8_t** out_bytes)
{
    return rt ? basis_luau_buffer_pool_alloc(rt->buffer_pool, host_id, length, out_ref, out_bytes) : 0;
}

BASIS_LUAU_API void basis_luau_buffer_release(basis_luau_runtime* rt, const basis_luau_buffer_ref* ref)
{
    if (rt) {
        basis_luau_buffer_pool_release(rt->buffer_pool, ref);
    }
}

BASIS_LUAU_API int basis_luau_scheduler_start(basis_luau_runtime* rt, int worker_count)
{
    if (!rt) {
        return 0;
    }
    rt->scheduler_running = basis_luau_scheduler_start_impl(rt, worker_count);
    return rt->scheduler_running;
}

BASIS_LUAU_API void basis_luau_scheduler_shutdown(basis_luau_runtime* rt)
{
    if (rt) {
        basis_luau_scheduler_shutdown_impl(rt);
        rt->scheduler_running = 0;
    }
}

BASIS_LUAU_API void basis_luau_scheduler_kick(basis_luau_runtime* rt)
{
    if (rt) {
        basis_luau_scheduler_kick_impl(rt);
    }
}
