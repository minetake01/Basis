$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$pluginsDir = Join-Path (Split-Path -Parent $root) "Native\Plugins\win-x64"

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

foreach ($name in @("luau.dll", "basis_luau_limits.dll", "basis_luau_runtime.dll")) {
    $built = Join-Path $pluginsDir "$name.built"
    $dest = Join-Path $pluginsDir $name
    if (-not (Test-Path $built)) {
        Write-Warning "Skip $name (no .built artifact)"
        continue
    }
    Copy-Item $built $dest -Force
    Write-Host "Deployed $dest"
}

Remove-PluginBuildArtifacts $pluginsDir
Write-Host "Close Unity/Cursor if copy fails due to file locks."
