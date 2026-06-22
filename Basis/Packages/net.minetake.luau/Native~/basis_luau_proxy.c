#include "basis_luau_runtime.h"
#include "basis_luau_internal.h"

#include "lua.h"
#include "lualib.h"
#include "basis_luau_ffi_lua.h"

#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#endif

#define BASIS_PROXY_MAX 256

typedef struct basis_luau_proxy_entry {
    uint32_t proxy_id;
    uint32_t proxy_generation;
    lua_State* thread;
    int ref_update;
    int ref_fixed_update;
    int ref_late_update;
    int ref_trigger_enter;
    int ref_trigger_exit;
    int ref_collision_enter;
    int ref_collision_exit;
    int enabled;
} basis_luau_proxy_entry;

struct basis_luau_proxy_registry {
    basis_luau_proxy_entry entries[BASIS_PROXY_MAX];
    uint32_t count;
#if defined(_WIN32)
    CRITICAL_SECTION vm_lock;
#else
    pthread_mutex_t vm_lock;
#endif
};

static void proxy_lock(basis_luau_proxy_registry* registry)
{
#if defined(_WIN32)
    EnterCriticalSection(&registry->vm_lock);
#else
    pthread_mutex_lock(&registry->vm_lock);
#endif
}

static void proxy_unlock(basis_luau_proxy_registry* registry)
{
#if defined(_WIN32)
    LeaveCriticalSection(&registry->vm_lock);
#else
    pthread_mutex_unlock(&registry->vm_lock);
#endif
}

basis_luau_proxy_registry* basis_luau_proxy_registry_create(void)
{
    basis_luau_proxy_registry* registry = (basis_luau_proxy_registry*)calloc(1, sizeof(*registry));
    if (!registry) {
        return NULL;
    }
#if defined(_WIN32)
    InitializeCriticalSection(&registry->vm_lock);
#else
    pthread_mutex_init(&registry->vm_lock, NULL);
#endif
    return registry;
}

void basis_luau_proxy_registry_destroy(basis_luau_proxy_registry* registry)
{
    if (!registry) {
        return;
    }
#if defined(_WIN32)
    DeleteCriticalSection(&registry->vm_lock);
#else
    pthread_mutex_destroy(&registry->vm_lock);
#endif
    free(registry);
}

static basis_luau_proxy_entry* proxy_find(basis_luau_proxy_registry* registry, uint32_t proxy_id)
{
    for (uint32_t i = 0; i < registry->count; ++i) {
        if (registry->entries[i].proxy_id == proxy_id) {
            return &registry->entries[i];
        }
    }
    return NULL;
}

static int proxy_call_ref(lua_State* L, int ref, int arg_count)
{
    if (ref <= 0) {
        return 0;
    }
    lua_rawgeti(L, LUA_REGISTRYINDEX, ref);
    if (!lua_isfunction(L, -1)) {
        lua_pop(L, 1);
        return 0;
    }
    if (arg_count > 0) {
        lua_insert(L, -1 - arg_count);
    }
    if (lua_pcall(L, arg_count, 0, 0) != 0) {
        lua_pop(L, 1);
        return -1;
    }
    return 0;
}

void basis_luau_proxy_apply_thread_context(lua_State* thread, uint32_t proxy_id, uint32_t proxy_generation)
{
    if (!thread) {
        return;
    }
    lua_pushnumber(thread, (double)proxy_id);
    lua_rawseti(thread, LUA_REGISTRYINDEX, BASIS_PROXY_REG_ID);
    lua_pushnumber(thread, (double)proxy_generation);
    lua_rawseti(thread, LUA_REGISTRYINDEX, BASIS_PROXY_REG_GEN);
}

