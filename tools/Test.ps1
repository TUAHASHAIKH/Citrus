param([switch]$AllEngines)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$checkRoot = Join-Path $env:TEMP ('CitrusChecks-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $projectRoot 'build.ps1') -OutputDirectory $checkRoot
$process = Start-Process -FilePath (Join-Path $checkRoot 'Citrus.exe') -WorkingDirectory $checkRoot -ArgumentList '--self-test', '.' -WindowStyle Hidden -Wait -PassThru
Get-Content (Join-Path $checkRoot 'results.txt')
Write-Host "Report and UI previews: $checkRoot"
if ($process.ExitCode -ne 0) { throw 'Citrus checks failed.' }
if ($AllEngines) {
    $fallbackReports = @(Get-ChildItem -LiteralPath $checkRoot -Recurse -File -Filter optional-engines.txt)
    if ($fallbackReports.Count -gt 0) {
        $fallbackReports | Get-Content
        throw 'Full-engine checks require a working app engine bridge; native-only fallback is not a passing result.'
    }
    $previousLibraries = $env:CITRUS_LIBRARIES
    try {
        $env:CITRUS_LIBRARIES = Join-Path $projectRoot '.engines\python'
        & python (Join-Path $projectRoot 'engines\test_worker.py')
        if ($LASTEXITCODE -ne 0) { throw 'Conversion engine checks failed.' }
    }
    finally { $env:CITRUS_LIBRARIES = $previousLibraries }
}
