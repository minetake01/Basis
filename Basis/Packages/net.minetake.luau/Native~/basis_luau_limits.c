#define BASIS_LUAU_LIMITS_EXPORT 1

#include "basis_luau_limits.h"

#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <time.h>
#endif

typedef void* (*lua_Alloc_fn)(void* ud, void* ptr, size_t osize, size_t nsize);
typedef lua_State* (*lua_newstate_fn)(lua_Alloc_fn f, void* ud);
typedef void (*lua_close_fn)(lua_State* L);
typedef void* (*lua_getthreaddata_fn)(lua_State* L);
typedef void (*lua_setthreaddata_fn)(lua_State* L, void* data);
typedef size_t (*lua_totalbytes_fn)(lua_State* L, int category);

typedef struct lua_Callbacks {
    void* userdata;
    void (*interrupt)(lua_State* L, int gc);
    void (*panic)(lua_State* L, int errcode);
    void (*userthread)(lua_State* LP, lua_State* L);
    int16_t (*useratom)(lua_State* L, const char* s, size_t l);
    void (*debugbreak)(lua_State* L, void* ar);
    void (*debugstep)(lua_State* L, void* ar);
    void (*debuginterrupt)(lua_State* L, void* ar);
    void (*debugprotectederror)(lua_State* L);
    void (*onallocate)(lua_State* L, size_t osize, size_t nsize);
} lua_Callbacks;

typedef lua_Callbacks* (*lua_callbacks_fn)(lua_State* L);
typedef int (*luaL_error_fn)(lua_State* L, const char* fmt, ...);

typedef struct {
    uint64_t memory_cap_bytes;
    uint64_t allocated_bytes;
    int64_t deadline_ns;
    basis_luau_disable_reason last_reason;
} basis_luau_limits_ctx;

static lua_newstate_fn p_lua_newstate;
static lua_close_fn p_lua_close;
static lua_getthreaddata_fn p_lua_getthreaddata;
static lua_setthreaddata_fn p_lua_setthreaddata;
static lua_totalbytes_fn p_lua_totalbytes;
static lua_callbacks_fn p_lua_callbacks;
static luaL_error_fn p_luaL_error;

#if defined(_WIN32)
static int64_t basis_monotonic_ns(void)
{
    static LARGE_INTEGER frequency = {0};
    LARGE_INTEGER counter;
    if (frequency.QuadPart == 0) {
        QueryPerformanceFrequency(&frequency);
    }
    QueryPerformanceCounter(&counter);
    return (int64_t)((counter.QuadPart * 1000000000LL) / frequency.QuadPart);
}