void basis_luau_proxy_read_thread_context(lua_State* thread, uint32_t* proxy_id, uint32_t* proxy_generation)
{
    if (!thread || !proxy_id || !proxy_generation) {
        return;
    }

    lua_rawgeti(thread, LUA_REGISTRYINDEX, BASIS_PROXY_REG_ID);
    *proxy_id = (uint32_t)lua_tonumber(thread, -1);
    lua_pop(thread, 1);
    lua_rawgeti(thread, LUA_REGISTRYINDEX, BASIS_PROXY_REG_GEN);
    *proxy_generation = (uint32_t)lua_tonumber(thread, -1);
    lua_pop(thread, 1);
}

void basis_luau_proxy_run_frame(basis_luau_runtime* rt)
{
    if (!rt || !basis_luau_runtime_is_scheduler_running(rt)) {
        return;
    }

    basis_luau_proxy_dispatch_events(rt);
    if (basis_luau_runtime_consume_fixed_tick(rt)) {
        basis_luau_proxy_tick_all(rt, basis_luau_tick_fixed_update, basis_luau_runtime_get_fixed_delta(rt));
    }
    basis_luau_proxy_tick_all(rt, basis_luau_tick_update, basis_luau_runtime_get_delta(rt));
    basis_luau_proxy_tick_all(rt, basis_luau_tick_late_update, basis_luau_runtime_get_delta(rt));
}

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
    int ref_collision_exit)
{
    if (!registry || !thread || proxy_id == 0) {
        return 0;
    }

    basis_luau_proxy_entry* existing = proxy_find(registry, proxy_id);
    if (existing) {
        existing->thread = thread;
        existing->proxy_generation = proxy_generation;
        existing->ref_update = ref_update;
        existing->ref_fixed_update = ref_fixed_update;
        existing->ref_late_update = ref_late_update;
        existing->ref_trigger_enter = ref_trigger_enter;
        existing->ref_trigger_exit = ref_trigger_exit;
        existing->ref_collision_enter = ref_collision_enter;
        existing->ref_collision_exit = ref_collision_exit;
        existing->enabled = 1;
        basis_luau_proxy_apply_thread_context(thread, proxy_id, proxy_generation);
        return 1;
    }

    if (registry->count >= BASIS_PROXY_MAX) {
        return 0;
    }

    basis_luau_proxy_entry* entry = &registry->entries[registry->count++];
    entry->proxy_id = proxy_id;
    entry->proxy_generation = proxy_generation;
    entry->thread = thread;
    entry->ref_update = ref_update;
    entry->ref_fixed_update = ref_fixed_update;
    entry->ref_late_update = ref_late_update;
    entry->ref_trigger_enter = ref_trigger_enter;
    entry->ref_trigger_exit = ref_trigger_exit;
    entry->ref_collision_enter = ref_collision_enter;
    entry->ref_collision_exit = ref_collision_exit;
    entry->enabled = 1;
    basis_luau_proxy_apply_thread_context(thread, proxy_id, proxy_generation);
    return 1;
}

void basis_luau_proxy_unregister_impl(basis_luau_proxy_registry* registry, uint32_t proxy_id)
{
    if (!registry) {
        return;
    }
    for (uint32_t i = 0; i < registry->count; ++i) {
        if (registry->entries[i].proxy_id == proxy_id) {
            registry->entries[i].enabled = 0;
            return;
        }
    }
}

void basis_luau_proxy_set_generation_impl(basis_luau_proxy_registry* registry, uint32_t proxy_id, uint32_t proxy_generation)
{
    basis_luau_proxy_entry* entry = proxy_find(registry, proxy_id);
    if (entry) {
        entry->proxy_generation = proxy_generation;
        if (entry->thread) {
            basis_luau_proxy_apply_thread_context(entry->thread, proxy_id, proxy_generation);
        }
    }
}

