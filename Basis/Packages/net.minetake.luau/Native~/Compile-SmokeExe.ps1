function Compile-SmokeExe {
    param(
        [Parameter(Mandatory = $true)][string]$SourceCode,
        [Parameter(Mandatory = $true)][string]$OutputExe
    )

    $sourceFile = "$OutputExe.cs"
    Set-Content -Path $sourceFile -Value $SourceCode -Encoding UTF8

    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) {
        Write-Error "csc.exe not found at $csc"
    }

    & $csc /nologo /platform:x64 "/out:$OutputExe" $sourceFile
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
