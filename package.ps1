param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$binary = Join-Path $PSScriptRoot 'dist\PeekJet.exe'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Build the executable first.' }
$version = ([Version](Get-Item -LiteralPath $binary).VersionInfo.FileVersion).ToString(3)
$release = Join-Path $PSScriptRoot 'dist'
$portableFiles = @($binary)
foreach ($name in @('README.md', 'README.en.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $release -Force
    $portableFiles += Join-Path $release $name
}
$portable = Join-Path $PSScriptRoot "PeekJet-v$version-windows.zip"
Compress-Archive -LiteralPath $portableFiles -DestinationPath $portable -Force
$sourceNames = @('Program.cs', 'Native.cs', 'Themes.cs', 'SelfTest.cs', 'ThemeGallery.cs', 'AssemblyInfo.cs',
    'PeekJet.csproj', 'app.manifest', 'app.ico', 'build.ps1', 'package.ps1', 'README.md', 'README.en.md',
    'CONTRIBUTING.md', 'CHANGELOG.md', 'LICENSE', '.gitignore', '.github', 'docs')
$sourcePaths = @($sourceNames | ForEach-Object { Join-Path $PSScriptRoot $_ })
# Explicit allowlist: no binaries, user settings, logs, or neighboring workspace files.
$source = Join-Path $PSScriptRoot "PeekJet-v$version-source.zip"
Compress-Archive -LiteralPath $sourcePaths -DestinationPath $source -Force
Write-Host "Packaged v$version"
Write-Host $portable
Write-Host $source