int basis_luau_proxy_tick_all_impl(basis_luau_proxy_registry* registry, basis_luau_proxy_tick_kind kind, float delta_time)
{
    if (!registry) {
        return 0;
    }

    int errors = 0;
    proxy_lock(registry);
    for (uint32_t i = 0; i < registry->count; ++i) {
        basis_luau_proxy_entry* entry = &registry->entries[i];
        if (!entry->enabled || !entry->thread) {
            continue;
        }

        int ref = 0;
        switch (kind) {
        case basis_luau_tick_update:
            ref = entry->ref_update;
            break;
        case basis_luau_tick_fixed_update:
            ref = entry->ref_fixed_update;
            break;
        case basis_luau_tick_late_update:
            ref = entry->ref_late_update;
            break;
        default:
            break;
        }

        if (ref <= 0) {
            continue;
        }

        basis_luau_begin_execution(entry->thread, 500000000LL);
        lua_pushnumber(entry->thread, (double)delta_time);
        if (proxy_call_ref(entry->thread, ref, 1) != 0) {
            errors += 1;
        }
        basis_luau_end_execution(entry->thread);
    }
    proxy_unlock(registry);
    return errors;
}

static int proxy_event_ref_for_type(basis_luau_proxy_entry* entry, uint16_t type)
{
    switch ((basis_luau_command_type)type) {
    case basis_luau_cmd_event_trigger_enter:
        return entry->ref_trigger_enter;
    case basis_luau_cmd_event_trigger_exit:
        return entry->ref_trigger_exit;
    case basis_luau_cmd_event_collision_enter:
        return entry->ref_collision_enter;
    case basis_luau_cmd_event_collision_exit:
        return entry->ref_collision_exit;
    case basis_luau_cmd_event_osc_message:
        return 0;
    default:
        return 0;
    }
}

int basis_luau_proxy_dispatch_event_impl(basis_luau_proxy_registry* registry, basis_luau_runtime* rt, const basis_luau_command* cmd)
{
    if (!registry || !cmd || cmd->proxy_id == 0) {
        return 0;
    }

    int handled = 0;
    proxy_lock(registry);
    basis_luau_proxy_entry* entry = proxy_find(registry, cmd->proxy_id);
    if (!entry || !entry->enabled || !entry->thread || entry->proxy_generation != cmd->proxy_generation) {
        proxy_unlock(registry);
        return 0;
    }

    int ref = proxy_event_ref_for_type(entry, cmd->type);
    if (cmd->type == (uint16_t)basis_luau_cmd_event_osc_message) {
        ref = (int)cmd->data[0];
    }

    if (ref <= 0) {
        proxy_unlock(registry);
        return 0;
    }

    basis_luau_begin_execution(entry->thread, 500000000LL);
    if (cmd->type == (uint16_t)basis_luau_cmd_event_osc_message) {
        lua_createtable(entry->thread, 0, 3);
        lua_pushnumber(entry->thread, (double)cmd->data[1]);
        lua_setfield(entry->thread, -2, "value");
        if (rt && cmd->data[2] > 0.0f) {
            basis_luau_buffer_ref buffer_ref;
            memset(&buffer_ref, 0, sizeof(buffer_ref));
            buffer_ref.slot = (uint32_t)cmd->data[2];
            buffer_ref.generation = (uint32_t)cmd->data[3];
            buffer_ref.host_id = cmd->host_id;
            uint8_t* bytes = NULL;
            uint32_t length = 0;
            if (basis_luau_buffer_get(rt, &buffer_ref, &bytes, &length) && bytes && length > 0) {
                lua_pushlstring(entry->thread, (const char*)bytes, length);
                lua_setfield(entry->thread, -2, "address");
            }
        }
        if (proxy_call_ref(entry->thread, ref, 1) == 0) {
            handled = 1;
        }
    } else {
        uint64_t handle_raw =
            ((uint64_t)cmd->handle_generation << 32) | (uint64_t)cmd->handle_index;
        lua_pushnumber(entry->thread, (double)handle_raw);
        if (proxy_call_ref(entry->thread, ref, 1) == 0) {
            handled = 1;
        }
    }
    basis_luau_end_execution(entry->thread);
    proxy_unlock(registry);
    return handled;
}
