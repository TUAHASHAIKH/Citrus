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
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $out -Force
$engineDirectory = Join-Path $out 'engine'
New-Item -ItemType Directory -Force -Path $engineDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'engines\worker.py') -Destination (Join-Path $engineDirectory 'worker.py') -Force
$pythonCommand = Get-Command python.exe -ErrorAction SilentlyContinue
@{ Python = $(if ($pythonCommand) { $pythonCommand.Source } else { '' }); Libraries = (Join-Path $root '.engines\python') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $engineDirectory 'runtime.json') -Encoding UTF8
Write-Host "Built $out\Citrus.exe"
