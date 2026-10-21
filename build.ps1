param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'Try Citrus'),
    [string]$CSharpCompiler
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = $CSharpCompiler
if (!$compiler) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $products = & $vswhere -products '*' -requires Microsoft.Component.MSBuild -format json | ConvertFrom-Json
        $visualStudio = @($products | Where-Object { $_.productId -match '^Microsoft\.VisualStudio\.Product\.(Community|Professional|Enterprise)$' } |
            Sort-Object { [version]$_.installationVersion } -Descending | Select-Object -First 1)
        if ($visualStudio.Count) {
            $candidate = Join-Path $visualStudio[0].installationPath 'MSBuild\Current\Bin\Roslyn\csc.exe'
            if (Test-Path -LiteralPath $candidate) { $compiler = $candidate }
        }
    }
}
if (!$compiler) { $compiler = Join-Path $framework 'csc.exe' }
if (!(Test-Path -LiteralPath $compiler)) { throw 'The requested C# compiler does not exist.' }
$references = @('mscorlib', 'System', 'System.Core', 'System.Xml', 'System.Drawing', 'System.Windows.Forms', 'System.Runtime.Serialization') |
    ForEach-Object { '/reference:' + (Join-Path $framework ($_ + '.dll')) }
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $out | Out-Null
$iconGenerator = Join-Path $env:TEMP ('CitrusIcon-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo /noconfig /nostdlib+ /target:exe @references /out:$iconGenerator "$root\src\Brand.cs" "$root\tools\GenerateIcon.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Icon generator build failed.' }
    & $iconGenerator (Join-Path $root 'assets')
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
}
finally { if (Test-Path -LiteralPath $iconGenerator) { Remove-Item -LiteralPath $iconGenerator -Force } }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler /nologo /noconfig /nostdlib+ /target:winexe /optimize+ /platform:anycpu @references "/win32icon:$root\assets\citrus.ico" "/resource:$root\assets\citrus.ico,Citrus.Icon" "/out:$out\Citrus.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') -Destination $out -Force
$compilerInfo = (Get-Item -LiteralPath $compiler).VersionInfo
@{ Compiler = $compilerInfo.ProductName; CompilerVersion = $compilerInfo.FileVersion;
   CompilerSHA256 = (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash.ToLowerInvariant();
   SourceFiles = @($sources + @((Join-Path $root 'tools\GenerateIcon.cs'), (Join-Path $root 'build.ps1'),
       (Join-Path $root 'assets\citrus.ico'), (Join-Path $root 'engines\worker.py')) | ForEach-Object {
       @{ File = $_.Substring($root.Length + 1).Replace('\', '/'); SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
   }) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'build-info.json') -Encoding UTF8
$engineDirectory = Join-Path $out 'engine'
New-Item -ItemType Directory -Force -Path $engineDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'engines\worker.py') -Destination (Join-Path $engineDirectory 'worker.py') -Force
$pythonCommand = Get-Command python.exe -ErrorAction SilentlyContinue
@{ Python = $(if ($pythonCommand) { $pythonCommand.Source } else { '' }); Libraries = (Join-Path $root '.engines\python') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $engineDirectory 'runtime.json') -Encoding UTF8
Write-Host "Built $out\Citrus.exe"
