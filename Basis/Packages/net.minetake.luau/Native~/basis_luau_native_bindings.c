#include "basis_luau_internal.h"

#include "lua.h"
#include "lualib.h"
#include "basis_luau_ffi_lua.h"

#include <math.h>
#include <string.h>

#define REG_RT_KEY "__basis_rt"

static basis_luau_runtime* basis_rt_from_state(lua_State* L)
{
    lua_pushlightuserdata(L, (void*)REG_RT_KEY);
    lua_rawget(L, LUA_REGISTRYINDEX);
    basis_luau_runtime* rt = (basis_luau_runtime*)lua_touserdata(L, -1);
    lua_pop(L, 1);
    return rt;
}

static void decode_handle(double raw, uint32_t* index, uint32_t* generation)
{
    uint64_t u = (uint64_t)raw;
    *index = (uint32_t)(u & 0xFFFFFFFFu);
    *generation = (uint32_t)(u >> 32);
}

static void read_proxy_context(lua_State* L, uint32_t* proxy_id, uint32_t* proxy_generation)
{
    basis_luau_proxy_read_thread_context(L, proxy_id, proxy_generation);
}

static int basis_native_enqueue(
    lua_State* L,
    basis_luau_command_type type,
    uint32_t handle_index,
    uint32_t handle_generation,
    const float* data)
{
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (!rt) {
        return 0;
    }

    basis_luau_command cmd;
    memset(&cmd, 0, sizeof(cmd));
    cmd.type = (uint16_t)type;
    cmd.host_id = basis_luau_runtime_host_id(rt);
    cmd.handle_index = handle_index;
    cmd.handle_generation = handle_generation;
    read_proxy_context(L, &cmd.proxy_id, &cmd.proxy_generation);
    if (data) {
        cmd.data[0] = data[0];
        cmd.data[1] = data[1];
        cmd.data[2] = data[2];
        cmd.data[3] = data[3];
    }

    return basis_luau_ring_try_push_command(rt, cmd.host_id, &cmd) == basis_luau_ring_ok;
}

static int basis_native_handle_cmd(lua_State* L, basis_luau_command_type type, int handle_arg, int data_start, int data_count)
{
    uint32_t index = 0;
    uint32_t generation = 0;
    decode_handle(lua_tonumber(L, handle_arg), &index, &generation);

    float data[4] = {0, 0, 0, 0};
    for (int i = 0; i < data_count && i < 4; ++i) {
        data[i] = (float)lua_tonumber(L, data_start + i);
    }

    basis_native_enqueue(L, type, index, generation, data);
    return 0;
}

static int basis_native_set_position(lua_State* L)
{
    return basis_native_handle_cmd(L, basis_luau_cmd_set_position, 1, 2, 3);
}

static int basis_native_set_local_position(lua_State* L)
{
    return basis_native_handle_cmd(L, basis_luau_cmd_set_local_position, 1, 2, 3);
}

static int basis_native_set_euler(lua_State* L)
{
    return basis_native_handle_cmd(L, basis_luau_cmd_set_rotation, 1, 2, 3);
}

static int basis_native_rotate(lua_State* L)
{
    return basis_native_handle_cmd(L, basis_luau_cmd_rotate, 1, 2, 3);
}

static int basis_native_rotate_local(lua_State* L)
{
    float data[4];
    data[0] = (float)lua_tonumber(L, 2);
    data[1] = (float)lua_tonumber(L, 3);
    data[2] = (float)lua_tonumber(L, 4);
    data[3] = 1.0f;
    uint32_t index = 0;
    uint32_t generation = 0;
    decode_handle(lua_tonumber(L, 1), &index, &generation);
    basis_native_enqueue(L, basis_luau_cmd_rotate, index, generation, data);
    return 0;
}

