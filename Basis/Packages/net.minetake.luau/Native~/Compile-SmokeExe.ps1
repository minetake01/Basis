function Compile-SmokeExe {
    param(
        [Parameter(Mandatory = $true)][string]$SourceCode,
        [Parameter(Mandatory = $true)][string]$OutputExe
    )

    $outputDir = Split-Path -Parent $OutputExe
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

    $sourceFile = Join-Path ([System.IO.Path]::GetTempPath()) ("basis_luau_smoke_" + [Guid]::NewGuid().ToString("N") + ".cs")
    Set-Content -Path $sourceFile -Value $SourceCode -Encoding UTF8

    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) {
        Write-Error "csc.exe not found at $csc"
    }

    & $csc /nologo /platform:x64 "/out:$OutputExe" $sourceFile
    $exitCode = $LASTEXITCODE
    Remove-Item $sourceFile -Force -ErrorAction SilentlyContinue
    if ($exitCode -ne 0) { exit $exitCode }
}
