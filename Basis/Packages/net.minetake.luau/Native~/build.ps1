$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path (Split-Path -Parent $root) "Native\Plugins\win-x64"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$vcvars = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) {
    $vcvars = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
}
if (-not (Test-Path $vcvars)) {
    $vcvars = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
}
if (-not (Test-Path $vcvars)) {
    Write-Error "MSVC vcvars64.bat not found. Install Visual Studio Build Tools with C++ workload."
}

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

cmd /c "`"$vcvars`" && cl /nologo /LD /O2 /DBASIS_LUAU_LIMITS_EXPORT $includeFlag `"$source`" /Fe:`"$dllBuilt`" $linkFlag"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

try {
    Copy-Item $dllBuilt $dll -Force
    Remove-Item $dllBuilt -Force -ErrorAction SilentlyContinue
}
catch {
    Write-Warning "Could not overwrite locked $dll. Output is $dllBuilt"
}

Write-Host "Built $(if (Test-Path $dll) { $dll } else { $dllBuilt })"
