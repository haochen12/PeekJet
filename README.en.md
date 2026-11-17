<div align="center">

<img src="docs/branding/peekjet.png" alt="PeekJet" width="72" height="72">

# PeekJet

**Preview your VS Code workspaces. Click to switch.**

[![Release](https://img.shields.io/github/v/release/haochen12/PeekJet)](https://github.com/haochen12/PeekJet/releases/latest)
[![Windows build](https://github.com/haochen12/PeekJet/actions/workflows/build.yml/badge.svg)](https://github.com/haochen12/PeekJet/actions/workflows/build.yml)
[![MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**[Download for Windows](https://github.com/haochen12/PeekJet/releases/latest)** · [简体中文](README.md) · [Report an issue](https://github.com/haochen12/PeekJet/issues)

</div>

## Features

- **Global typing hide**: keyboard input in any foreground app hides the dock. It fades back in place after about 1.2 seconds without typing, keeping editors, terminals and other input surfaces clear. Enabled by default; optional in the menu.
- **Click-through and gradual activation**: idle translucency lets mouse actions reach the window underneath. Hover steadily to brighten and select; default 150ms, customizable from 50 to 2000ms.
- **Multiple monitors and four-edge docking**: follow the current VS Code window or pin a monitor, with dragging, cross-monitor movement and different display scales.
- **Live workspace previews**: supports VS Code / Insiders and project-name labels. The current editor is subdued; click another preview to switch or restore its window.
- **Project names mode**: switch to compact labels from the context menu, with four-edge docking and all themes.
- **Automatic visibility**: stays visible when switching between multiple windows and hides when zero or one remains.
- **Personalization**: four themes plus system appearance, adjustable preview sizes and saved preferences.

## Get started

Requires **Windows 10/11 and .NET Framework 4.8**. No VS Code extension needed.

1. Download `PeekJet-vVERSION-windows.zip`, unzip it and run `PeekJet.exe`.
2. Open at least two VS Code windows. The dock appears automatically.
3. Hover steadily for **150ms**, then click a preview when the dock brightens.

## Controls

| Action | Result |
| --- | --- |
| Drag the activated dock | Snap to any screen edge or move across monitors |
| Wheel / arrows while active | Change pages, up to 10 previews per page |
| Far-right button | Minimize all VS Code windows |
| Right-click the tray / activated dock | Choose theme, size, monitor and activation delay, or quit |
| `Ctrl + Alt + V` / tray click | Show, hide or recall the dock |

## Display modes

PeekJet provides two display modes. Switch between them from **显示模式** in the context menu.

### Live preview mode

Shows live VS Code thumbnails with project names. The current window is subdued; click another preview to switch or restore a window.

![Live preview mode in the Ember theme](docs/themes/ember.png)

### Project names mode

When thumbnails are unnecessary, use a compact project label bar: horizontal on the top or bottom edge and vertical on the left or right edge. The current project is dimmed, hover brightens a label, and clicking a name switches windows. This mode creates no DWM thumbnails; label width is adjusted separately and saved automatically.

![Project names mode in the Ember theme](docs/names/ember.png)

<details>
<summary>Theme preview</summary>

![Four themes using built-in example workspaces](docs/themes/gallery.png)

</details>

## More

[Usage and FAQ](docs/USAGE.en.md) · [Build and development](docs/DEVELOPMENT.en.md) · [Contributing](CONTRIBUTING.md) · [Changelog](CHANGELOG.md)

This README describes main. See the changelog for released features. Settings are currently in Chinese.

Runs locally without networking or telemetry. [MIT](LICENSE). Independent community project, not an official Microsoft product.
