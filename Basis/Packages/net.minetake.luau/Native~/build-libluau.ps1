$ErrorActionPreference = "Stop"

$llvmLibClang = @(
    "$env:USERPROFILE\scoop\apps\llvm\current\bin",
    "${env:ProgramFiles}\LLVM\bin",
    "C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Data\PlaybackEngines\AndroidPlayer\NDK\toolchains\renderscript\prebuilt\windows-x86_64\bin"
)
foreach ($dir in $llvmLibClang) {
    if (Test-Path (Join-Path $dir "libclang.dll")) {
        $env:LIBCLANG_PATH = $dir
        break
    }
}
if (-not $env:LIBCLANG_PATH) {
    Write-Error "libclang.dll not found. Install LLVM (winget install LLVM.LLVM) or set LIBCLANG_PATH."
}
$dotnet9 = Join-Path $env:USERPROFILE ".dotnet\sdk9"
if (Test-Path (Join-Path $dotnet9 "dotnet.exe")) {
    $env:PATH = "$dotnet9;$env:PATH"
}

$cmakeCandidates = @()
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath 2>$null
    if ($installPath) {
        $cmakeCandidates += Join-Path $installPath "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin"
    }
}
$cmakeCandidates += @(
    "${env:ProgramFiles}\Microsoft Visual Studio\18\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin",
    "${env:ProgramFiles}\Microsoft Visual Studio\18\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin",
    "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin",
    "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin",
    "${env:ProgramFiles}\CMake\bin"
)
foreach ($cmakeDir in $cmakeCandidates) {
    if (Test-Path (Join-Path $cmakeDir "cmake.exe")) {
        $env:PATH = "$cmakeDir;$env:PATH"
        break
    }
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageRoot = Split-Path -Parent $root
$patchesDir = Join-Path $root "patches"
$pluginsDir = Join-Path $packageRoot "Native\Plugins\win-x64"
$runtimeDir = Join-Path $packageRoot "Runtime"
$workRoot = if ($env:BASIS_LUAU_BUILD_ROOT) { $env:BASIS_LUAU_BUILD_ROOT } else { "C:\lb\luau" }
$cargoTargetRoot = if ($env:BASIS_LUAU_CARGO_TARGET) { $env:BASIS_LUAU_CARGO_TARGET } else { "C:\lb\cargo-target" }
$env:CARGO_TARGET_DIR = $cargoTargetRoot
$repoDir = Join-Path $workRoot "luau-dotnet"
$pin = "820559978beca636c871c68a4000082e566dcb31"
$target = "x86_64-pc-windows-msvc"

New-Item -ItemType Directory -Force -Path $pluginsDir, $runtimeDir, $workRoot | Out-Null

function Ensure-Repo {
    if (-not (Test-Path (Join-Path $repoDir ".git"))) {
        Write-Host "Cloning luau-dotnet @ $pin ..."
        git clone --filter=blob:none --no-checkout https://github.com/nuskey8/luau-dotnet.git $repoDir
        Push-Location $repoDir
        git checkout $pin
        git submodule update --init --recursive
        Pop-Location
    }
}

function Apply-Patches {
    $ffiDir = Join-Path $repoDir "native\luau-ffi"
    Copy-Item (Join-Path $patchesDir "luau_ffi_extras.c") (Join-Path $ffiDir "src\luau_ffi_extras.c") -Force
    Copy-Item (Join-Path $patchesDir "luau_ffi_anchor.rs") (Join-Path $ffiDir "src\luau_ffi_anchor.rs") -Force

    $libRs = Join-Path $ffiDir "src\lib.rs"
    $libContent = Get-Content $libRs -Raw
    if ($libContent -notmatch "mod luau_ffi_anchor") {
        $libContent = $libContent -replace "mod luau_ffi;", "mod luau_ffi;`nmod luau_ffi_anchor;"
        Set-Content -Path $libRs -Value $libContent -NoNewline
    }

    $buildRs = Join-Path $ffiDir "build.rs"
    $buildContent = Get-Content $buildRs -Raw
    if ($buildContent -notmatch "luau_ffi_extras.c") {
        $inject = @"

    cc::Build::new()
        .file("src/luau_ffi_extras.c")
        .include("../../luau/VM/include")
        .compile("luau_ffi_extras");
"@
        $buildContent = $buildContent -replace "fn main\(\) \{", "fn main() {$inject"
        Set-Content -Path $buildRs -Value $buildContent -NoNewline
    }

    $luauState = Join-Path $repoDir "src\Luau\LuauState.cs"
    $stateContent = Get-Content $luauState -Raw
    $stateContent = $stateContent -replace "if \(from != null\) lua_close\(l\);\s*else lua_unref\(l, reference\);", "if (from != null) lua_unref(from.AsPointer(), reference);`n        else lua_close(l);"
    Set-Content -Path $luauState -Value $stateContent -NoNewline
}

function Find-ImportLibrary {
    param([string]$SearchRoot)
    $preferred = @(
        (Join-Path $SearchRoot "release\luau.dll.lib"),
        (Join-Path $SearchRoot "release\luau.lib"),
        (Join-Path $SearchRoot "release\libluau.lib")
    )
    foreach ($path in $preferred) {
        if (Test-Path $path) { return $path }
    }
    $libs = Get-ChildItem -Path $SearchRoot -Recurse -Filter "*.lib" |
        Where-Object { $_.Name -match "^(luau\.dll|luau|libluau)\.lib$" -and $_.Name -notlike "*extras*" } |
        Sort-Object LastWriteTime -Descending
    if ($libs.Count -eq 0) {
        throw "Could not find Cargo-generated import library under $SearchRoot"
    }
    return $libs[0].FullName
}

Ensure-Repo
Apply-Patches

# cmake crate 0.1.54 (luau-dotnet pin) does not recognize VS 18 yet; relabeling keeps the default VS generator layout (build/Release).
if ($env:VisualStudioVersion -match '^18\.') {
    $env:VisualStudioVersion = '17.0'
}

Write-Host "Building libluau ($target) ..."
Push-Location (Join-Path $repoDir "native\luau-ffi")
cargo build --release --target $target
$artifactDir = Join-Path $cargoTargetRoot "$target\release"
Pop-Location

$libDll = Join-Path $artifactDir "luau.dll"
if (-not (Test-Path $libDll)) {
    $libDll = Join-Path $artifactDir "libluau.dll"
}
if (-not (Test-Path $libDll)) {
    throw "Missing luau.dll / libluau.dll under $artifactDir"
}

$importLib = Find-ImportLibrary -SearchRoot (Join-Path $cargoTargetRoot $target)
Write-Host "Using import library: $importLib"

function Copy-LockedFile {
    param([string]$Source, [string]$Destination)
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try {
            Copy-Item $Source $Destination -Force
            return
        }
        catch {
            if ($attempt -eq 4) {
                $fallback = "$Destination.built"
                Copy-Item $Source $fallback -Force
                Write-Warning "Could not overwrite locked file $Destination. Wrote $fallback instead."
                return
            }
            Start-Sleep -Milliseconds 500
        }
    }
}