static void* basis_resolve(const char* name)
{
    HMODULE mod = GetModuleHandleA("libluau");
    if (!mod) {
        mod = LoadLibraryA("libluau.dll");
    }
    if (!mod) {
        return NULL;
    }
    return (void*)GetProcAddress(mod, name);
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

static void* basis_resolve(const char* name)
{
    return NULL;
}
#endif

static int basis_ensure_api(void)
{
    if (p_lua_newstate) {
        return 1;
    }

    p_lua_newstate = (lua_newstate_fn)basis_resolve("ffi_lua_newstate");
    p_lua_close = (lua_close_fn)basis_resolve("ffi_lua_close");
    p_lua_getthreaddata = (lua_getthreaddata_fn)basis_resolve("ffi_lua_getthreaddata");
    p_lua_setthreaddata = (lua_setthreaddata_fn)basis_resolve("ffi_lua_setthreaddata");
    p_lua_totalbytes = (lua_totalbytes_fn)basis_resolve("ffi_lua_totalbytes");
    p_lua_callbacks = (lua_callbacks_fn)basis_resolve("ffi_lua_callbacks");
    p_luaL_error = (luaL_error_fn)basis_resolve("ffi_luaL_error");

    return p_lua_newstate && p_lua_close && p_lua_callbacks && p_luaL_error;
}

static basis_luau_limits_ctx* basis_ctx_from_state(lua_State* L)
{
    if (!L || !p_lua_getthreaddata) {
        return NULL;
    }
    return (basis_luau_limits_ctx*)p_lua_getthreaddata(L);
}

static void basis_luau_interrupt(lua_State* L, int gc)
{
    (void)gc;
    basis_luau_limits_ctx* ctx = basis_ctx_from_state(L);
    if (!ctx || ctx->deadline_ns <= 0) {
        return;
    }
    if (basis_monotonic_ns() >= ctx->deadline_ns) {
        ctx->last_reason = basis_luau_disable_timeout;
        p_luaL_error(L, "execution time limit exceeded");
    }
}

static void* basis_luau_alloc(void* ud, void* ptr, size_t osize, size_t nsize)
{
    basis_luau_limits_ctx* ctx = (basis_luau_limits_ctx*)ud;
    if (nsize == 0) {
        if (ptr != NULL && osize > 0) {
            ctx->allocated_bytes -= (uint64_t)osize;
        }
        free(ptr);
        return NULL;
    }

    uint64_t current = ctx->allocated_bytes;
    if (osize > 0 && ptr != NULL) {
        current -= (uint64_t)osize;
    }
    if (current + (uint64_t)nsize > ctx->memory_cap_bytes) {
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

BASIS_LUAU_API lua_State* basis_luau_newstate_with_limits(const basis_luau_limits_config* config)
{
    if (!config || !basis_ensure_api()) {
        return NULL;
    }

    basis_luau_limits_ctx* ctx = (basis_luau_limits_ctx*)calloc(1, sizeof(basis_luau_limits_ctx));
    if (!ctx) {
        return NULL;
    }

    ctx->memory_cap_bytes = config->memory_cap_bytes > 0 ? config->memory_cap_bytes : (8ull * 1024ull * 1024ull);

    lua_State* L = p_lua_newstate(basis_luau_alloc, ctx);
    if (!L) {
        free(ctx);
        return NULL;
    }

    if (p_lua_setthreaddata) {
        p_lua_setthreaddata(L, ctx);
    }

    lua_Callbacks* callbacks = p_lua_callbacks(L);
    if (callbacks) {
        callbacks->userdata = ctx;
        callbacks->interrupt = basis_luau_interrupt;
    }

    return L;
}

BASIS_LUAU_API void basis_luau_set_execution_deadline(lua_State* L, int64_t deadline_ns_monotonic)
{
    basis_luau_limits_ctx* ctx = basis_ctx_from_state(L);
    if (ctx) {
        ctx->deadline_ns = deadline_ns_monotonic;
    }
}

BASIS_LUAU_API void basis_luau_begin_execution(lua_State* L, int64_t budget_ns)
{
    if (budget_ns <= 0) {
        basis_luau_set_execution_deadline(L, 0);
        return;
    }
    basis_luau_set_execution_deadline(L, basis_monotonic_ns() + budget_ns);
}

BASIS_LUAU_API void basis_luau_end_execution(lua_State* L)
{
    basis_luau_set_execution_deadline(L, 0);
}

BASIS_LUAU_API uint64_t basis_luau_total_bytes(lua_State* L)
{
    if (L && p_lua_totalbytes) {
        return (uint64_t)p_lua_totalbytes(L, -1);
    }
    basis_luau_limits_ctx* ctx = basis_ctx_from_state(L);
    return ctx ? ctx->allocated_bytes : 0;
}

BASIS_LUAU_API uint64_t basis_luau_memory_cap(lua_State* L)
{
    basis_luau_limits_ctx* ctx = basis_ctx_from_state(L);
    return ctx ? ctx->memory_cap_bytes : 0;
}

BASIS_LUAU_API basis_luau_disable_reason basis_luau_last_disable_reason(lua_State* L)
{
    basis_luau_limits_ctx* ctx = basis_ctx_from_state(L);
    return ctx ? ctx->last_reason : basis_luau_disable_none;
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
