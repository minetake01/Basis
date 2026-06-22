$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $root "Resolve-VcVars64.ps1")
$outDir = Join-Path (Split-Path -Parent $root) "Native\Plugins\win-x64"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$include = $env:BASIS_LUAU_INCLUDE
if (-not $include) {
    $shortBuild = "C:\lb\luau\luau-dotnet\luau\VM\include"
    $localBuild = Join-Path $env:LOCALAPPDATA "basis-luau-build\luau-dotnet\luau\VM\include"
    $nestedBuild = Join-Path $root ".build\luau-dotnet\luau\VM\include"
    if (Test-Path $shortBuild) { $include = $shortBuild }
    elseif (Test-Path $localBuild) { $include = $localBuild }
    elseif (Test-Path $nestedBuild) { $include = $nestedBuild }
    else { $include = $shortBuild }
}
if (-not (Test-Path $include)) {
    Write-Error "Luau headers not found. Run Native~/build-libluau.ps1 first or set BASIS_LUAU_INCLUDE."
}

$importLib = $env:BASIS_LUAU_IMPORT_LIB
if (-not $importLib) {
    $importLib = Join-Path $outDir "libluau.import.lib"
}
if (-not (Test-Path $importLib)) {
    Write-Error "Import library not found at $importLib. Run Native~/build-libluau.ps1 first."
}

$source = Join-Path $root "basis_luau_limits.c"
$dll = Join-Path $outDir "basis_luau_limits.dll"
$dllBuilt = "$dll.built"

$includeFlag = "/I`"$include`""
$linkFlag = "/link `"$importLib`""
$objDir = Join-Path $env:TEMP "basis-luau-limits-obj"
Remove-Item $objDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $objDir | Out-Null

$objectFile = Join-Path $objDir "basis_luau_limits.obj"
$clArgs = "/nologo /LD /O2 /DBASIS_LUAU_LIMITS_EXPORT $includeFlag `"$source`" /Fo:`"$objectFile`" /Fe:`"$dllBuilt`" $linkFlag"
$exitCode = Invoke-MsvcCl -WorkingDirectory $objDir -Arguments $clArgs
if ($exitCode -ne 0) { exit $exitCode }

try {
    Copy-Item $dllBuilt $dll -Force
    Remove-Item $dllBuilt -Force -ErrorAction SilentlyContinue
}
catch {
    Write-Warning "Could not overwrite locked $dll. Output is $dllBuilt"
}

Write-Host "Built $(if (Test-Path $dll) { $dll } else { $dllBuilt })"
