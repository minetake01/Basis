param(
    [string]$PluginsDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "Native\Plugins\win-x64"),
    [int]$SnapshotSlots = 128,
    [int]$RingIterations = 100000,
    [int]$SnapshotIterations = 5000
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Stage([string]$name) {
    $built = Join-Path $PluginsDir "$name.built"
    $plain = Join-Path $PluginsDir $name
    if (Test-Path $built) { Copy-Item $built (Join-Path $testDir $name) -Force }
    elseif (Test-Path $plain) { Copy-Item $plain (Join-Path $testDir $name) -Force }
    else { throw "Missing $name" }
}

$testDir = Join-Path $env:TEMP "basis-luau-runtime-benchmark"
Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $testDir | Out-Null

Stage "luau.dll"
Stage "basis_luau_runtime.dll"

$code = @"
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

public static class RuntimeBenchmark {
    [StructLayout(LayoutKind.Sequential)] struct Cfg { public ulong memory_cap_bytes; }
    enum InitErr { None=0, InvalidConfig=1, CtxAllocFailed=2, VmAllocFailed=3 }
    enum RingResult { Ok=0, Full=1, Empty=2, Invalid=3 }

    [StructLayout(LayoutKind.Sequential, Pack=1, Size=32)]
    struct Command {
        public ushort Type;
        public ushort HostId;
        public uint HandleIndex;
        public uint HandleGeneration;
        public uint ProxyId;
        public uint ProxyGeneration;
        public unsafe fixed float Data[4];
    }

    [StructLayout(LayoutKind.Sequential, Pack=1)]
    struct SnapshotSlot {
        public uint HandleIndex;
        public uint HandleGeneration;
        public uint HostId;
        public unsafe fixed float Position[3];
        public unsafe fixed float Rotation[4];
        public unsafe fixed float TimeData[4];
        public uint Epoch;
    }

    [DllImport("basis_luau_runtime")] static extern IntPtr basis_luau_runtime_create(ref Cfg cfg, out InitErr err);
    [DllImport("basis_luau_runtime")] static extern void basis_luau_runtime_destroy(IntPtr rt);
    [DllImport("basis_luau_runtime")] static extern IntPtr basis_luau_runtime_root_state(IntPtr rt);
    [DllImport("basis_luau_runtime")] static extern RingResult basis_luau_ring_try_push_command(IntPtr rt, uint hostId, ref Command cmd);
    [DllImport("basis_luau_runtime")] static extern RingResult basis_luau_ring_try_pop_command(IntPtr rt, out Command cmd);
    [DllImport("basis_luau_runtime")] static extern int basis_luau_snapshot_publish_begin(IntPtr rt, out ulong epoch);
    [DllImport("basis_luau_runtime")] static extern void basis_luau_snapshot_publish_end(IntPtr rt, ulong epoch);
    [DllImport("basis_luau_runtime")] static extern int basis_luau_snapshot_write_slot(IntPtr rt, uint slotIndex, ref SnapshotSlot slot);
    [DllImport("basis_luau_runtime")] static extern ulong basis_luau_total_bytes(IntPtr L);
    [DllImport("basis_luau_runtime")] static extern ulong basis_luau_memory_cap(IntPtr L);

    static void Bench(string name, int warmup, int iterations, Action body) {
        for (int i = 0; i < warmup; i++) body();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) body();
        sw.Stop();
        double us = sw.Elapsed.TotalMilliseconds * 1000.0 / iterations;
        Console.WriteLine("[Native] {0}: {1:F3}ms / {2} iter ({3:F2}us/iter)", name, sw.Elapsed.TotalMilliseconds, iterations, us);
    }

    public static int Main() {
        var cfg = new Cfg { memory_cap_bytes = 256ul * 1024ul * 1024ul };
        InitErr err;
        var rt = basis_luau_runtime_create(ref cfg, out err);
        if (rt == IntPtr.Zero) { Console.WriteLine("create failed: " + err); return 1; }

        IntPtr root = basis_luau_runtime_root_state(rt);
        ulong luauBytes = basis_luau_total_bytes(root);
        ulong luauCap = basis_luau_memory_cap(root);
        Console.WriteLine("[Native] runtime baseline luau memory: {0} / cap {1} bytes", luauBytes, luauCap);

        var cmd = new Command { Type = 1, HostId = 1 };
        Bench("command ring push+pop", 1000, $RingIterations, () => {
            basis_luau_ring_try_push_command(rt, 1, ref cmd);
            Command popped;
            basis_luau_ring_try_pop_command(rt, out popped);
        });

        int slots = $SnapshotSlots;
        Bench("snapshot publish+" + slots + " slots", 50, $SnapshotIterations, () => {
            ulong epoch;
            if (basis_luau_snapshot_publish_begin(rt, out epoch) == 0) return;
            for (uint s = 1; s <= slots; s++) {
                var slot = new SnapshotSlot {
                    HandleIndex = s,
                    HandleGeneration = 1,
                    HostId = 1,
                };
                unsafe { slot.Position[0] = s; }
                basis_luau_snapshot_write_slot(rt, s, ref slot);
            }
            var timeSlot = new SnapshotSlot { HandleIndex = 0, HostId = 1 };
            unsafe { timeSlot.TimeData[0] = 0.016f; }
            basis_luau_snapshot_write_slot(rt, 0, ref timeSlot);
            basis_luau_snapshot_publish_end(rt, epoch);
        });

        luauBytes = basis_luau_total_bytes(root);
        Console.WriteLine("[Native] runtime luau memory after benchmarks: {0} bytes", luauBytes);

        basis_luau_runtime_destroy(rt);
        Console.WriteLine("[Native] benchmark complete");
        return 0;
    }
}
"@

. (Join-Path $root "Compile-SmokeExe.ps1")
Compile-SmokeExe -SourceCode $code -OutputExe (Join-Path $testDir "RuntimeBenchmark.exe")
Push-Location $testDir
& .\RuntimeBenchmark.exe
$exit = $LASTEXITCODE
Pop-Location
if ($exit -ne 0) { exit $exit }
Write-Host "native runtime benchmark passed"