static int basis_native_read_snapshot(lua_State* L, int use_local)
{
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (!rt) {
        lua_pushnumber(L, 0);
        lua_pushnumber(L, 0);
        lua_pushnumber(L, 0);
        return 3;
    }

    uint32_t index = 0;
    uint32_t generation = 0;
    decode_handle(lua_tonumber(L, 1), &index, &generation);

    basis_luau_snapshot_slot slot;
    memset(&slot, 0, sizeof(slot));
    uint64_t epoch = 0;
    basis_luau_snapshot_read_begin(rt, &epoch);
    basis_luau_snapshot_read_slot(rt, index, &slot);
    basis_luau_snapshot_read_end(rt, epoch);

    if (slot.handle_generation != generation) {
        lua_pushnumber(L, 0);
        lua_pushnumber(L, 0);
        lua_pushnumber(L, 0);
        return 3;
    }

    if (use_local) {
        lua_pushnumber(L, slot.position[0]);
        lua_pushnumber(L, slot.position[1]);
        lua_pushnumber(L, slot.position[2]);
        return 3;
    }

    lua_pushnumber(L, slot.position[0]);
    lua_pushnumber(L, slot.position[1]);
    lua_pushnumber(L, slot.position[2]);
    return 3;
}

static int basis_native_get_position(lua_State* L)
{
    return basis_native_read_snapshot(L, 0);
}

static int basis_native_get_local_position(lua_State* L)
{
    return basis_native_read_snapshot(L, 1);
}

static int basis_native_set_ui_text(lua_State* L)
{
    uint32_t index = 0;
    uint32_t generation = 0;
    decode_handle(lua_tonumber(L, 1), &index, &generation);

    float data[4] = {0, 0, 0, 0};
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (rt && lua_isstring(L, 2)) {
        size_t len = 0;
        const char* text = lua_tolstring(L, 2, &len);
        basis_luau_buffer_ref ref;
        uint8_t* bytes = NULL;
        if (basis_luau_buffer_alloc(rt, basis_luau_runtime_host_id(rt), (uint32_t)len + 1, &ref, &bytes)) {
            memcpy(bytes, text, len);
            bytes[len] = 0;
            data[0] = (float)ref.slot;
            data[1] = (float)ref.generation;
            data[2] = (float)len;
        }
    }

    basis_native_enqueue(L, basis_luau_cmd_set_ui_text, index, generation, data);
    return 0;
}

static int basis_native_log(lua_State* L)
{
    float data[4] = {0, 0, 0, 0};
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (rt && lua_isstring(L, 1)) {
        size_t len = 0;
        const char* text = lua_tolstring(L, 1, &len);
        basis_luau_buffer_ref ref;
        uint8_t* bytes = NULL;
        if (basis_luau_buffer_alloc(rt, basis_luau_runtime_host_id(rt), (uint32_t)len + 1, &ref, &bytes)) {
            memcpy(bytes, text, len);
            bytes[len] = 0;
            data[0] = (float)ref.slot;
            data[1] = (float)ref.generation;
            data[2] = (float)len;
        }
    }
    basis_native_enqueue(L, basis_luau_cmd_log, 0, 0, data);
    return 0;
}

static int basis_native_warn(lua_State* L)
{
    float data[4] = {0, 0, 0, 0};
    basis_native_enqueue(L, basis_luau_cmd_warn, 0, 0, data);
    return 0;
}

static int basis_native_error(lua_State* L)
{
    float data[4] = {0, 0, 0, 0};
    basis_native_enqueue(L, basis_luau_cmd_error, 0, 0, data);
    return 0;
}

static int basis_native_destroy(lua_State* L)
{
    return basis_native_handle_cmd(L, basis_luau_cmd_destroy_object, 1, 2, 0);
}

static int basis_native_ticket_handle(lua_State* L, basis_luau_command_type type)
{
    return basis_native_handle_cmd(L, type, 1, 2, 0);
}

static int basis_native_clone(lua_State* L)
{
    return basis_native_ticket_handle(L, basis_luau_cmd_ticket_clone);
}

