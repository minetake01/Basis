#include "basis_luau_internal.h"

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN
#  include <windows.h>
#else
#  include <pthread.h>
#  include <unistd.h>
#endif

#include <stdlib.h>

struct basis_luau_worker {
    basis_luau_runtime* rt;
#if defined(_WIN32)
    HANDLE thread;
    HANDLE wake_event;
    volatile LONG stop;
#else
    pthread_t thread;
    int stop;
#endif
};

typedef struct basis_luau_worker basis_luau_worker;

static basis_luau_worker* g_workers;
static int g_worker_count;
static int g_sync_mode;

#if defined(_WIN32)
static DWORD WINAPI basis_luau_worker_main(LPVOID param)
{
    basis_luau_worker* worker = (basis_luau_worker*)param;
    for (;;) {
        WaitForSingleObject(worker->wake_event, INFINITE);
        if (worker->stop) {
            break;
        }
        ResetEvent(worker->wake_event);

        basis_luau_runtime* rt = worker->rt;
        if (rt && basis_luau_runtime_is_scheduler_running(rt)) {
            basis_luau_proxy_run_frame(rt);
        }
    }
    return 0;
}
#else
static void* basis_luau_worker_main(void* param)
{
    basis_luau_worker* worker = (basis_luau_worker*)param;
    while (!worker->stop) {
        usleep(1000);
        basis_luau_runtime* rt = worker->rt;
        if (rt && basis_luau_runtime_is_scheduler_running(rt)) {
            basis_luau_proxy_run_frame(rt);
        }
    }
    return NULL;
}
#endif

int basis_luau_scheduler_start_impl(basis_luau_runtime* rt, int worker_count)
{
    (void)rt;
    basis_luau_scheduler_shutdown_impl(rt);

    if (worker_count <= 0) {
        g_sync_mode = 1;
        g_workers = NULL;
        g_worker_count = 0;
        return 1;
    }

    g_sync_mode = 0;
    if (worker_count > 4) {
        worker_count = 4;
    }

    g_workers = (basis_luau_worker*)calloc((size_t)worker_count, sizeof(basis_luau_worker));
    if (!g_workers) {
        return 0;
    }
    g_worker_count = worker_count;

    for (int i = 0; i < worker_count; ++i) {
        g_workers[i].rt = rt;
#if defined(_WIN32)
        g_workers[i].stop = 0;
        g_workers[i].wake_event = CreateEventA(NULL, TRUE, FALSE, NULL);
        g_workers[i].thread = CreateThread(NULL, 0, basis_luau_worker_main, &g_workers[i], 0, NULL);
        if (!g_workers[i].thread) {
            return 0;
        }
#else
        g_workers[i].stop = 0;
        if (pthread_create(&g_workers[i].thread, NULL, basis_luau_worker_main, &g_workers[i]) != 0) {
            return 0;
        }
#endif
    }
    return 1;
}

void basis_luau_scheduler_shutdown_impl(basis_luau_runtime* rt)
{
    (void)rt;
    if (!g_workers) {
        return;
    }
    for (int i = 0; i < g_worker_count; ++i) {
#if defined(_WIN32)
        g_workers[i].stop = 1;
        SetEvent(g_workers[i].wake_event);
        if (g_workers[i].thread) {
            WaitForSingleObject(g_workers[i].thread, 5000);
            CloseHandle(g_workers[i].thread);
        }
        if (g_workers[i].wake_event) {
            CloseHandle(g_workers[i].wake_event);
        }
#else
        g_workers[i].stop = 1;
        pthread_join(g_workers[i].thread, NULL);
#endif
    }
    free(g_workers);
    g_workers = NULL;
    g_worker_count = 0;
    g_sync_mode = 0;
}

void basis_luau_scheduler_kick_impl(basis_luau_runtime* rt, float delta_time, float fixed_delta_time, int tick_fixed)
{
    if (!rt) {
        return;
    }

    basis_luau_runtime_set_frame_times(rt, delta_time, fixed_delta_time, tick_fixed);

    if (g_sync_mode || !g_workers) {
        basis_luau_proxy_run_frame(rt);
        return;
    }

    for (int i = 0; i < g_worker_count; ++i) {
        if (g_workers[i].rt == rt) {
#if defined(_WIN32)
            SetEvent(g_workers[i].wake_event);
#endif
            return;
        }
    }

    if (g_worker_count > 0) {
#if defined(_WIN32)
        SetEvent(g_workers[0].wake_event);
#endif
    }
}
