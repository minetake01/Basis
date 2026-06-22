function Resolve-VcVars64 {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
        if ($installPath) {
            $candidate = Join-Path $installPath "VC\Auxiliary\Build\vcvars64.bat"
            if (Test-Path $candidate) { return $candidate }
        }
    }

    $relPaths = @(
        "Microsoft Visual Studio\18\Enterprise\VC\Auxiliary\Build\vcvars64.bat",
        "Microsoft Visual Studio\18\Professional\VC\Auxiliary\Build\vcvars64.bat",
        "Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat",
        "Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat",
        "Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat",
        "Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat",
        "Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat",
        "Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
    )
    foreach ($rel in $relPaths) {
        foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
            if (-not $base) { continue }
            $candidate = Join-Path $base $rel
            if (Test-Path $candidate) { return $candidate }
        }
    }

    return $null
}

function Invoke-MsvcCl {
    param(
        [Parameter(Mandatory = $true)][string]$Arguments,
        [string]$WorkingDirectory
    )

    $vcvars = Resolve-VcVars64
    $cd = if ($WorkingDirectory) { "cd /d `"$WorkingDirectory`" && " } else { "" }
    if ($vcvars) {
        cmd /c "`"$vcvars`" && ${cd}cl $Arguments" 2>&1 | ForEach-Object { Write-Host $_ }
        return [int]$LASTEXITCODE
    }

    if (Get-Command cl -ErrorAction SilentlyContinue) {
        cmd /c "${cd}cl $Arguments" 2>&1 | ForEach-Object { Write-Host $_ }
        return [int]$LASTEXITCODE
    }

    Write-Error "MSVC cl.exe not found. Install Visual Studio Build Tools with the C++ workload or run from a Developer shell."
    return 1
}
