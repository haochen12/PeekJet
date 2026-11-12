param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [ValidateSet('anycpu32bitpreferred', 'anycpu', 'x86', 'x64')]
    [string]$Platform = 'anycpu32bitpreferred'
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'C# compiler not found. Please install .NET Framework 4.8.'
}
$output = $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = @(foreach ($folder in @('src', 'tests', 'tools')) {
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $folder) -Filter '*.cs' -Recurse | ForEach-Object FullName
})
& $compiler /nologo /target:winexe "/platform:$Platform" /optimize+ /utf8output /codepage:65001 `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll `
    "/win32manifest:$PSScriptRoot\assets\app.manifest" "/win32icon:$PSScriptRoot\assets\app.ico" "/resource:$PSScriptRoot\assets\app.ico,PeekJet.AppIcon" "/out:$output\PeekJet.exe" `
    $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "Built: $output\PeekJet.exe"
