param(
    [string]$PythonVersion = '3.13.16',
    [string]$OfficeDirectory = "$env:ProgramFiles\LibreOffice",
    [string]$Compiler = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    [switch]$RefreshEngines
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $project '.engines\release'
$payload = Join-Path $project 'artifacts\payload'
$output = Join-Path $project 'artifacts\setup'
if (!(Test-Path -LiteralPath $Compiler)) { throw 'Install Inno Setup 6 (winget install JRSoftware.InnoSetup), or supply -Compiler.' }
if (!(Test-Path -LiteralPath (Join-Path $OfficeDirectory 'program\soffice.exe'))) { throw 'Supply -OfficeDirectory with a complete LibreOffice installation.' }
New-Item -ItemType Directory -Force $cache, $payload, $output | Out-Null
& (Join-Path $project 'build.ps1') -OutputDirectory $payload
$runtime = Join-Path $cache "python-$PythonVersion"
$ready = Join-Path $runtime 'complete.txt'
$requirements = Join-Path $project 'engines\requirements.txt'
$fingerprint = (Get-FileHash -LiteralPath $requirements -Algorithm SHA256).Hash
if ($RefreshEngines -or !(Test-Path -LiteralPath $ready) -or (Get-Content -LiteralPath $ready -Raw).Trim() -ne $fingerprint) {
    & python -c 'import sys,struct; assert sys.version_info[:2] == (3,13) and struct.calcsize("P") == 8, "Engine preparation requires Python 3.13 x64"'
    if ($LASTEXITCODE -ne 0) { throw 'Use Python 3.13 x64 to prepare the embedded engine packages.' }
    New-Item -ItemType Directory -Force $runtime | Out-Null
    $archive = Join-Path $cache "python-$PythonVersion-embed-amd64.zip"
    if (!(Test-Path -LiteralPath $archive)) {
        Invoke-WebRequest -UseBasicParsing "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip" -OutFile $archive
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $runtime -Force
    "python313.zip`n.`nLib\site-packages`nimport site" | Set-Content -LiteralPath (Join-Path $runtime 'python313._pth') -Encoding Ascii
    & python -m pip --isolated install --ignore-installed --upgrade --only-binary=:all: --target (Join-Path $runtime 'Lib\site-packages') -r $requirements
    if ($LASTEXITCODE -ne 0) { throw 'Could not prepare all Python engines.' }
    $fingerprint | Set-Content -LiteralPath $ready -Encoding Ascii
}
$pythonTarget = Join-Path $payload 'engine\python'
$officeTarget = Join-Path $payload 'engine\office'
# Robocopy mirrors only generated directories inside artifacts; it never touches user input files.
& robocopy $runtime $pythonTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Python runtime copy failed.' }
& robocopy $OfficeDirectory $officeTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'LibreOffice runtime copy failed.' }
$ffmpeg = @(Get-ChildItem -LiteralPath (Join-Path $pythonTarget 'Lib\site-packages\imageio_ffmpeg\binaries') -Filter '*.exe')
if ($ffmpeg.Count -ne 1) { throw 'Expected exactly one bundled FFmpeg executable.' }
$ffmpegRelative = 'python\Lib\site-packages\imageio_ffmpeg\binaries\' + $ffmpeg[0].Name
@{ Python = 'python\python.exe'; Libraries = 'python\Lib\site-packages'; Office = 'office\program\soffice.exe'; FFmpeg = $ffmpegRelative } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $payload 'engine\runtime.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $project 'LICENSE'), (Join-Path $project 'THIRD_PARTY_NOTICES.md') -Destination $payload -Force
$prerequisites = Join-Path $payload 'prerequisites'
New-Item -ItemType Directory -Force $prerequisites | Out-Null
$redist = Join-Path $prerequisites 'vc_redist.x64.exe'
if (!(Test-Path -LiteralPath $redist)) {
    Invoke-WebRequest -UseBasicParsing 'https://aka.ms/vc14/vc_redist.x64.exe' -OutFile $redist
}
$signature = Get-AuthenticodeSignature -LiteralPath $redist
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw 'The Microsoft runtime installer signature is invalid.'
}
# The engine suite is verified against VC++ 14.50. A newer prerequisite is included
# for fresh machines, without needlessly upgrading already compatible installations.
$runtimeMinor = 50
$previousOffice = $env:CITRUS_OFFICE
$previousLibraries = $env:CITRUS_LIBRARIES
$previousFFmpeg = $env:CITRUS_FFMPEG
$env:CITRUS_OFFICE = Join-Path $officeTarget 'program\soffice.exe'
$env:CITRUS_LIBRARIES = Join-Path $pythonTarget 'Lib\site-packages'
$env:CITRUS_FFMPEG = $ffmpeg[0].FullName
try {
    $capabilityText = & (Join-Path $pythonTarget 'python.exe') (Join-Path $payload 'engine\worker.py') --capabilities
    if ($LASTEXITCODE -ne 0) { throw 'Bundled engine discovery failed.' }
    $capabilities = $capabilityText | ConvertFrom-Json
    if ($capabilities.Count -lt 71) { throw "Incomplete bundled catalog: $($capabilities.Count) profiles; expected at least 71." }
    $capabilityText | Set-Content -LiteralPath (Join-Path $output 'capabilities.json') -Encoding UTF8
    & (Join-Path $pythonTarget 'python.exe') (Join-Path $project 'engines\test_worker.py')
    if ($LASTEXITCODE -ne 0) { throw 'Bundled conversion checks failed.' }
} finally {
    $env:CITRUS_OFFICE = $previousOffice
    $env:CITRUS_LIBRARIES = $previousLibraries
    $env:CITRUS_FFMPEG = $previousFFmpeg
}
& (Join-Path $pythonTarget 'python.exe') (Join-Path $project 'tools\Inventory-Engines.py') $payload |
    Set-Content -LiteralPath (Join-Path $output 'engine-inventory.json') -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw 'Engine inventory failed.' }
& $Compiler "/DPayload=$payload" "/DReleaseOutput=$output" "/DRuntimeMinor=$runtimeMinor" (Join-Path $project 'installer\Citrus.iss')
if ($LASTEXITCODE -ne 0) { throw 'Setup compiler failed.' }
$setup = Join-Path $output 'Citrus-Setup-0.3.0-x64.exe'
Get-FileHash -LiteralPath $setup -Algorithm SHA256 | Format-List
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding Ascii
Copy-Item -LiteralPath (Join-Path $project 'RELEASE.md') -Destination $output -Force
Write-Host "Release candidate: $setup"
Write-Host 'Public distribution still requires the source/license bundle and signing checklist in RELEASE.md.'
