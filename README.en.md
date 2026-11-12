# PeekJet

A quiet, themeable live window dock for Visual Studio Code on Windows.

**v0.1.0 · Windows 10/11 · .NET Framework 4.8 · MIT**

[Download](https://github.com/haochen12/PeekJet/releases/latest) · [简体中文](README.md)

![Four appearance themes, rendered with synthetic workspaces](docs/themes/gallery.png)

## Start

Unzip the portable package and run `PeekJet.exe`. No installer, VS Code extension, NuGet dependency, account, or administrator access is required.

Open at least two VS Code windows. Hover for **250 ms**: the dock gradually brightens, then becomes clickable. Click a workspace to switch. The dock stays visible after switching and automatically hides when only zero or one VS Code window remains.

## Appearance and controls

- Right-click the tray icon or use the settings icon → **外观主题** to select Midnight, Paper, Ocean, Ember, or Windows system appearance. Themes apply to cards, text, controls, and menus and are remembered.
- Dock at any of four screen edges; top is the default. Side docks stack previews vertically.
- After activating the dock, drag a preview or the header to move it. Release to snap to the closest edge. A cross-monitor drag pins the destination; automatic following remains available in the menu.
- Choose 80, 100, 120, or 160 logical pixels per preview. The default is 100, with per-monitor DPI scaling.
- While idle, the dock is translucent and mouse clicks pass through. Moving away restores this state.
- `Ctrl + Alt + V` or a tray click toggles visibility. Use the tray menu to quit.
- With typing avoidance enabled, keyboard activity in an overlapping VS Code window makes the dock transparent. It recovers in place after 1.2 seconds without typing. The dock never changes your selected edge for avoidance.

Theme images use rendered example editors, not screenshots of the user's workspaces. Changing the dock theme does not change the theme inside VS Code.

## Build and check

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1 -SkipBuild
```

The build script uses the .NET Framework compiler included with Windows. A Visual Studio project targeting .NET Framework 4.8 is also included.

`--self-test-themes` checks offscreen rendering and palette contrast. `--self-test` runs interactive integration checks on an unlocked desktop; `--self-test-avoidance` skips real keyboard and screenshot checks. `--render-themes` rebuilds the example gallery. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Privacy and limitations

The app uses Windows DWM thumbnails. It has no networking, analytics, or screen recording. The keyboard activity monitor retains only event timing and the foreground window handle; it does not inspect or store key values or typed text, and input is forwarded unchanged.

Minimized windows may show the last cached frame. Windows on other virtual desktops are excluded. Windows can restrict switching to elevated VS Code instances. Workspace labels are parsed from standard window titles; custom formats can reduce accuracy. The current interface is in Chinese.

Settings: `HKCU\Software\PeekJet`. Existing VSCodePreview settings are read as a migration fallback. Error log: `%LOCALAPPDATA%\PeekJet\error.log`. Delete the executable to uninstall; settings and logs can be removed separately.

## License

[MIT](LICENSE). Independent community tool; not an official Microsoft product.