static int basis_native_download_image(lua_State* L)
{
    float data[4] = {0, 0, 0, 0};
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (rt && lua_isstring(L, 1)) {
        size_t len = 0;
        const char* url = lua_tolstring(L, 1, &len);
        basis_luau_buffer_ref ref;
        uint8_t* bytes = NULL;
        if (basis_luau_buffer_alloc(rt, basis_luau_runtime_host_id(rt), (uint32_t)len + 1, &ref, &bytes)) {
            memcpy(bytes, url, len);
            bytes[len] = 0;
            data[0] = (float)ref.slot;
            data[1] = (float)ref.generation;
            data[2] = (float)len;
        }
    }
    basis_native_enqueue(L, basis_luau_cmd_ticket_download_image, 0, 0, data);
    return 0;
}

static int basis_native_read_time(lua_State* L, int component)
{
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (!rt) {
        lua_pushnumber(L, 0);
        return 1;
    }

    basis_luau_snapshot_slot slot;
    memset(&slot, 0, sizeof(slot));
    uint64_t epoch = 0;
    basis_luau_snapshot_read_begin(rt, &epoch);
    basis_luau_snapshot_read_slot(rt, BASIS_LUAU_SNAPSHOT_TIME_SLOT, &slot);
    basis_luau_snapshot_read_end(rt, epoch);

    lua_pushnumber(L, slot.time_data[component]);
    return 1;
}

static int basis_native_delta_time(lua_State* L)
{
    return basis_native_read_time(L, 0);
}

static int basis_native_fixed_delta_time(lua_State* L)
{
    return basis_native_read_time(L, 1);
}

static int basis_native_time(lua_State* L)
{
    return basis_native_read_time(L, 2);
}

static int basis_native_unscaled_delta_time(lua_State* L)
{
    return basis_native_read_time(L, 3);
}

static int basis_native_now(lua_State* L)
{
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (!rt) {
        lua_pushstring(L, "");
        return 1;
    }

    uint8_t* bytes = NULL;
    uint32_t length = 0;
    if (basis_luau_runtime_read_datetime(rt, &bytes, &length) && bytes && length > 0) {
        lua_pushlstring(L, (const char*)bytes, length);
        return 1;
    }

    lua_pushstring(L, "");
    return 1;
}

static void register_fn(lua_State* L, const char* name, lua_CFunction fn)
{
    lua_pushcfunction(L, fn, name);
    lua_setfield(L, -2, name);
}

static void register_library(lua_State* L, const char* name, const luaL_Reg* funcs)
{
    luaL_register(L, name, funcs);
    lua_pop(L, 1);
}

static int basis_native_string_ticket(lua_State* L, basis_luau_command_type type, int string_arg, float extra_data)
{
    float data[4] = {0, 0, 0, extra_data};
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (rt && lua_isstring(L, string_arg)) {
        size_t len = 0;
        const char* text = lua_tolstring(L, string_arg, &len);
        basis_luau_buffer_ref ref;
        uint8_t* bytes = NULL;
        if (basis_luau_buffer_alloc(rt, basis_luau_runtime_host_id(rt), (uint32_t)len + 1, &ref, &bytes)) {
            memcpy(bytes, text, len);
            bytes[len] = 0;
            data[0] = (float)ref.slot;
            data[1] = (float)ref.generation;
            data[2] = (float)len;
        }
    }
    basis_native_enqueue(L, type, 0, 0, data);
    return 0;
}

static int basis_native_osc_publish_float(lua_State* L)
{
    float data[4] = {0, 0, 0, (float)lua_tonumber(L, 2)};
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (rt && lua_isstring(L, 1)) {
        size_t len = 0;
        const char* text = lua_tolstring(L, 1, &len);
        basis_luau_buffer_ref ref;
        uint8_t* bytes = NULL;
        if (basis_luau_buffer_alloc(rt, basis_luau_runtime_host_id(rt), (uint32_t)len + 1, &ref, &bytes)) {
            memcpy(bytes, text, len);
            bytes[len] = 0;
            data[0] = (float)ref.slot;
            data[1] = (float)ref.generation;
            data[2] = (float)len;
        }
    }
    basis_native_enqueue(L, basis_luau_cmd_ticket_osc_publish_float, 0, 0, data);
    return 0;
}

