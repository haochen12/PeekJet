# Development

[Home](../README.en.md) · [简体中文](DEVELOPMENT.md) · [Contributing](../CONTRIBUTING.md)

## Build and package

Use Windows 10/11 with .NET Framework 4.8. The script uses the compiler included with Windows; no SDK or NuGet downloads are required.

```powershell
git clone https://github.com/haochen12/PeekJet.git
cd PeekJet
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1 -SkipBuild
```

Output: `dist/PeekJet.exe` and two release ZIPs in the project root. Portable archives include offline documentation. Source archives use an explicit allowlist to exclude build output, logs and user data.

The default build prefers a 32-bit process and supports 64-bit VS Code windows. For a 64-bit build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Platform x64
```

Visual Studio can open the root `PeekJet.csproj`; install the .NET Framework 4.8 targeting pack.

## Layout

| Path | Purpose |
| --- | --- |
| `src/PeekJet/` | Application entry point, interaction, Win32/DWM interop and themes |
| `tests/SelfTest.cs` | Rendering, window, layout and avoidance checks |
| `assets/` | Application icon and Windows manifest |
| `tools/ThemeGallery.cs` | Synthetic editor scenes for documentation screenshots |
| `tools/Generate-Icon.ps1` | Multi-size ICO, PNG and SVG generation |
| `docs/` | User/developer guides, theme images and branding |
| `.github/` | Windows CI and issue templates |
| `build.ps1` / `package.ps1` | Root build and packaging entry points |

Diagnostics and gallery commands remain part of the same executable for local verification. They do not run automatically during normal use.

## Checks

```powershell
$check = Start-Process .\dist\PeekJet.exe '--self-test-themes' -Wait -PassThru
Get-Content .\dist\theme-test.log
if ($check.ExitCode -ne 0) { throw 'Theme checks failed' }
```

| Option | Purpose |
| --- | --- |
| `--self-test-themes` | Offscreen rendering, contrast and system appearance; used in CI |
| `--self-test-controls` | Multi-monitor layouts, paging, minimize-all, hover timing and thumbnail reuse |
| `--self-test-avoidance` | Avoidance state checks without real keyboard or pixel checks |
| `--self-test` | Full interaction checks on an unlocked desktop |
| `--render-themes` | Regenerate synthetic images in `docs/themes/` |

Full checks open test windows and send F24 to a test window without entering text or commands. State checks on a locked desktop do not replace real click, keyboard and pixel verification.

## Themes and icons

Add themes to `ThemeCatalog.All` in `src/PeekJet/Themes.cs`. The menu and gallery discover them automatically. Check primary/secondary text contrast and use example workspaces for screenshots.

```powershell
Start-Process .\dist\PeekJet.exe '--render-themes' -Wait
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Generate-Icon.ps1
```

The icon script updates `assets/app.ico` and `docs/branding/` without external dependencies. Rebuild to embed the shared executable, tray and window icon.
