param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'C# compiler not found. Please install .NET Framework 4.8.'
}
$output = $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /codepage:65001 `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    "/win32manifest:$PSScriptRoot\app.manifest" "/win32icon:$PSScriptRoot\app.ico" "/out:$output\PeekJet.exe" `
    $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "Built: $output\PeekJet.exe"
