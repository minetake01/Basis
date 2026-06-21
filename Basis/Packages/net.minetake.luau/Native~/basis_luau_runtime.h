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

typedef enum basis_luau_command_type {
    basis_luau_cmd_invalid = 0,
    basis_luau_cmd_set_position = 1,
    basis_luau_cmd_set_rotation = 2,
    basis_luau_cmd_set_local_position = 3,
    basis_luau_cmd_rotate = 4,
    basis_luau_cmd_destroy_object = 5,
    basis_luau_cmd_get_position_snapshot = 64,
    basis_luau_cmd_get_rotation_snapshot = 65,
    basis_luau_cmd_ticket_clone = 128,
    basis_luau_cmd_ticket_download_image = 129,
    basis_luau_cmd_event_network_message = 192,
    basis_luau_cmd_event_osc_message = 193,
    basis_luau_cmd_event_trigger_enter = 194,
} basis_luau_command_type;

#pragma pack(push, 1)
typedef struct basis_luau_command {
    uint16_t type;
    uint16_t host_id;
    uint32_t handle_index;
    uint32_t handle_generation;
    uint32_t proxy_generation;
    uint32_t sequence;
    float data[4];
} basis_luau_command;

typedef struct basis_luau_snapshot_slot {
    uint32_t handle_index;
    uint32_t handle_generation;
    uint32_t host_id;
    float position[3];
    float rotation[4];
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

typedef struct basis_luau_runtime basis_luau_runtime;

BASIS_LUAU_API basis_luau_runtime* basis_luau_runtime_create(const basis_luau_limits_config* config, basis_luau_init_error* out_error);
BASIS_LUAU_API void basis_luau_runtime_destroy(basis_luau_runtime* rt);

BASIS_LUAU_API lua_State* basis_luau_runtime_root_state(basis_luau_runtime* rt);

/* Bytecode gate */
BASIS_LUAU_API basis_luau_verify_error basis_luau_verify_bytecode(const uint8_t* data, size_t size, size_t max_bytes);
BASIS_LUAU_API int basis_luau_verify_signature(const uint8_t* payload, size_t payload_size, const uint8_t* signature, const uint8_t* key, size_t key_size);

/* Command ring (per-host credit) */
BASIS_LUAU_API basis_luau_ring_result basis_luau_ring_try_push_command(basis_luau_runtime* rt, uint32_t host_id, const basis_luau_command* cmd);
BASIS_LUAU_API basis_luau_ring_result basis_luau_ring_try_pop_command(basis_luau_runtime* rt, basis_luau_command* out_cmd);

/* RCU snapshot */
BASIS_LUAU_API int basis_luau_snapshot_publish_begin(basis_luau_runtime* rt, uint64_t* out_epoch);
BASIS_LUAU_API void basis_luau_snapshot_publish_end(basis_luau_runtime* rt, uint64_t epoch);
BASIS_LUAU_API int basis_luau_snapshot_read_begin(basis_luau_runtime* rt, uint64_t* out_epoch);
BASIS_LUAU_API void basis_luau_snapshot_read_end(basis_luau_runtime* rt, uint64_t epoch);
BASIS_LUAU_API int basis_luau_snapshot_write_slot(basis_luau_runtime* rt, uint32_t slot_index, const basis_luau_snapshot_slot* slot);

/* Buffer pool */
BASIS_LUAU_API int basis_luau_buffer_alloc(basis_luau_runtime* rt, uint32_t host_id, uint32_t length, basis_luau_buffer_ref* out_ref, uint8_t** out_bytes);
BASIS_LUAU_API void basis_luau_buffer_release(basis_luau_runtime* rt, const basis_luau_buffer_ref* ref);

/* Worker scheduler */
BASIS_LUAU_API int basis_luau_scheduler_start(basis_luau_runtime* rt, int worker_count);
BASIS_LUAU_API void basis_luau_scheduler_shutdown(basis_luau_runtime* rt);
BASIS_LUAU_API void basis_luau_scheduler_kick(basis_luau_runtime* rt);

#ifdef __cplusplus
}
#endif

#ifdef __cplusplus
static_assert(sizeof(basis_luau_command) == BASIS_LUAU_COMMAND_SIZE, "basis_luau_command ABI size");
#endif
