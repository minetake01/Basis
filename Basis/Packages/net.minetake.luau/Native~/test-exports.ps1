$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $root "Resolve-VcVars64.ps1")
$pluginsDir = Join-Path (Split-Path -Parent $root) "Native\Plugins\win-x64"
$dll = Join-Path $pluginsDir "basis_luau_runtime.dll"
$built = "$dll.built"

$artifact = $dll
if ((Test-Path $built) -and ((!(Test-Path $dll)) -or ((Get-Item $built).LastWriteTime -gt (Get-Item $dll).LastWriteTime))) {
    $artifact = $built
    Write-Host "Checking newer artifact $built"
}

if (-not (Test-Path $artifact)) {
    throw "Missing $dll (and no $built artifact)"
}

$vcvars = Resolve-VcVars64
if (-not $vcvars) { throw "vcvars not found" }

$dump = cmd /c "`"$vcvars`" && dumpbin /exports `"$artifact`"" 2>&1 | Out-String
if ($dump -notmatch "number of functions") {
    throw "dumpbin failed for $artifact"
}

$countMatch = [regex]::Match($dump, "(\d+) number of functions")
$exportCount = [int]$countMatch.Groups[1].Value
Write-Host "export_count=$exportCount"
if ($exportCount -lt 35) {
    throw "Expected at least 35 exports, got $exportCount"
}

$required = @(
    "basis_luau_runtime_set_host_id",
    "basis_luau_runtime_release_root_ownership",
    "basis_luau_runtime_set_datetime_buffer",
    "basis_luau_proxy_unregister"
)
foreach ($symbol in $required) {
    if ($dump -notmatch [regex]::Escape($symbol)) {
        throw "Missing export $symbol"
    }
    Write-Host "$symbol`: ok"
}

Write-Host "Export check passed for $artifact"
