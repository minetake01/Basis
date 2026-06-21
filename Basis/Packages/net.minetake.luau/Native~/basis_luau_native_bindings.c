#include "basis_luau_runtime.h"

#include "luau_ffi_exports.h"

#include <string.h>

struct basis_luau_runtime;

extern basis_luau_ring_result basis_luau_ring_try_push_command(basis_luau_runtime* rt, uint32_t host_id, const basis_luau_command* cmd);

static basis_luau_runtime* basis_rt_from_state(lua_State* L)
{
    (void)L;
    return NULL;
}

static int basis_native_enqueue(lua_State* L, basis_luau_command_type type)
{
    basis_luau_runtime* rt = basis_rt_from_state(L);
    if (!rt) {
        return 0;
    }

    basis_luau_command cmd;
    memset(&cmd, 0, sizeof(cmd));
    cmd.type = (uint16_t)type;
    cmd.data[0] = (float)ffi_lua_tonumber(L, 2);
    cmd.data[1] = (float)ffi_lua_tonumber(L, 3);
    cmd.data[2] = (float)ffi_lua_tonumber(L, 4);

    return basis_luau_ring_try_push_command(rt, 0, &cmd) == basis_luau_ring_ok;
}

static int basis_native_set_position(lua_State* L)
{
    return basis_native_enqueue(L, basis_luau_cmd_set_position);
}

void basis_luau_register_native_bindings(lua_State* L, basis_luau_runtime* rt)
{
    (void)L;
    (void)rt;
    (void)basis_native_set_position;
}
