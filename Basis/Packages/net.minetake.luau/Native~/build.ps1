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

$source = Join-Path $root "basis_luau_limits.c"
$dll = Join-Path $outDir "basis_luau_limits.dll"

cmd /c "`"$vcvars`" && cl /nologo /LD /O2 /DBASIS_LUAU_LIMITS_EXPORT `"$source`" /Fe:`"$dll`""
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Built $dll"
