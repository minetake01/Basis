#pragma once

#include <stddef.h>
#include <stdint.h>

#include "basis_luau_limits.h"

#ifdef __cplusplus
extern "C" {
#endif

#define BASIS_LUAU_SIGNATURE_BYTES 32
#define BASIS_LUAU_COMMAND_SIZE 32
#define BASIS_LUAU_SNAPSHOT_STRIDE 48
#define BASIS_LUAU_SNAPSHOT_TIME_SLOT 0

typedef enum basis_luau_command_type {
    basis_luau_cmd_invalid = 0,
    basis_luau_cmd_set_position = 1,
    basis_luau_cmd_set_rotation = 2,
    basis_luau_cmd_set_local_position = 3,
    basis_luau_cmd_rotate = 4,
    basis_luau_cmd_destroy_object = 5,
    basis_luau_cmd_set_ui_text = 6,
    basis_luau_cmd_log = 7,
    basis_luau_cmd_warn = 8,
    basis_luau_cmd_error = 9,
    basis_luau_cmd_get_position_snapshot = 64,
    basis_luau_cmd_get_rotation_snapshot = 65,
    basis_luau_cmd_get_local_position_snapshot = 66,
    basis_luau_cmd_ticket_clone = 128,
    basis_luau_cmd_ticket_download_image = 129,
    basis_luau_cmd_ticket_network_send = 130,
    basis_luau_cmd_ticket_take_ownership = 131,
    basis_luau_cmd_ticket_make_networkable = 132,
    basis_luau_cmd_ticket_make_interactable = 133,
    basis_luau_cmd_ticket_osc_publish_float = 140,
    basis_luau_cmd_ticket_osc_publish_int = 141,
    basis_luau_cmd_ticket_osc_publish_bool = 142,
    basis_luau_cmd_ticket_osc_publish_string = 143,
    basis_luau_cmd_ticket_osc_subscribe = 144,
    basis_luau_cmd_ticket_avatar_resolve = 150,
    basis_luau_cmd_ticket_player_teleport = 151,
    basis_luau_cmd_ticket_player_respawn = 152,
    basis_luau_cmd_ticket_vixxy_get = 153,
    basis_luau_cmd_ticket_vixxy_apply = 154,
    basis_luau_cmd_ticket_interact_press = 155,
    basis_luau_cmd_event_network_message = 192,
    basis_luau_cmd_event_osc_message = 193,
    basis_luau_cmd_event_trigger_enter = 194,
    basis_luau_cmd_event_trigger_exit = 195,
    basis_luau_cmd_event_collision_enter = 196,
    basis_luau_cmd_event_collision_exit = 197,
    basis_luau_cmd_shutdown = 255,
} basis_luau_command_type;

#pragma pack(push, 1)
typedef struct basis_luau_command {
    uint16_t type;
    uint16_t host_id;
    uint32_t handle_index;
    uint32_t handle_generation;
    uint32_t proxy_id;
    uint32_t proxy_generation;
    float data[4];
} basis_luau_command;

typedef struct basis_luau_snapshot_slot {
    uint32_t handle_index;
    uint32_t handle_generation;
    uint32_t host_id;
    float position[3];
    float rotation[4];
    float time_data[4];
    uint32_t epoch;
} basis_luau_snapshot_slot;

typedef struct basis_luau_buffer_ref {
    uint32_t pool_id;
    uint32_t slot;
    uint32_t generation;
    uint32_t host_id;
    uint32_t length;
} basis_luau_buffer_ref;
#pragma pack(pop)

typedef struct basis_luau_host_quota {
    uint32_t max_commands_per_slice;
    uint32_t max_command_bytes_per_slice;
    uint32_t max_buffer_bytes;
    uint32_t max_buffer_count;
    uint32_t max_payload_bytes;
} basis_luau_host_quota;

typedef enum basis_luau_verify_error {
    basis_luau_verify_ok = 0,
    basis_luau_verify_empty = 1,
    basis_luau_verify_truncated = 2,
    basis_luau_verify_bad_version = 3,
    basis_luau_verify_bad_type_version = 4,
    basis_luau_verify_limit_exceeded = 5,
    basis_luau_verify_malformed = 6,
} basis_luau_verify_error;

typedef enum basis_luau_ring_result {
    basis_luau_ring_ok = 0,
    basis_luau_ring_full = 1,
    basis_luau_ring_empty = 2,
    basis_luau_ring_invalid = 3,
} basis_luau_ring_result;

typedef enum basis_luau_proxy_tick_kind {
    basis_luau_tick_update = 0,
    basis_luau_tick_fixed_update = 1,
    basis_luau_tick_late_update = 2,
} basis_luau_proxy_tick_kind;

typedef struct basis_luau_runtime basis_luau_runtime;

BASIS_LUAU_API basis_luau_runtime* basis_luau_runtime_create(const basis_luau_limits_config* config, basis_luau_init_error* out_error);
BASIS_LUAU_API void basis_luau_runtime_destroy(basis_luau_runtime* rt);

BASIS_LUAU_API lua_State* basis_luau_runtime_root_state(basis_luau_runtime* rt);
BASIS_LUAU_API void basis_luau_runtime_release_root_ownership(basis_luau_runtime* rt);
BASIS_LUAU_API void basis_luau_runtime_set_host_id(basis_luau_runtime* rt, uint16_t host_id);
BASIS_LUAU_API uint16_t basis_luau_runtime_host_id(basis_luau_runtime* rt);
BASIS_LUAU_API void basis_luau_runtime_set_host_kind(basis_luau_runtime* rt, int host_kind);

/* Bytecode gate */
BASIS_LUAU_API basis_luau_verify_error basis_luau_verify_bytecode(const uint8_t* data, size_t size, size_t max_bytes);
BASIS_LUAU_API int basis_luau_verify_signature(const uint8_t* payload, size_t payload_size, const uint8_t* signature, const uint8_t* key, size_t key_size);

/* Command ring */
BASIS_LUAU_API basis_luau_ring_result basis_luau_ring_try_push_command(basis_luau_runtime* rt, uint32_t host_id, const basis_luau_command* cmd);
BASIS_LUAU_API basis_luau_ring_result basis_luau_ring_try_pop_command(basis_luau_runtime* rt, basis_luau_command* out_cmd);

BASIS_LUAU_API basis_luau_ring_result basis_luau_event_ring_try_push(basis_luau_runtime* rt, uint32_t host_id, const basis_luau_command* cmd);
BASIS_LUAU_API basis_luau_ring_result basis_luau_event_ring_try_pop(basis_luau_runtime* rt, basis_luau_command* out_cmd);

/* RCU snapshot */
BASIS_LUAU_API int basis_luau_snapshot_publish_begin(basis_luau_runtime* rt, uint64_t* out_epoch);
BASIS_LUAU_API void basis_luau_snapshot_publish_end(basis_luau_runtime* rt, uint64_t epoch);
BASIS_LUAU_API int basis_luau_snapshot_read_begin(basis_luau_runtime* rt, uint64_t* out_epoch);
BASIS_LUAU_API void basis_luau_snapshot_read_end(basis_luau_runtime* rt, uint64_t epoch);
BASIS_LUAU_API int basis_luau_snapshot_write_slot(basis_luau_runtime* rt, uint32_t slot_index, const basis_luau_snapshot_slot* slot);
BASIS_LUAU_API int basis_luau_snapshot_read_slot(basis_luau_runtime* rt, uint32_t slot_index, basis_luau_snapshot_slot* out_slot);

/* Buffer pool */
BASIS_LUAU_API int basis_luau_buffer_alloc(basis_luau_runtime* rt, uint32_t host_id, uint32_t length, basis_luau_buffer_ref* out_ref, uint8_t** out_bytes);
BASIS_LUAU_API void basis_luau_buffer_release(basis_luau_runtime* rt, const basis_luau_buffer_ref* ref);
BASIS_LUAU_API int basis_luau_buffer_get(basis_luau_runtime* rt, const basis_luau_buffer_ref* buffer_ref, uint8_t** out_bytes, uint32_t* out_length);

/* Proxy registry */
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
    int ref_collision_exit);
