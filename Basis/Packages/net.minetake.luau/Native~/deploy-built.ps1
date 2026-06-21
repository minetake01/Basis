$ErrorActionPreference = "Stop"
$pluginsDir = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "Native\Plugins\win-x64"

foreach ($name in @("libluau.dll", "basis_luau_limits.dll", "basis_luau_runtime.dll")) {
    $built = Join-Path $pluginsDir "$name.built"
    $dest = Join-Path $pluginsDir $name
    if (-not (Test-Path $built)) {
        Write-Warning "Skip $name (no .built artifact)"
        continue
    }
    Copy-Item $built $dest -Force
    Write-Host "Deployed $dest"
}

$libBuilt = Join-Path $pluginsDir "libluau.dll.built"
if (Test-Path $libBuilt) {
    Copy-Item $libBuilt (Join-Path $pluginsDir "luau.dll") -Force
    Write-Host "Deployed luau.dll (alias of libluau)"
}

Write-Host "Close Unity/Cursor if copy fails due to file locks."
