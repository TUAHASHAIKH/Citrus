param(
    [string]$PythonVersion = '3.13.16',
    [string]$OfficeDirectory = "$env:ProgramFiles\LibreOffice",
    [string]$Compiler = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    [switch]$RefreshEngines,
    [switch]$PrepareOnly
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
$releaseRequirements = Join-Path $cache 'requirements-release.txt'
# Media conversion uses the separately pinned FFmpeg build. Do not accidentally
# redistribute the unverifiable Gyan binary inside imageio-ffmpeg's wheel too.
Get-Content -LiteralPath $requirements | Where-Object { $_ -notmatch '^imageio-ffmpeg==' } |
    Set-Content -LiteralPath $releaseRequirements -Encoding Ascii
$fingerprint = (Get-FileHash -LiteralPath $releaseRequirements -Algorithm SHA256).Hash
if ($RefreshEngines -or !(Test-Path -LiteralPath $ready) -or (Get-Content -LiteralPath $ready -Raw).Trim() -ne $fingerprint) {
    & python -c 'import sys,struct; assert sys.version_info[:2] == (3,13) and struct.calcsize(chr(80)) == 8'
    if ($LASTEXITCODE -ne 0) { throw 'Use Python 3.13 x64 to prepare the embedded engine packages.' }
    New-Item -ItemType Directory -Force $runtime | Out-Null
    $sitePackages = [IO.Path]::GetFullPath((Join-Path $runtime 'Lib\site-packages'))
    $cacheRoot = [IO.Path]::GetFullPath($cache).TrimEnd('\') + '\'
    if (!$sitePackages.StartsWith($cacheRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package cleanup escaped the generated engine cache.' }
    if (Test-Path -LiteralPath $sitePackages) { Remove-Item -LiteralPath $sitePackages -Recurse -Force }
    $archive = Join-Path $cache "python-$PythonVersion-embed-amd64.zip"
    if (!(Test-Path -LiteralPath $archive)) {
        Invoke-WebRequest -UseBasicParsing "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip" -OutFile $archive
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $runtime -Force
    "python313.zip`n.`nLib\site-packages`nimport site" | Set-Content -LiteralPath (Join-Path $runtime 'python313._pth') -Encoding Ascii
    & python -m pip --isolated install --ignore-installed --upgrade --only-binary=:all: --target $sitePackages -r $releaseRequirements
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
# OpenCV is needed for PDF page analysis, never video conversion. Its upstream
# explicitly permits omitting this optional LGPL video plugin. All media routes
# use the standalone FFmpeg executable; retain OpenCV's other native notices.
Get-ChildItem -LiteralPath (Join-Path $pythonTarget 'Lib\site-packages\cv2') -Filter 'opencv_videoio_ffmpeg*.dll' |
    Remove-Item -Force
if (Test-Path -LiteralPath (Join-Path $pythonTarget 'Lib\site-packages\imageio_ffmpeg')) { throw 'The release still contains the old FFmpeg wheel.' }
$sourceArguments = @()
if (!$PrepareOnly) { $sourceArguments += '--with-sources' }
& python (Join-Path $project 'tools\Prepare-FFmpeg.py') @sourceArguments
if ($LASTEXITCODE -ne 0) { throw 'Could not stage the pinned FFmpeg build.' }
$ffmpegTarget = Join-Path $payload 'engine\ffmpeg'
& robocopy (Join-Path $project '.engines\compliance\ffmpeg') $ffmpegTarget /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Pinned FFmpeg runtime copy failed.' }
$ffmpegExe = Join-Path $ffmpegTarget 'bin\ffmpeg.exe'
if (!(Test-Path -LiteralPath $ffmpegExe)) { throw 'Pinned FFmpeg archive did not contain bin\ffmpeg.exe.' }
$ffmpegRelative = 'ffmpeg\bin\ffmpeg.exe'
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
$installerLicense = Join-Path (Split-Path $Compiler -Parent) 'license.txt'
if (!(Test-Path -LiteralPath $installerLicense)) { throw 'The full Inno Setup publisher license is missing.' }
$installerNotices = Join-Path $payload 'licenses\installer'
New-Item -ItemType Directory -Force $installerNotices | Out-Null
Copy-Item -LiteralPath $installerLicense -Destination (Join-Path $installerNotices 'Inno-Setup-license.txt') -Force
@'
Citrus 0.3.2 - corresponding sources
https://github.com/TUAHASHAIKH/Citrus/releases/tag/citrus-sources-0.3.2

The source release supplies Citrus, exact-version conversion-engine sources,
native dependencies, patches and build instructions, with SHA256 manifests.
Download BUILDING-SOURCES.txt for extraction and rebuild instructions.
Installing and converting files with Citrus does not require these downloads.

Full third-party notices are under licenses and engine distribution metadata.
Microsoft runtime binaries retain Microsoft's proprietary terms and signature;
they are not covered by Citrus's AGPL license.
'@ | Set-Content -LiteralPath (Join-Path $payload 'SOURCE_INFORMATION.txt') -Encoding UTF8
& python (Join-Path $project 'tools\Prepare-Runtime-License.py') --payload $payload
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the full offline Microsoft runtime license.' }
# The engine suite is verified against VC++ 14.50. A newer prerequisite is included
# for fresh machines, without needlessly upgrading already compatible installations.
$runtimeMinor = 50
$previousOffice = $env:CITRUS_OFFICE
$previousLibraries = $env:CITRUS_LIBRARIES
$previousFFmpeg = $env:CITRUS_FFMPEG
$env:CITRUS_OFFICE = Join-Path $officeTarget 'program\soffice.exe'
$env:CITRUS_LIBRARIES = Join-Path $pythonTarget 'Lib\site-packages'
$env:CITRUS_FFMPEG = $ffmpegExe
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
if ($PrepareOnly) {
    Write-Host "Prepared and tested offline payload: $payload"
    return
}
& python (Join-Path $project 'tools\Verify-Release-Sources.py') --payload $payload
if ($LASTEXITCODE -ne 0) { throw 'Matching source materials are incomplete. Use -PrepareOnly for local payload checks, then finish collecting sources.' }
& python (Join-Path $project 'tools\Collect-Release-Notices.py') --payload $payload
if ($LASTEXITCODE -ne 0) { throw 'Full upstream notice collection failed.' }
& $Compiler "/DPayload=$payload" "/DReleaseOutput=$output" "/DRuntimeMinor=$runtimeMinor" (Join-Path $project 'installer\Citrus.iss')
if ($LASTEXITCODE -ne 0) { throw 'Setup compiler failed.' }
$setup = Join-Path $output 'Citrus-Setup-0.3.2-x64.exe'
Get-FileHash -LiteralPath $setup -Algorithm SHA256 | Format-List
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding Ascii
Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination $output -Force
Write-Host "Release candidate: $setup"
Write-Host 'Publish matching source materials with the installer; clean Windows acceptance is checked separately. See README.md.'
