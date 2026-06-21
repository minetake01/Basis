#pragma once

#include "basis_luau_runtime.h"

typedef struct basis_luau_buffer_pool basis_luau_buffer_pool;
typedef struct basis_luau_rcu_snapshot basis_luau_rcu_snapshot;

int basis_luau_buffer_pool_create(basis_luau_buffer_pool** out_pool, uint32_t capacity, const basis_luau_host_quota* quota);
void basis_luau_buffer_pool_destroy(basis_luau_buffer_pool* pool);
int basis_luau_buffer_pool_alloc(
    basis_luau_buffer_pool* pool,
    uint32_t host_id,
    uint32_t length,
    basis_luau_buffer_ref* out_ref,
    uint8_t** out_bytes);
void basis_luau_buffer_pool_release(basis_luau_buffer_pool* pool, const basis_luau_buffer_ref* ref);

int basis_luau_rcu_snapshot_create(basis_luau_rcu_snapshot** out_snap, uint32_t capacity);
void basis_luau_rcu_snapshot_destroy(basis_luau_rcu_snapshot* snap);
int basis_luau_rcu_snapshot_publish_begin(basis_luau_rcu_snapshot* snap, uint64_t* out_epoch);
void basis_luau_rcu_snapshot_publish_end(basis_luau_rcu_snapshot* snap, uint64_t epoch);
int basis_luau_rcu_snapshot_read_begin(basis_luau_rcu_snapshot* snap, uint64_t* out_epoch);
void basis_luau_rcu_snapshot_read_end(basis_luau_rcu_snapshot* snap, uint64_t epoch);
int basis_luau_rcu_snapshot_write_slot(basis_luau_rcu_snapshot* snap, uint32_t slot_index, const basis_luau_snapshot_slot* slot);

int basis_luau_scheduler_start_impl(basis_luau_runtime* rt, int worker_count);
void basis_luau_scheduler_shutdown_impl(basis_luau_runtime* rt);
void basis_luau_scheduler_kick_impl(basis_luau_runtime* rt);

void basis_luau_register_native_bindings(lua_State* L, basis_luau_runtime* rt);