$pluginsLibLuau = Join-Path $pluginsDir "libluau.dll"
$pluginsLuau = Join-Path $pluginsDir "luau.dll"
Copy-LockedFile $libDll $pluginsLibLuau
Copy-LockedFile $libDll $pluginsLuau
$exportDll = $pluginsLibLuau
if (Test-Path "$pluginsLibLuau.built") {
    $exportDll = "$pluginsLibLuau.built"
}
$pluginsImportLib = Join-Path $pluginsDir "libluau.import.lib"
Copy-LockedFile $importLib $pluginsImportLib
if (-not (Test-Path $pluginsImportLib)) {
    $pluginsImportLib = $importLib
}

Write-Host "Building Luau.dll ..."
Push-Location (Join-Path $repoDir "src\Luau")
dotnet build -c Release -f netstandard2.1
$luauDll = Join-Path (Get-Location) "bin\Release\netstandard2.1\Luau.dll"
if (-not (Test-Path $luauDll)) {
  $luauDll = Get-ChildItem -Path (Join-Path (Get-Location) "bin") -Recurse -Filter "Luau.dll" | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $luauDll) {
    throw "Luau.dll build output not found"
}
Pop-Location

Copy-LockedFile $luauDll (Join-Path $runtimeDir "Luau.dll")

$luauInclude = Join-Path $repoDir "luau\VM\include"
$env:BASIS_LUAU_INCLUDE = $luauInclude
$env:BASIS_LUAU_IMPORT_LIB = $pluginsImportLib

Write-Host "Building basis_luau_limits.dll ..."
& (Join-Path $root "build.ps1")

Write-Host "Verifying ffi_luaL_error_msg export ..."
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ExportCheck {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string p);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr h, string n);
    public static void Check(string path) {
        var h = LoadLibrary(path);
        if (h == IntPtr.Zero) throw new Exception("LoadLibrary failed");
        if (GetProcAddress(h, "ffi_luaL_error_msg") == IntPtr.Zero) throw new Exception("ffi_luaL_error_msg missing");
    }
}
"@
$exportDll = if (Test-Path "$pluginsLibLuau.built") { "$pluginsLibLuau.built" } else { $pluginsLibLuau }
[ExportCheck]::Check($exportDll)

Write-Host "Done. libluau.dll, Luau.dll, basis_luau_limits.dll updated."