static int basis_native_osc_publish_int(lua_State* L)
{
    float data[4] = {0, 0, 0, (float)lua_tointeger(L, 2)};
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (rt && lua_isstring(L, 1)) {
        size_t len = 0;
        const char* text = lua_tolstring(L, 1, &len);
        basis_luau_buffer_ref ref;
        uint8_t* bytes = NULL;
        if (basis_luau_buffer_alloc(rt, basis_luau_runtime_host_id(rt), (uint32_t)len + 1, &ref, &bytes)) {
            memcpy(bytes, text, len);
            bytes[len] = 0;
            data[0] = (float)ref.slot;
            data[1] = (float)ref.generation;
            data[2] = (float)len;
        }
    }
    basis_native_enqueue(L, basis_luau_cmd_ticket_osc_publish_int, 0, 0, data);
    return 0;
}

static int basis_native_osc_publish_bool(lua_State* L)
{
    float data[4] = {0, 0, 0, lua_toboolean(L, 2) ? 1.0f : 0.0f};
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (rt && lua_isstring(L, 1)) {
        size_t len = 0;
        const char* text = lua_tolstring(L, 1, &len);
        basis_luau_buffer_ref ref;
        uint8_t* bytes = NULL;
        if (basis_luau_buffer_alloc(rt, basis_luau_runtime_host_id(rt), (uint32_t)len + 1, &ref, &bytes)) {
            memcpy(bytes, text, len);
            bytes[len] = 0;
            data[0] = (float)ref.slot;
            data[1] = (float)ref.generation;
            data[2] = (float)len;
        }
    }
    basis_native_enqueue(L, basis_luau_cmd_ticket_osc_publish_bool, 0, 0, data);
    return 0;
}

static int basis_native_osc_publish_string(lua_State* L)
{
    return basis_native_string_ticket(L, basis_luau_cmd_ticket_osc_publish_string, 1, 0);
}

static int basis_native_osc_subscribe(lua_State* L)
{
    if (!lua_isfunction(L, 2)) {
        return 0;
    }

    lua_pushvalue(L, 2);
    int callback_ref = lua_ref(L, -1);
    lua_pop(L, 1);
    float data[4] = {(float)callback_ref, 0, 0, 0};
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (rt && lua_isstring(L, 1)) {
        size_t len = 0;
        const char* text = lua_tolstring(L, 1, &len);
        basis_luau_buffer_ref ref;
        uint8_t* bytes = NULL;
        if (basis_luau_buffer_alloc(rt, basis_luau_runtime_host_id(rt), (uint32_t)len + 1, &ref, &bytes)) {
            memcpy(bytes, text, len);
            bytes[len] = 0;
            data[1] = (float)ref.slot;
            data[2] = (float)ref.generation;
            data[3] = (float)len;
        }
    }
    basis_native_enqueue(L, basis_luau_cmd_ticket_osc_subscribe, 0, 0, data);
    return 0;
}

static int basis_native_network_send(lua_State* L)
{
    return basis_native_ticket_handle(L, basis_luau_cmd_ticket_network_send);
}

static int basis_native_network_take_ownership(lua_State* L)
{
    return basis_native_ticket_handle(L, basis_luau_cmd_ticket_take_ownership);
}

static int basis_native_network_make_networkable(lua_State* L)
{
    return basis_native_ticket_handle(L, basis_luau_cmd_ticket_make_networkable);
}

static int basis_native_make_interactable(lua_State* L)
{
    return basis_native_ticket_handle(L, basis_luau_cmd_ticket_make_interactable);
}

static int basis_native_avatar_resolve(lua_State* L)
{
    return basis_native_ticket_handle(L, basis_luau_cmd_ticket_avatar_resolve);
}

