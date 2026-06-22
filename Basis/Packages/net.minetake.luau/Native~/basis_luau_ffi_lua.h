#pragma once

// Native runtime links against libluau.import.lib, which exports ffi_lua_* entry points.
// Include after lua.h / lualib.h so call sites resolve to libluau.dll.

#ifdef __cplusplus
extern "C" {
#endif

void ffi_lua_settop(lua_State* L, int idx);
void ffi_lua_pushvalue(lua_State* L, int idx);
void ffi_lua_insert(lua_State* L, int idx);
int ffi_lua_isstring(lua_State* L, int idx);
int ffi_lua_type(lua_State* L, int idx);
double ffi_lua_tonumberx(lua_State* L, int idx, int* isnum);
int ffi_lua_tointegerx(lua_State* L, int idx, int* isnum);
int ffi_lua_toboolean(lua_State* L, int idx);
const char* ffi_lua_tolstring(lua_State* L, int idx, size_t* len);
void* ffi_lua_touserdata(lua_State* L, int idx);
void ffi_lua_pushnumber(lua_State* L, double n);
void ffi_lua_pushlstring(lua_State* L, const char* s, size_t len);
void ffi_lua_pushstring(lua_State* L, const char* s);
void ffi_lua_pushlightuserdatatagged(lua_State* L, void* p, int tag);
void ffi_lua_pushcclosurek(lua_State* L, lua_CFunction fn, const char* debugname, int nup, lua_Continuation cont);
void ffi_lua_getfield(lua_State* L, int idx, const char* k);
void ffi_lua_rawget(lua_State* L, int idx);
void ffi_lua_rawgeti(lua_State* L, int idx, int n);
void ffi_lua_rawseti(lua_State* L, int idx, int n);
void ffi_lua_createtable(lua_State* L, int narr, int nrec);
void ffi_lua_setfield(lua_State* L, int idx, const char* k);
void ffi_lua_rawset(lua_State* L, int idx);
int ffi_lua_pcall(lua_State* L, int nargs, int nresults, int errfunc);
int ffi_lua_ref(lua_State* L, int idx);
void ffi_luaL_register(lua_State* L, const char* libname, const luaL_Reg* l);

#ifdef __cplusplus
}
#endif

#define lua_settop ffi_lua_settop
#define lua_pushvalue ffi_lua_pushvalue
#define lua_insert ffi_lua_insert
#define lua_isstring ffi_lua_isstring
#define lua_type ffi_lua_type
#define lua_tonumberx ffi_lua_tonumberx
#define lua_tointegerx ffi_lua_tointegerx
#define lua_toboolean ffi_lua_toboolean
#define lua_tolstring ffi_lua_tolstring
#define lua_touserdata ffi_lua_touserdata
#define lua_pushnumber ffi_lua_pushnumber
#define lua_pushlstring ffi_lua_pushlstring
#define lua_pushstring ffi_lua_pushstring
#define lua_pushlightuserdatatagged ffi_lua_pushlightuserdatatagged
#define lua_pushcclosurek ffi_lua_pushcclosurek
#define lua_getfield ffi_lua_getfield
#define lua_rawget ffi_lua_rawget
#define lua_rawgeti ffi_lua_rawgeti
#define lua_rawseti ffi_lua_rawseti
#define lua_createtable ffi_lua_createtable
#define lua_setfield ffi_lua_setfield
#define lua_rawset ffi_lua_rawset
#define lua_pcall ffi_lua_pcall
#define lua_ref ffi_lua_ref
#define luaL_register ffi_luaL_register
