<div align="center">

# PeekJet

**Your workspaces, one glance away.**

A lightweight, themeable live window preview dock for VS Code on Windows.

[![Windows build](https://github.com/haochen12/PeekJet/actions/workflows/build.yml/badge.svg)](https://github.com/haochen12/PeekJet/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/haochen12/PeekJet)](https://github.com/haochen12/PeekJet/releases/latest)
[![MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[Download](https://github.com/haochen12/PeekJet/releases/latest) · [简体中文](README.md) · [Changelog](CHANGELOG.md) · [Report an issue](https://github.com/haochen12/PeekJet/issues)

</div>

![Four themes rendered with synthetic workspaces](docs/themes/gallery.png)

> Images use the dock renderer and built-in example editors, never the user's code or desktop. This README describes main; see Releases for published versions.

## Features

- Native DWM live previews for VS Code and Insiders, with workspace-name labels.
- Four-edge docking, drag-to-snap, multiple monitors and per-monitor DPI.
- Midnight, Paper, Ocean, Ember and Windows system appearance.
- A 250 ms gradual hover activation and translucent mouse click-through while idle.
- Typing avoidance that respects your selected dock edge.
- A portable executable with no account, extension or network dependency.

## Download and install

Requires **Windows 10/11 and .NET Framework 4.8**.

1. Download `PeekJet-vVERSION-windows.zip` from [Releases](https://github.com/haochen12/PeekJet/releases/latest).
2. Unzip it and run `PeekJet.exe`.
3. Open at least two VS Code windows to display the dock.

The release also includes a source archive and `SHA256SUMS.txt`. To uninstall, quit and delete the app; preferences and logs can be removed separately.

## Usage

| Action | Result |
| --- | --- |
| Hover steadily for 250 ms | Gradually brighten and enable selection |
| Click a workspace preview | Switch to that VS Code window |
| Drag an activated preview or header | Move and snap to the nearest edge |
| Use the wheel or arrows while active | Change pages |
| Move away | Restore translucency and click-through |
| Right-click the tray or an activated dock | Open settings |
| `Ctrl + Alt + V` or a tray click | Toggle visibility or recall an avoided dock |

The dock has no settings button. Use the context menu for appearance, size, screen, dock edge and typing avoidance; use the tray menu to quit. The top-right minus button hides the dock manually.

Switching workspaces keeps the dock visible. It hides when only zero or one window remains and returns when multiple windows are available. An explicit manual hide is respected until you recall it.

### Personalization

Choose **外观主题** in the menu: Midnight (default), Paper, Ocean, Ember, or system appearance. Cards, text, controls and menus change together, and the selection is saved. VS Code's own colors remain unchanged. Available widths are 80, 100, 120 and 160 logical pixels; the default is 100.

Top and bottom docks arrange previews horizontally; side docks stack them vertically. Dragging across monitors pins the destination, with automatic following available in the menu. The default edge is top.

### Typing avoidance

Keyboard activity in an overlapping VS Code window makes the dock transparent and click-through. After 1.2 seconds without typing, it gradually recovers **in place**. Avoidance never moves a bottom dock to the top. The tray or hotkey can recall it immediately, and avoidance can be disabled in the menu.

## Build

```powershell
git clone https://github.com/haochen12/PeekJet.git
cd PeekJet
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1 -SkipBuild
```

Output: `dist/PeekJet.exe`. The build uses the .NET Framework compiler included with Windows and downloads no SDK or NuGet packages. The Visual Studio project requires the .NET Framework 4.8 targeting pack.

| Developer option | Purpose |
| --- | --- |
| `--self-test-themes` | Offscreen rendering, contrast and theme state checks; used in Windows CI |
| `--self-test` | Interactive integration checks on an unlocked desktop |
| `--self-test-avoidance` | Avoidance state checks without real keyboard or screenshot checks |
| `--render-themes` | Regenerate the synthetic theme gallery |

Full checks briefly open test windows and send F24 to a test window without entering text or commands. See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow and theme contributions.

## Privacy and limitations

No networking, telemetry or screen recording. Keyboard detection retains only event timing and the foreground window handle, without inspecting or storing key values or typed text. Input is forwarded unchanged.

Minimized windows may show a cached frame. Windows on other virtual desktops are excluded. Windows can restrict switching to elevated VS Code instances. Workspace names depend on standard window titles; custom formats can reduce accuracy. Settings are currently in Chinese.

Preferences: `HKCU\Software\PeekJet`, with legacy VSCodePreview migration support. Log: `%LOCALAPPDATA%\PeekJet\error.log`. The app does not enable startup automatically.

## Contributing

Bug reports, themes and pull requests are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md), and include Windows version, monitor scaling and reproduction steps in reports.

## License

[MIT](LICENSE) © PeekJet contributors. Independent community project; not an official Microsoft product.
