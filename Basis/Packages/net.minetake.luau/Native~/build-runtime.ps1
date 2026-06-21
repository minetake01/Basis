$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $root "Resolve-VcVars64.ps1")
$outDir = Join-Path (Split-Path -Parent $root) "Native\Plugins\win-x64"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$include = $env:BASIS_LUAU_INCLUDE
if (-not $include) {
    $candidates = @(
        "C:\lb\luau\luau-dotnet\luau\VM\include",
        (Join-Path $env:LOCALAPPDATA "basis-luau-build\luau-dotnet\luau\VM\include"),
        (Join-Path $root ".build\luau-dotnet\luau\VM\include")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $include = $c; break }
    }
}
if (-not (Test-Path $include)) {
    Write-Error "Luau headers not found. Run Native~/build-libluau.ps1 first."
}

$importLib = Join-Path $outDir "libluau.import.lib"
if (-not (Test-Path $importLib)) {
    Write-Error "Import library not found at $importLib."
}

$sources = @(
    "basis_luau_limits.c",
    "basis_luau_bytecode_verify.c",
    "basis_luau_buffer_pool.c",
    "basis_luau_rcu_snapshot.c",
    "basis_luau_scheduler.c",
    "basis_luau_native_bindings.c",
    "basis_luau_runtime.c"
) | ForEach-Object { Join-Path $root $_ }

$dll = Join-Path $outDir "basis_luau_runtime.dll"
$dllBuilt = "$dll.built"
$includeFlag = "/I`"$include`" /I`"$root`""
$linkFlag = "/link `"$importLib`""
$sourceList = ($sources | ForEach-Object { "`"$_`"" }) -join " "

$clArgs = "/nologo /LD /O2 /DBASIS_LUAU_LIMITS_EXPORT $includeFlag $sourceList /Fe:`"$dllBuilt`" $linkFlag"
$exitCode = Invoke-MsvcCl -Arguments $clArgs
if ($exitCode -ne 0) { exit $exitCode }

try {
    Copy-Item $dllBuilt $dll -Force
    Remove-Item $dllBuilt -Force -ErrorAction SilentlyContinue
}
catch {
    Write-Warning "Could not overwrite locked $dll. Output is $dllBuilt"
}

Write-Host "Built $(if (Test-Path $dll) { $dll } else { $dllBuilt })"

& (Join-Path $root "deploy-built.ps1")
& (Join-Path $root "build.ps1")
