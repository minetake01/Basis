#pragma once

#include "basis_luau_runtime.h"

// Sandbox-safe proxy context slots in LUA_REGISTRYINDEX (globals are blocked on sandbox threads).
#define BASIS_PROXY_REG_ID 0xB4510110
#define BASIS_PROXY_REG_GEN 0xB4510111

typedef struct basis_luau_buffer_pool basis_luau_buffer_pool;
typedef struct basis_luau_rcu_snapshot basis_luau_rcu_snapshot;
typedef struct basis_luau_proxy_registry basis_luau_proxy_registry;

int basis_luau_buffer_pool_create(basis_luau_buffer_pool** out_pool, uint32_t capacity, const basis_luau_host_quota* quota);
void basis_luau_buffer_pool_destroy(basis_luau_buffer_pool* pool);
int basis_luau_buffer_pool_alloc(
    basis_luau_buffer_pool* pool,
    uint32_t host_id,
    uint32_t length,
    basis_luau_buffer_ref* out_ref,
    uint8_t** out_bytes);
void basis_luau_buffer_pool_release(basis_luau_buffer_pool* pool, const basis_luau_buffer_ref* ref);

int basis_luau_buffer_pool_get(
    basis_luau_buffer_pool* pool,
    const basis_luau_buffer_ref* ref,
    uint8_t** out_bytes,
    uint32_t* out_length);

int basis_luau_rcu_snapshot_create(basis_luau_rcu_snapshot** out_snap, uint32_t capacity);
void basis_luau_rcu_snapshot_destroy(basis_luau_rcu_snapshot* snap);
int basis_luau_rcu_snapshot_publish_begin(basis_luau_rcu_snapshot* snap, uint64_t* out_epoch);
void basis_luau_rcu_snapshot_publish_end(basis_luau_rcu_snapshot* snap, uint64_t epoch);
int basis_luau_rcu_snapshot_read_begin(basis_luau_rcu_snapshot* snap, uint64_t* out_epoch);
void basis_luau_rcu_snapshot_read_end(basis_luau_rcu_snapshot* snap, uint64_t epoch);
int basis_luau_rcu_snapshot_write_slot(basis_luau_rcu_snapshot* snap, uint32_t slot_index, const basis_luau_snapshot_slot* slot);
int basis_luau_rcu_snapshot_read_slot(basis_luau_rcu_snapshot* snap, uint32_t slot_index, basis_luau_snapshot_slot* out_slot);

int basis_luau_scheduler_start_impl(basis_luau_runtime* rt, int worker_count);
void basis_luau_scheduler_shutdown_impl(basis_luau_runtime* rt);
void basis_luau_scheduler_kick_impl(basis_luau_runtime* rt, float delta_time, float fixed_delta_time, int tick_fixed);

basis_luau_proxy_registry* basis_luau_proxy_registry_create(void);
void basis_luau_proxy_registry_destroy(basis_luau_proxy_registry* registry);
int basis_luau_proxy_register_impl(
    basis_luau_proxy_registry* registry,
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
int basis_luau_proxy_dispatch_event_impl(basis_luau_proxy_registry* registry, basis_luau_runtime* rt, const basis_luau_command* cmd);
void basis_luau_proxy_unregister_impl(basis_luau_proxy_registry* registry, uint32_t proxy_id);
void basis_luau_proxy_set_generation_impl(basis_luau_proxy_registry* registry, uint32_t proxy_id, uint32_t proxy_generation);
int basis_luau_proxy_tick_all_impl(basis_luau_proxy_registry* registry, basis_luau_proxy_tick_kind kind, float delta_time);

void basis_luau_register_native_bindings(lua_State* L, basis_luau_runtime* rt, int host_kind);

void basis_luau_runtime_set_frame_times(basis_luau_runtime* rt, float delta_time, float fixed_delta_time, int fixed_tick);
float basis_luau_runtime_get_delta(basis_luau_runtime* rt);
float basis_luau_runtime_get_fixed_delta(basis_luau_runtime* rt);
int basis_luau_runtime_consume_fixed_tick(basis_luau_runtime* rt);
int basis_luau_runtime_is_scheduler_running(basis_luau_runtime* rt);
void basis_luau_proxy_apply_thread_context(lua_State* thread, uint32_t proxy_id, uint32_t proxy_generation);
void basis_luau_proxy_read_thread_context(lua_State* thread, uint32_t* proxy_id, uint32_t* proxy_generation);
void basis_luau_proxy_run_frame(basis_luau_runtime* rt);
