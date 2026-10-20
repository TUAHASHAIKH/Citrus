param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'Try Citrus'))
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $out | Out-Null
$iconGenerator = Join-Path $env:TEMP ('CitrusIcon-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /target:exe /reference:System.Drawing.dll /out:$iconGenerator "$root\src\Brand.cs" "$root\tools\GenerateIcon.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Icon generator build failed.' }
    & $iconGenerator (Join-Path $root 'assets')
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
}
finally { if (Test-Path -LiteralPath $iconGenerator) { Remove-Item -LiteralPath $iconGenerator -Force } }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll "/win32icon:$root\assets\citrus.ico" "/resource:$root\assets\citrus.ico,Citrus.Icon" "/out:$out\Citrus.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "Built $out\Citrus.exe"
