param([switch]$WithOffice)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& python -m pip install --upgrade --target (Join-Path $root '.engines\python') -r (Join-Path $root 'engines\requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Python engine installation failed.' }
if ($WithOffice) {
    & winget install --id TheDocumentFoundation.LibreOffice --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) { throw 'LibreOffice installation failed or requires manual installation.' }
}
Write-Host 'Engines installed. Rebuild Citrus to refresh its runtime configuration.'
