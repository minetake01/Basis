#include "lua.h"
#include "lualib.h"

#if defined(_WIN32)
#  define BASIS_LUAU_FFI_EXPORT __declspec(dllexport)
#else
#  define BASIS_LUAU_FFI_EXPORT __attribute__((visibility("default")))
#endif

BASIS_LUAU_FFI_EXPORT int ffi_luaL_error_msg(lua_State* L, const char* msg)
{
    luaL_error(L, "%s", msg);
    return 0;
}
