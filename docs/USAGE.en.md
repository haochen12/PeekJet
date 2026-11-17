# Usage and FAQ

[Home](../README.en.md) · [简体中文](USAGE.md)

## Settings

Right-click the tray or the activated dock. Changes apply immediately and persist. Menu labels are currently in Chinese.

| Setting | Options |
| --- | --- |
| 外观主题 | Midnight, Paper, Ocean, Ember or Windows system appearance |
| 显示模式 | Live previews (default) or project names mode; saved automatically |
| 预览大小 | 80 / 100 / 120 / 160 logical pixels; default 100 |
| 悬停激活时间 | Default 150ms; presets or custom 50–2000ms |
| 停靠边缘 | Top by default; four edges and drag-to-snap |
| 显示位置 | Follow the current VS Code window or pin a monitor |
| 输入时自动隐藏 | Hides on keyboard input in any foreground app; enabled by default |

Dragging across monitors pins the destination; automatic following can be restored in the menu. Each page has up to 10 previews, fewer when screen space is limited. The current editor preview is subdued. Clicking another preview switches windows and restores minimized editors.

Choose **显示模式 → 项目名称模式** for horizontal labels at the top/bottom or a vertical list at either side. The current project is dimmed; gradual activation, switching, dragging, typing avoidance and paging remain available. Long names are truncated; hover for the full window title. **标签宽度** adjusts label size independently of preview size. This mode creates no DWM thumbnails.

![Project names mode in Ember](names/ember.png)

## Visibility

- The dock stays visible when switching between multiple windows; it hides with zero or one.
- `Ctrl + Alt + V` or a tray click shows, hides or recalls the dock.
- Keyboard input in any foreground app hides the dock. It fades back in place after about 1.2 seconds without typing.
- The far-right button minimizes all tracked VS Code windows across pages; the dock remains available to restore them.

## FAQ

**No previews?** Open at least two VS Code / Insiders windows, try the shortcut, or refresh windows from the tray menu.

**A minimized preview stopped updating?** Windows may retain a cached frame. Restore the editor to resume live updates.

**Wrong project name?** Names depend on standard VS Code window titles. Custom formats can affect parsing.

**Missing windows or switching fails?** Windows hidden on other virtual desktops are excluded. Windows may restrict switching to elevated VS Code instances.

**Upgrade or uninstall?** Quit the old version before replacing the app. Preferences are retained. To uninstall, quit and delete the app directory.

## Privacy and local data

No networking, telemetry or screen recording. Windows DWM provides live previews. Typing avoidance retains only event timing and the foreground window handle; it does not inspect or store key values or typed text.

- Preferences: `HKCU\Software\PeekJet`, with legacy VSCodePreview migration support.
- Log: `%LOCALAPPDATA%\PeekJet\error.log`.
- No automatic startup registration. Preferences and logs can be removed manually after quitting.

Release assets: `windows.zip` is the portable app, `source.zip` contains source, and `SHA256SUMS.txt` lists checksums. These docs describe main; see the [changelog](../CHANGELOG.md) for released features.
