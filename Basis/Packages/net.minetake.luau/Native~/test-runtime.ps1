param(
    [string]$PluginsDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "Native\Plugins\win-x64")
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

& (Join-Path $root "build-runtime.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$testDir = Join-Path $env:TEMP "basis-luau-runtime-test"
Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $testDir | Out-Null

function Stage([string]$name) {
    $built = Join-Path $PluginsDir "$name.built"
    $plain = Join-Path $PluginsDir $name
    if (Test-Path $built) { Copy-Item $built (Join-Path $testDir $name) -Force }
    elseif (Test-Path $plain) { Copy-Item $plain (Join-Path $testDir $name) -Force }
    else { throw "Missing $name" }
}

Stage "luau.dll"
Stage "basis_luau_runtime.dll"

$code = @"
using System;
using System.Runtime.InteropServices;
public static class RuntimeSmoke {
    [StructLayout(LayoutKind.Sequential)] struct Cfg { public ulong memory_cap_bytes; }
    enum InitErr { None=0, InvalidConfig=1, CtxAllocFailed=2, VmAllocFailed=3 }
    enum VerifyErr { Ok=0, Empty=1, Truncated=2, BadVersion=3, BadTypeVersion=4, LimitExceeded=5, Malformed=6 }
    [DllImport("basis_luau_runtime")] static extern IntPtr basis_luau_runtime_create(ref Cfg cfg, out InitErr err);
    [DllImport("basis_luau_runtime")] static extern void basis_luau_runtime_destroy(IntPtr rt);
    [DllImport("basis_luau_runtime")] static extern VerifyErr basis_luau_verify_bytecode(IntPtr data, UIntPtr size, UIntPtr max);
    public static int Main() {
        var cfg = new Cfg { memory_cap_bytes = 8 * 1024 * 1024 };
        InitErr err;
        var rt = basis_luau_runtime_create(ref cfg, out err);
        if (rt == IntPtr.Zero) { Console.WriteLine("create failed: " + err); return 1; }
        var ve = basis_luau_verify_bytecode(IntPtr.Zero, UIntPtr.Zero, new UIntPtr(1024));
        if (ve != VerifyErr.Empty) { Console.WriteLine("verify empty expected Empty got " + ve); return 2; }
        basis_luau_runtime_destroy(rt);
        Console.WriteLine("ok");
        return 0;
    }
}
"@

. (Join-Path $root "Compile-SmokeExe.ps1")
Compile-SmokeExe -SourceCode $code -OutputExe (Join-Path $testDir "RuntimeSmoke.exe")
Push-Location $testDir
& .\RuntimeSmoke.exe
$exit = $LASTEXITCODE
Pop-Location
if ($exit -ne 0) { exit $exit }
Write-Host "runtime smoke test passed"
