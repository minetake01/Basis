#define BASIS_LUAU_LIMITS_EXPORT 1

#include "basis_luau_limits.h"
#include "luau_ffi_exports.h"

#include <limits.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <time.h>
#endif

typedef struct basis_luau_limits_ctx {
    uint64_t memory_cap_bytes;
    uint64_t allocated_bytes;
    int64_t deadline_ns;
    basis_luau_disable_reason last_reason;
    lua_State* root_L;
    int released;
} basis_luau_limits_ctx;

#if defined(_WIN32)
static int64_t basis_monotonic_ns(void)
{
    static LARGE_INTEGER frequency = {0};
    LARGE_INTEGER counter;
    if (frequency.QuadPart == 0) {
        QueryPerformanceFrequency(&frequency);
    }
    QueryPerformanceCounter(&counter);
    const int64_t seconds = (int64_t)(counter.QuadPart / frequency.QuadPart);
    const int64_t remainder = (int64_t)(counter.QuadPart % frequency.QuadPart);
    return seconds * 1000000000LL + remainder * 1000000000LL / (int64_t)frequency.QuadPart;
}
#else
static int64_t basis_monotonic_ns(void)
{
#if defined(CLOCK_MONOTONIC)
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000000000LL + (int64_t)ts.tv_nsec;
#else
    return 0;
#endif
}
#endif

static basis_luau_limits_ctx* basis_ctx(lua_State* L)
{
    if (!L) {
        return NULL;
    }

    lua_Callbacks* callbacks = ffi_lua_callbacks(L);
    return callbacks ? (basis_luau_limits_ctx*)callbacks->userdata : NULL;
}

static void basis_luau_interrupt(lua_State* L, int gc)
{
    (void)gc;
    basis_luau_limits_ctx* ctx = basis_ctx(L);
    if (!ctx || ctx->released || ctx->deadline_ns <= 0) {
        return;
    }

    if (basis_monotonic_ns() >= ctx->deadline_ns) {
        ctx->last_reason = basis_luau_disable_timeout;
        ffi_luaL_error_msg(L, "execution time limit exceeded");
    }
}

static void basis_maybe_release_ctx(basis_luau_limits_ctx* ctx)
{
    if (!ctx || ctx->released) {
        return;
    }

    if (ctx->allocated_bytes == 0 && ctx->root_L != NULL) {
        ctx->released = 1;
        ctx->root_L = NULL;
        free(ctx);
    }
}

static void* basis_luau_alloc(void* ud, void* ptr, size_t osize, size_t nsize)
{
    basis_luau_limits_ctx* ctx = (basis_luau_limits_ctx*)ud;
    if (!ctx || ctx->released) {
        return NULL;
    }

    if (nsize == 0) {
        if (ptr != NULL && osize > 0) {
            if ((uint64_t)osize > ctx->allocated_bytes) {
                ctx->last_reason = basis_luau_disable_internal;
                return NULL;
            }
            ctx->allocated_bytes -= (uint64_t)osize;
        }
        free(ptr);
        basis_maybe_release_ctx(ctx);
        return NULL;
    }

    uint64_t current = ctx->allocated_bytes;
    if (osize > 0 && ptr != NULL) {
        if ((uint64_t)osize > current) {
            ctx->last_reason = basis_luau_disable_internal;
            return NULL;
        }
        current -= (uint64_t)osize;
    }

    if (current > ctx->memory_cap_bytes || (uint64_t)nsize > ctx->memory_cap_bytes - current) {
        ctx->last_reason = basis_luau_disable_alloc_failure;
        return NULL;
    }

    void* next = realloc(ptr, nsize);
    if (next == NULL) {
        ctx->last_reason = basis_luau_disable_alloc_failure;
        return NULL;
    }

    ctx->allocated_bytes = current + (uint64_t)nsize;
    return next;
}

BASIS_LUAU_API lua_State* basis_luau_newstate_with_limits(
    const basis_luau_limits_config* config,
    basis_luau_init_error* out_error)
{
    if (out_error) {
        *out_error = basis_luau_init_none;
    }

    if (!config) {
        if (out_error) {
            *out_error = basis_luau_init_invalid_config;
        }
        return NULL;
    }

    basis_luau_limits_ctx* ctx = (basis_luau_limits_ctx*)calloc(1, sizeof(basis_luau_limits_ctx));
    if (!ctx) {
        if (out_error) {
            *out_error = basis_luau_init_ctx_alloc_failed;
        }
        return NULL;
    }

    ctx->memory_cap_bytes = config->memory_cap_bytes > 0 ? config->memory_cap_bytes : (8ull * 1024ull * 1024ull);

    lua_State* L = ffi_lua_newstate(basis_luau_alloc, ctx);
    if (!L) {
        free(ctx);
        if (out_error) {
            *out_error = basis_luau_init_vm_alloc_failed;
        }
        return NULL;
    }

    ctx->root_L = L;

    lua_Callbacks* callbacks = ffi_lua_callbacks(L);
    if (callbacks) {
        callbacks->userdata = ctx;
        callbacks->interrupt = basis_luau_interrupt;
    }

    return L;
}

BASIS_LUAU_API void basis_luau_set_execution_deadline(lua_State* L, int64_t deadline_ns_monotonic)
{
    basis_luau_limits_ctx* ctx = basis_ctx(L);
    if (ctx && !ctx->released) {
        ctx->deadline_ns = deadline_ns_monotonic;
    }
}

BASIS_LUAU_API void basis_luau_begin_execution(lua_State* L, int64_t budget_ns)
{
    basis_luau_limits_ctx* ctx = basis_ctx(L);
    if (!ctx || ctx->released) {
        return;
    }

    ctx->last_reason = basis_luau_disable_none;

    if (budget_ns <= 0) {
        basis_luau_set_execution_deadline(L, 0);
        return;
    }

    const int64_t now = basis_monotonic_ns();
    if (budget_ns > 0 && now > INT64_MAX - budget_ns) {
        ctx->last_reason = basis_luau_disable_timeout;
        basis_luau_set_execution_deadline(L, now);
        return;
    }

    basis_luau_set_execution_deadline(L, now + budget_ns);
}

BASIS_LUAU_API void basis_luau_end_execution(lua_State* L)
{
    basis_luau_set_execution_deadline(L, 0);
}

BASIS_LUAU_API uint64_t basis_luau_total_bytes(lua_State* L)
{
    if (L) {
        return (uint64_t)ffi_lua_totalbytes(L, -1);
    }

    basis_luau_limits_ctx* ctx = basis_ctx(L);
    return ctx && !ctx->released ? ctx->allocated_bytes : 0;
}

BASIS_LUAU_API uint64_t basis_luau_memory_cap(lua_State* L)
{
    basis_luau_limits_ctx* ctx = basis_ctx(L);
    return ctx && !ctx->released ? ctx->memory_cap_bytes : 0;
}

BASIS_LUAU_API basis_luau_disable_reason basis_luau_last_disable_reason(lua_State* L)
{
    basis_luau_limits_ctx* ctx = basis_ctx(L);
    return ctx && !ctx->released ? ctx->last_reason : basis_luau_disable_none;
}

#if defined(_WIN32)
BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    (void)inst;
    (void)reason;
    (void)reserved;
    return TRUE;
}
#endif