static void register_osc(lua_State* L)
{
    static const luaL_Reg funcs[] = {
        {"publishFloat", basis_native_osc_publish_float},
        {"publishInt", basis_native_osc_publish_int},
        {"publishBool", basis_native_osc_publish_bool},
        {"publishString", basis_native_osc_publish_string},
        {"subscribe", basis_native_osc_subscribe},
        {NULL, NULL},
    };
    register_library(L, "basis_osc", funcs);
}

static void register_network(lua_State* L)
{
    static const luaL_Reg funcs[] = {
        {"sendBytes", basis_native_network_send},
        {"takeOwnership", basis_native_network_take_ownership},
        {"makeNetworkable", basis_native_network_make_networkable},
        {NULL, NULL},
    };
    register_library(L, "basis_network", funcs);
}

static void register_avatar(lua_State* L)
{
    static const luaL_Reg funcs[] = {
        {"resolve", basis_native_avatar_resolve},
        {NULL, NULL},
    };
    register_library(L, "basis_avatar", funcs);
}

static void register_interact(lua_State* L)
{
    static const luaL_Reg funcs[] = {
        {"makeInteractable", basis_native_make_interactable},
        {NULL, NULL},
    };
    register_library(L, "basis_interact", funcs);
}

static void register_transform(lua_State* L)
{
    static const luaL_Reg funcs[] = {
        {"setPosition", basis_native_set_position},
        {"setLocalPosition", basis_native_set_local_position},
        {"setEuler", basis_native_set_euler},
        {"rotate", basis_native_rotate},
        {"rotateLocal", basis_native_rotate_local},
        {"getPosition", basis_native_get_position},
        {"getLocalPosition", basis_native_get_local_position},
        {NULL, NULL},
    };
    register_library(L, "transform", funcs);
}

static void register_time(lua_State* L)
{
    static const luaL_Reg funcs[] = {
        {"deltaTime", basis_native_delta_time},
        {"fixedDeltaTime", basis_native_fixed_delta_time},
        {"time", basis_native_time},
        {"unscaledDeltaTime", basis_native_unscaled_delta_time},
        {"now", basis_native_now},
        {NULL, NULL},
    };
    register_library(L, "basis_time", funcs);
}

static void register_ui(lua_State* L)
{
    static const luaL_Reg funcs[] = {
        {"setText", basis_native_set_ui_text},
        {NULL, NULL},
    };
    register_library(L, "basis_ui", funcs);
}

static void register_util(lua_State* L)
{
    static const luaL_Reg funcs[] = {
        {"log", basis_native_log},
        {"warn", basis_native_warn},
        {"error", basis_native_error},
        {NULL, NULL},
    };
    register_library(L, "basis_util", funcs);
}

static void register_instantiate(lua_State* L)
{
    static const luaL_Reg funcs[] = {
        {"destroy", basis_native_destroy},
        {"clone", basis_native_clone},
        {NULL, NULL},
    };
    register_library(L, "basis_instantiate", funcs);
}

static void register_image(lua_State* L)
{
    static const luaL_Reg funcs[] = {
        {"downloadImage", basis_native_download_image},
        {NULL, NULL},
    };
    register_library(L, "basis_image", funcs);
}

void basis_luau_register_native_bindings(lua_State* L, basis_luau_runtime* rt, int host_kind)
{
    (void)host_kind;

    lua_pushlightuserdata(L, (void*)REG_RT_KEY);
    lua_pushlightuserdata(L, rt);
    lua_rawset(L, LUA_REGISTRYINDEX);

    register_transform(L);
    register_time(L);
    register_ui(L);
    register_util(L);
    register_instantiate(L);
    register_image(L);
    register_osc(L);
    register_network(L);
    register_avatar(L);
    register_interact(L);

    lua_getglobal(L, "game");
    if (!lua_istable(L, -1)) {
        lua_pop(L, 1);
        lua_createtable(L, 0, 0);
        lua_setglobal(L, "game");
    } else {
        lua_pop(L, 1);
    }
}
