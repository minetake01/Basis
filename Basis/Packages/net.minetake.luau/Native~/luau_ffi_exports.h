#pragma once

#include <stddef.h>
#include <stdint.h>

#include "lua.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef lua_State* (*basis_ffi_lua_newstate_fn)(lua_Alloc f, void* ud);

lua_State* ffi_lua_newstate(lua_Alloc f, void* ud);
void ffi_lua_close(lua_State* L);
lua_Callbacks* ffi_lua_callbacks(lua_State* L);
size_t ffi_lua_totalbytes(lua_State* L, int category);
int ffi_luaL_error_msg(lua_State* L, const char* msg);

#ifdef __cplusplus
}
#endif
