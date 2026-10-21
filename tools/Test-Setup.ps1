param([string]$Setup = '')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
if (!$Setup) { $Setup = Join-Path $project 'artifacts\setup\Citrus-Setup-0.3.2-x64.exe' }
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8BA05164-740A-4B45-A8B2-93D589747B51}_is1'
if (Test-Path -LiteralPath $key) { throw 'Citrus is already installed. Use a clean Windows account/VM for the isolated installer test.' }
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupBefore = (Get-ItemProperty -LiteralPath $startupKey -Name CitrusJpegConverter -ErrorAction SilentlyContinue).CitrusJpegConverter
$root = Join-Path $project ('artifacts\install-check-' + [Guid]::NewGuid().ToString('N'))
$installed = Join-Path $root 'installed'
$checks = Join-Path $root 'checks'
New-Item -ItemType Directory -Force $checks | Out-Null
$arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOLAUNCH=1 /TASKS="" /GROUP="Citrus Installer Check" /DIR="' + $installed + '" /LOG="' + (Join-Path $root 'install.log') + '"'
$process = Start-Process -FilePath $Setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Install failed: $($process.ExitCode). See $root" }
try {
    foreach ($relative in @('LICENSE', 'THIRD_PARTY_NOTICES.md', 'SOURCE_INFORMATION.txt', 'INSTALLER_LICENSE.txt',
        'licenses\index.json', 'licenses\microsoft\Microsoft-VC14-license.docx', 'licenses\installer\Inno-Setup-license.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $installed $relative))) { throw "Missing installed license/source material: $relative" }
    }
    $noticeIndex = Get-Content -LiteralPath (Join-Path $installed 'licenses\index.json') -Raw | ConvertFrom-Json
    if (!$noticeIndex.source_metadata_credential_scan_passed -or $noticeIndex.notices.Count -lt 100) { throw 'The installed native notice inventory is incomplete.' }
    $config = Get-Content -LiteralPath (Join-Path $installed 'engine\runtime.json') -Raw | ConvertFrom-Json
    foreach ($path in @($config.Python, $config.Libraries, $config.Office, $config.FFmpeg)) {
        if ([IO.Path]::IsPathRooted($path)) { throw 'Installer contains a machine-specific runtime path.' }
        if (!(Test-Path -LiteralPath (Join-Path $installed "engine\$path"))) { throw "Missing bundled dependency: $path" }
    }
    $oldPath = $env:PATH
    try {
        $env:PATH = "$env:WINDIR\System32;$env:WINDIR"
        $process = Start-Process -FilePath (Join-Path $installed 'Citrus.exe') -WorkingDirectory $checks -ArgumentList '--self-test', '.' -WindowStyle Hidden -Wait -PassThru
        Get-Content -LiteralPath (Join-Path $checks 'results.txt')
        if ($process.ExitCode -ne 0) { throw 'Installed-app regressions failed.' }
        if (Get-ChildItem -LiteralPath $checks -Recurse -File -Filter optional-engines.txt) { throw 'Installed app fell back to native-only conversion.' }
    } finally { $env:PATH = $oldPath }
    # Install the same setup over itself to verify the upgrade path.
    $process = Start-Process -FilePath $Setup -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Reinstall/upgrade failed.' }
} finally {
    $uninstaller = Join-Path $installed 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw 'Uninstall failed.' }
    }
}
if (Test-Path -LiteralPath (Join-Path $installed 'Citrus.exe')) { throw 'Uninstall left the application executable behind.' }
if (Test-Path -LiteralPath $key) { throw 'Uninstall left the application registration behind.' }
$startupAfter = (Get-ItemProperty -LiteralPath $startupKey -Name CitrusJpegConverter -ErrorAction SilentlyContinue).CitrusJpegConverter
if ($startupBefore -ne $startupAfter) { throw 'The isolated installation changed an existing startup preference.' }
if (!(Test-Path -LiteralPath (Join-Path $checks 'photo.png')) -or !(Test-Path -LiteralPath (Join-Path $checks 'photo.jpg'))) {
    throw 'Uninstall did not preserve conversion inputs/outputs outside the installation.'
}
"PASS: install, bundled runtime, installed-app regressions, reinstall, uninstall. Logs: $root" | Tee-Object -FilePath (Join-Path $root 'installer-results.txt')
