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
    int owns_root;
    basis_luau_command_ring ring;
    basis_luau_command_ring event_ring;
    basis_luau_buffer_pool* buffer_pool;
    basis_luau_rcu_snapshot* snapshot;
    basis_luau_proxy_registry* proxies;
    basis_luau_host_quota quota;
    uint16_t host_id;
    int host_kind;
    int scheduler_running;
    float last_delta_time;
    float last_fixed_delta_time;
    int pending_fixed_tick;
    basis_luau_buffer_ref datetime_buffer;
    int has_datetime_buffer;
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
    case basis_luau_cmd_set_ui_text:
    case basis_luau_cmd_log:
    case basis_luau_cmd_warn:
    case basis_luau_cmd_error:
    case basis_luau_cmd_get_position_snapshot:
    case basis_luau_cmd_get_rotation_snapshot:
    case basis_luau_cmd_get_local_position_snapshot:
    case basis_luau_cmd_ticket_clone:
    case basis_luau_cmd_ticket_download_image:
    case basis_luau_cmd_ticket_network_send:
    case basis_luau_cmd_ticket_take_ownership:
    case basis_luau_cmd_ticket_make_networkable:
    case basis_luau_cmd_ticket_make_interactable:
    case basis_luau_cmd_ticket_osc_publish_float:
    case basis_luau_cmd_ticket_osc_publish_int:
    case basis_luau_cmd_ticket_osc_publish_bool:
    case basis_luau_cmd_ticket_osc_publish_string:
    case basis_luau_cmd_ticket_osc_subscribe:
    case basis_luau_cmd_ticket_avatar_resolve:
    case basis_luau_cmd_ticket_player_teleport:
    case basis_luau_cmd_ticket_player_respawn:
    case basis_luau_cmd_ticket_vixxy_get:
    case basis_luau_cmd_ticket_vixxy_apply:
    case basis_luau_cmd_ticket_interact_press:
    case basis_luau_cmd_event_network_message:
    case basis_luau_cmd_event_osc_message:
    case basis_luau_cmd_event_trigger_enter:
    case basis_luau_cmd_event_trigger_exit:
    case basis_luau_cmd_event_collision_enter:
    case basis_luau_cmd_event_collision_exit:
    case basis_luau_cmd_shutdown:
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
    rt->owns_root = 1;

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

    if (!basis_luau_command_ring_init(&rt->event_ring, rt->quota.max_commands_per_slice)) {
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

    rt->proxies = basis_luau_proxy_registry_create();
    if (!rt->proxies) {
        basis_luau_runtime_destroy(rt);
        if (out_error) {
            *out_error = basis_luau_init_ctx_alloc_failed;
        }
        return NULL;
    }

    basis_luau_register_native_bindings(rt->root, rt, 0);
    return rt;
}

BASIS_LUAU_API void basis_luau_runtime_destroy(basis_luau_runtime* rt)
{
    if (!rt) {
        return;
    }
    basis_luau_scheduler_shutdown(rt);
    if (rt->proxies) {
        basis_luau_proxy_registry_destroy(rt->proxies);
    }
    if (rt->snapshot) {
        basis_luau_rcu_snapshot_destroy(rt->snapshot);
    }
    if (rt->buffer_pool) {
        basis_luau_buffer_pool_destroy(rt->buffer_pool);
    }
    basis_luau_command_ring_destroy(&rt->ring);
    basis_luau_command_ring_destroy(&rt->event_ring);
    if (rt->root && rt->owns_root) {
        ffi_lua_close(rt->root);
    }
    free(rt);
}

BASIS_LUAU_API lua_State* basis_luau_runtime_root_state(basis_luau_runtime* rt)
{
    return rt ? rt->root : NULL;
}

BASIS_LUAU_API void basis_luau_runtime_release_root_ownership(basis_luau_runtime* rt)
{
    if (rt) {
        rt->owns_root = 0;
    }
}

BASIS_LUAU_API void basis_luau_runtime_set_host_id(basis_luau_runtime* rt, uint16_t host_id)
{
    if (rt) {
        rt->host_id = host_id;
    }
}

BASIS_LUAU_API uint16_t basis_luau_runtime_host_id(basis_luau_runtime* rt)
{
    return rt ? rt->host_id : 0;
}

