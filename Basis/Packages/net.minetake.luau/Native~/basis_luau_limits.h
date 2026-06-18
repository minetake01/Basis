#pragma once

#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#  ifdef BASIS_LUAU_LIMITS_EXPORT
#    define BASIS_LUAU_API __declspec(dllexport)
#  else
#    define BASIS_LUAU_API __declspec(dllimport)
#  endif
#else
#  define BASIS_LUAU_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct lua_State lua_State;

typedef struct basis_luau_limits_config {
    uint64_t memory_cap_bytes;
} basis_luau_limits_config;

typedef enum basis_luau_disable_reason {
    basis_luau_disable_none = 0,
    basis_luau_disable_timeout = 1,
    basis_luau_disable_alloc_failure = 2,
    basis_luau_disable_panic = 3,
    basis_luau_disable_internal = 4,
} basis_luau_disable_reason;

/* Creates a root state with custom allocator + native interrupt hook installed. */
BASIS_LUAU_API lua_State* basis_luau_newstate_with_limits(const basis_luau_limits_config* config);

/* Sets monotonic deadline (nanoseconds). 0 disables time limit until next BeginExecution. */
BASIS_LUAU_API void basis_luau_set_execution_deadline(lua_State* L, int64_t deadline_ns_monotonic);

/* Call immediately before a protected script invocation. */
BASIS_LUAU_API void basis_luau_begin_execution(lua_State* L, int64_t budget_ns);

BASIS_LUAU_API void basis_luau_end_execution(lua_State* L);

BASIS_LUAU_API uint64_t basis_luau_total_bytes(lua_State* L);

BASIS_LUAU_API uint64_t basis_luau_memory_cap(lua_State* L);

BASIS_LUAU_API basis_luau_disable_reason basis_luau_last_disable_reason(lua_State* L);

#ifdef __cplusplus
}
#endif
