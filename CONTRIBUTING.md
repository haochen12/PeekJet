# Contributing

Use Windows 10 or 11 with .NET Framework 4.8. No NuGet packages or web services are required.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Run the theme render checks on any Windows desktop, including CI:

```powershell
Start-Process .\dist\PeekJet.exe '--self-test-themes' -Wait
Get-Content .\dist\theme-test.log
```

Run `--self-test` on an **unlocked desktop** for live DWM previews, switching, real mouse drag, keyboard event forwarding, monitor layouts and typing avoidance. It briefly opens test windows and sends F24 to a test window, without entering text or shell commands. Use `--self-test-avoidance` while locked to check avoidance state without real keyboard or screenshot checks.

Generate the documentation gallery with `--render-themes`. It uses the actual dock renderer and synthetic sample editors. It never includes the user's editor content. Source scenes live in `ThemeGallery.cs`.

## Theme contributions

Add a `ThemePalette` to `ThemeCatalog.All` in `Themes.cs`. The menu and gallery discover it automatically. Update descriptions and include readable primary and secondary text colors. The theme test checks text contrast, rendered background colors, system theme resolution and appearance state preservation.

## Pull requests

Describe the visible behavior and include the relevant validation result. For a UI change, include a screenshot with example workspaces. Preserve click-through while idle, 250 ms activation, the user's dock edge, and typing avoidance. Avoid collecting editor contents or key values.

Report reproducible bugs with Windows version, display scaling, monitor count, dock edge and steps. Do not include terminal secrets or private source code in screenshots or logs.
