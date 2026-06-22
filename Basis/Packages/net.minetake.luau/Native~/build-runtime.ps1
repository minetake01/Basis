$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $root "Resolve-VcVars64.ps1")
$outDir = Join-Path (Split-Path -Parent $root) "Native\Plugins\win-x64"

function Remove-PluginBuildArtifacts {
    param([string]$PluginsDir)
    foreach ($name in @(
            "basis_luau_runtime.dll.built",
            "basis_luau_runtime.dll.exp",
            "basis_luau_runtime.dll.lib",
            "basis_luau_limits.dll.exp",
            "basis_luau_limits.dll.lib",
            "basis_luau_limits.exp",
            "basis_luau_limits.lib",
            "libluau.dll.tmp",
            "ExportCheck.exe",
            "ExportCheck.exe.cs")) {
        Remove-Item (Join-Path $PluginsDir $name) -Force -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $PluginsDir "$name.meta") -Force -ErrorAction SilentlyContinue
    }
    Remove-Item (Join-Path $PluginsDir "export-check") -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $PluginsDir "export-check.meta") -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Remove-PluginBuildArtifacts $outDir
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
    "basis_luau_proxy.c",
    "basis_luau_runtime.c"
) | ForEach-Object { Join-Path $root $_ }

$dll = Join-Path $outDir "basis_luau_runtime.dll"
$dllBuilt = "$dll.built"
$includeFlag = "/I`"$include`" /I`"$root`""
$linkFlag = "/link `"$importLib`""
$sourceList = ($sources | ForEach-Object { "`"$_`"" }) -join " "
$objDir = Join-Path $env:TEMP "basis-luau-runtime-obj"
Remove-Item $objDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $objDir | Out-Null

$clArgs = "/nologo /LD /O2 /DBASIS_LUAU_LIMITS_EXPORT $includeFlag $sourceList /Fo:`"$objDir/`" /Fe:`"$dllBuilt`" $linkFlag"
$exitCode = Invoke-MsvcCl -WorkingDirectory $root -Arguments $clArgs
if ($exitCode -ne 0) { exit $exitCode }

if (-not (Test-Path $dllBuilt)) {
    Write-Error "Linker did not produce $dllBuilt"
}

try {
    Copy-Item $dllBuilt $dll -Force
    Remove-Item $dllBuilt -Force -ErrorAction SilentlyContinue
    Write-Host "Installed $dll"
}
catch {
    Write-Warning "Could not overwrite locked $dll. Output is $dllBuilt — close Unity and run Native~/deploy-built.ps1"
    & (Join-Path $root "test-exports.ps1")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    exit 1
}

Remove-PluginBuildArtifacts $outDir
Write-Host "Built $dll"

& (Join-Path $root "test-exports.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $root "build.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $root "deploy-built.ps1")
