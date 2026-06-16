param(
    [string]$UnityPluginApiDir = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$NativeDir = $PSScriptRoot
$SpoutDir = Join-Path $NativeDir "third_party\Spout2"

if (-not (Test-Path (Join-Path $SpoutDir "SPOUTSDK\SpoutDirectX\SpoutDX\SpoutDX.cpp"))) {
    Write-Host "Cloning Spout2 into third_party..."
    git clone --depth 1 https://github.com/leadedge/Spout2.git $SpoutDir
}

if (-not $UnityPluginApiDir) {
    $hub = "${env:ProgramFiles}\Unity\Hub\Editor"
    if (Test-Path $hub) {
        $editor = Get-ChildItem $hub -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($editor) {
            $UnityPluginApiDir = Join-Path $editor.FullName "Editor\Data\PluginAPI"
        }
    }
}

$cmake = "cmake"
$vsCmake = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
if (-not (Get-Command cmake -ErrorAction SilentlyContinue) -and (Test-Path $vsCmake)) {
    $cmake = $vsCmake
}

if (-not (Test-Path $UnityPluginApiDir)) {
    throw "Set -UnityPluginApiDir to your Unity Editor/Data/PluginAPI folder."
}

$buildDir = Join-Path $NativeDir "build-win-x64"
& $cmake -S $NativeDir -B $buildDir -A x64 "-DUNITY_PLUGIN_API_DIR=$UnityPluginApiDir"
& $cmake --build $buildDir --config $Configuration

$outDll = Join-Path $NativeDir "..\Plugins\Windows\x86_64\basis_mediastream_native.dll"
Write-Host "Built: $outDll"
