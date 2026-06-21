param(
    [string]$PluginsDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "Native\Plugins\win-x64")
)

$ErrorActionPreference = "Stop"
$testDir = Join-Path $env:TEMP "basis-luau-limits-test"
New-Item -ItemType Directory -Force -Path $testDir | Out-Null

function Stage([string]$name) {
    $built = Join-Path $PluginsDir "$name.built"
    $plain = Join-Path $PluginsDir $name
    if (Test-Path $built) { Copy-Item $built (Join-Path $testDir $name) -Force }
    elseif (Test-Path $plain) { Copy-Item $plain (Join-Path $testDir $name) -Force }
    else { throw "Missing $name" }
}

Stage "libluau.dll"
Copy-Item (Join-Path $testDir "libluau.dll") (Join-Path $testDir "luau.dll") -Force
Stage "basis_luau_limits.dll"

$code = @"
using System;
using System.Runtime.InteropServices;
public static class LimitsSmoke {
    [StructLayout(LayoutKind.Sequential)] struct Cfg { public ulong memory_cap_bytes; }
    enum InitErr { None=0, InvalidConfig=1, CtxAllocFailed=2, VmAllocFailed=3 }
    [DllImport("basis_luau_limits")] static extern IntPtr basis_luau_newstate_with_limits(ref Cfg cfg, out InitErr err);
    [DllImport("basis_luau_limits")] static extern void basis_luau_begin_execution(IntPtr L, long budgetNs);
    [DllImport("basis_luau_limits")] static extern void basis_luau_end_execution(IntPtr L);
    public static int Main() {
        var cfg = new Cfg { memory_cap_bytes = 8 * 1024 * 1024 };
        InitErr err;
        var L = basis_luau_newstate_with_limits(ref cfg, out err);
        if (L == IntPtr.Zero) { Console.WriteLine("newstate failed: " + err); return 1; }
        basis_luau_begin_execution(L, 50000000);
        basis_luau_end_execution(L);
        Console.WriteLine("ok");
        return 0;
    }
}
"@

. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "Compile-SmokeExe.ps1")
Compile-SmokeExe -SourceCode $code -OutputExe (Join-Path $testDir "LimitsSmoke.exe")
Push-Location $testDir
& .\LimitsSmoke.exe
$exit = $LASTEXITCODE
Pop-Location
if ($exit -ne 0) { exit $exit }
Write-Host "limits smoke test passed"