BASIS_LUAU_API void basis_luau_runtime_set_host_kind(basis_luau_runtime* rt, int host_kind)
{
    if (rt) {
        rt->host_kind = host_kind;
        basis_luau_register_native_bindings(rt->root, rt, host_kind);
    }
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

static int basis_luau_is_event_command(uint16_t type)
{
    switch ((basis_luau_command_type)type) {
    case basis_luau_cmd_event_network_message:
    case basis_luau_cmd_event_osc_message:
    case basis_luau_cmd_event_trigger_enter:
    case basis_luau_cmd_event_trigger_exit:
    case basis_luau_cmd_event_collision_enter:
    case basis_luau_cmd_event_collision_exit:
        return 1;
    default:
        return 0;
    }
}

BASIS_LUAU_API basis_luau_ring_result basis_luau_event_ring_try_push(basis_luau_runtime* rt, uint32_t host_id, const basis_luau_command* cmd)
{
    (void)host_id;
    if (!rt || !cmd || !basis_luau_is_event_command(cmd->type)) {
        return basis_luau_ring_invalid;
    }
    if (rt->event_ring.count >= rt->event_ring.capacity) {
        return basis_luau_ring_full;
    }
    rt->event_ring.items[rt->event_ring.tail] = *cmd;
    rt->event_ring.tail = (rt->event_ring.tail + 1) % rt->event_ring.capacity;
    rt->event_ring.count += 1;
    return basis_luau_ring_ok;
}

BASIS_LUAU_API basis_luau_ring_result basis_luau_event_ring_try_pop(basis_luau_runtime* rt, basis_luau_command* out_cmd)
{
    if (!rt || !out_cmd) {
        return basis_luau_ring_invalid;
    }
    if (rt->event_ring.count == 0) {
        return basis_luau_ring_empty;
    }
    *out_cmd = rt->event_ring.items[rt->event_ring.head];
    rt->event_ring.head = (rt->event_ring.head + 1) % rt->event_ring.capacity;
    rt->event_ring.count -= 1;
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

BASIS_LUAU_API int basis_luau_snapshot_read_slot(basis_luau_runtime* rt, uint32_t slot_index, basis_luau_snapshot_slot* out_slot)
{
    return rt ? basis_luau_rcu_snapshot_read_slot(rt->snapshot, slot_index, out_slot) : 0;
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

BASIS_LUAU_API int basis_luau_buffer_get(
    basis_luau_runtime* rt,
    const basis_luau_buffer_ref* buffer_ref,
    uint8_t** out_bytes,
    uint32_t* out_length)
{
    return rt ? basis_luau_buffer_pool_get(rt->buffer_pool, buffer_ref, out_bytes, out_length) : 0;
}

BASIS_LUAU_API int basis_luau_proxy_register(
    basis_luau_runtime* rt,
    uint32_t proxy_id,
    lua_State* thread,
    uint32_t proxy_generation,
    int ref_update,
    int ref_fixed_update,
    int ref_late_update,
    int ref_trigger_enter,
    int ref_trigger_exit,
    int ref_collision_enter,
    int ref_collision_exit)
{
    return rt
        ? basis_luau_proxy_register_impl(
              rt->proxies,
              proxy_id,
              thread,
              proxy_generation,
              ref_update,
              ref_fixed_update,
              ref_late_update,
              ref_trigger_enter,
              ref_trigger_exit,
              ref_collision_enter,
              ref_collision_exit)
        : 0;
}

BASIS_LUAU_API void basis_luau_proxy_unregister(basis_luau_runtime* rt, uint32_t proxy_id)
{
    if (rt) {
        basis_luau_proxy_unregister_impl(rt->proxies, proxy_id);
    }
}

BASIS_LUAU_API void basis_luau_proxy_set_generation(basis_luau_runtime* rt, uint32_t proxy_id, uint32_t proxy_generation)
{
    if (rt) {
        basis_luau_proxy_set_generation_impl(rt->proxies, proxy_id, proxy_generation);
    }
}

BASIS_LUAU_API void basis_luau_proxy_set_thread_context(lua_State* thread, uint32_t proxy_id, uint32_t proxy_generation)
{
    basis_luau_proxy_apply_thread_context(thread, proxy_id, proxy_generation);
}

BASIS_LUAU_API int basis_luau_proxy_tick_all(basis_luau_runtime* rt, basis_luau_proxy_tick_kind kind, float delta_time)
{
    return rt ? basis_luau_proxy_tick_all_impl(rt->proxies, kind, delta_time) : 0;
}

BASIS_LUAU_API int basis_luau_proxy_push_event(
    basis_luau_runtime* rt,
    uint32_t proxy_id,
    uint32_t proxy_generation,
    int event_ref,
    const float* data,
    int data_count)
{
    if (!rt || proxy_id == 0 || event_ref <= 0) {
        return 0;
    }

    basis_luau_command cmd;
    memset(&cmd, 0, sizeof(cmd));
    cmd.type = (uint16_t)basis_luau_cmd_event_osc_message;
    cmd.host_id = basis_luau_runtime_host_id(rt);
    cmd.proxy_id = proxy_id;
    cmd.proxy_generation = proxy_generation;
    cmd.data[0] = (float)event_ref;
    if (data && data_count > 0) {
        cmd.data[1] = data[0];
    }
    if (data && data_count > 1) {
        cmd.data[2] = data[1];
    }
    if (data && data_count > 2) {
        cmd.data[3] = data[2];
    }

    return basis_luau_event_ring_try_push(rt, cmd.host_id, &cmd) == basis_luau_ring_ok;
}

BASIS_LUAU_API int basis_luau_proxy_dispatch_events(basis_luau_runtime* rt)
{
    if (!rt || !rt->proxies) {
        return 0;
    }

    int dispatched = 0;
    for (;;) {
        basis_luau_command cmd;
        if (basis_luau_event_ring_try_pop(rt, &cmd) != basis_luau_ring_ok) {
            break;
        }
        if (basis_luau_proxy_dispatch_event_impl(rt->proxies, rt, &cmd) == 0) {
            dispatched += 1;
        }
    }
    return dispatched;
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

BASIS_LUAU_API void basis_luau_scheduler_kick(
    basis_luau_runtime* rt,
    float delta_time,
    float fixed_delta_time,
    int tick_fixed)
{
    if (rt) {
        basis_luau_scheduler_kick_impl(rt, delta_time, fixed_delta_time, tick_fixed);
        rt->pending_fixed_tick = 0;
    }
}

void basis_luau_runtime_set_frame_times(basis_luau_runtime* rt, float delta_time, float fixed_delta_time, int fixed_tick)
{
    if (rt) {
        rt->last_delta_time = delta_time;
        rt->last_fixed_delta_time = fixed_delta_time;
        if (fixed_tick) {
            rt->pending_fixed_tick = 1;
        }
    }
}

float basis_luau_runtime_get_delta(basis_luau_runtime* rt)
{
    return rt ? rt->last_delta_time : 0.0f;
}

float basis_luau_runtime_get_fixed_delta(basis_luau_runtime* rt)
{
    return rt ? rt->last_fixed_delta_time : 0.0f;
}

int basis_luau_runtime_consume_fixed_tick(basis_luau_runtime* rt)
{
    if (!rt || !rt->pending_fixed_tick) {
        return 0;
    }
    rt->pending_fixed_tick = 0;
    return 1;
}

int basis_luau_runtime_is_scheduler_running(basis_luau_runtime* rt)
{
    return rt && rt->scheduler_running;
}

BASIS_LUAU_API void basis_luau_runtime_set_datetime_buffer(basis_luau_runtime* rt, const basis_luau_buffer_ref* buffer_ref)
{
    if (!rt) {
        return;
    }

    if (rt->has_datetime_buffer) {
        basis_luau_buffer_release(rt, &rt->datetime_buffer);
        rt->has_datetime_buffer = 0;
        memset(&rt->datetime_buffer, 0, sizeof(rt->datetime_buffer));
    }

    if (buffer_ref && buffer_ref->length > 0) {
        rt->datetime_buffer = *buffer_ref;
        rt->has_datetime_buffer = 1;
    }
}

BASIS_LUAU_API int basis_luau_runtime_read_datetime(basis_luau_runtime* rt, uint8_t** out_bytes, uint32_t* out_length)
{
    if (!rt || !out_bytes || !out_length || !rt->has_datetime_buffer) {
        return 0;
    }

    return basis_luau_buffer_get(rt, &rt->datetime_buffer, out_bytes, out_length);
}
