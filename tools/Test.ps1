$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$checkRoot = Join-Path $env:TEMP ('CitrusChecks-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $projectRoot 'build.ps1') -OutputDirectory $checkRoot
$process = Start-Process -FilePath (Join-Path $checkRoot 'Citrus.exe') -WorkingDirectory $checkRoot -ArgumentList '--self-test', '.' -WindowStyle Hidden -Wait -PassThru
Get-Content (Join-Path $checkRoot 'results.txt')
Write-Host "Report and UI previews: $checkRoot"
if ($process.ExitCode -ne 0) { throw 'Citrus checks failed.' }