BASIS_LUAU_API void basis_luau_proxy_unregister(basis_luau_runtime* rt, uint32_t proxy_id);
BASIS_LUAU_API void basis_luau_proxy_set_generation(basis_luau_runtime* rt, uint32_t proxy_id, uint32_t proxy_generation);
BASIS_LUAU_API void basis_luau_proxy_set_thread_context(lua_State* thread, uint32_t proxy_id, uint32_t proxy_generation);
BASIS_LUAU_API int basis_luau_proxy_tick_all(basis_luau_runtime* rt, basis_luau_proxy_tick_kind kind, float delta_time);
BASIS_LUAU_API int basis_luau_proxy_push_event(basis_luau_runtime* rt, uint32_t proxy_id, uint32_t proxy_generation, int event_ref, const float* data, int data_count);
BASIS_LUAU_API int basis_luau_proxy_dispatch_events(basis_luau_runtime* rt);

/* Worker scheduler */
BASIS_LUAU_API int basis_luau_scheduler_start(basis_luau_runtime* rt, int worker_count);
BASIS_LUAU_API void basis_luau_scheduler_shutdown(basis_luau_runtime* rt);
BASIS_LUAU_API void basis_luau_scheduler_kick(
    basis_luau_runtime* rt,
    float delta_time,
    float fixed_delta_time,
    int tick_fixed);

BASIS_LUAU_API void basis_luau_runtime_set_datetime_buffer(basis_luau_runtime* rt, const basis_luau_buffer_ref* buffer_ref);
BASIS_LUAU_API int basis_luau_runtime_read_datetime(basis_luau_runtime* rt, uint8_t** out_bytes, uint32_t* out_length);

#ifdef __cplusplus
}
#endif

#ifdef __cplusplus
static_assert(sizeof(basis_luau_command) == BASIS_LUAU_COMMAND_SIZE, "basis_luau_command ABI size");
#endif
