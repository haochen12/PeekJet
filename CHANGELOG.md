# Changelog

## 0.1.1 — 2026-11-12

- Simplify the dock to previews and project names, removing the wordmark, window count and footer hints.
- Reduce panel height, use dots for page indicators, and show navigation/hide controls only while activated.
- Use antialiased Windows 11 compositor corners, with a rounded-region fallback on older Windows.
- Align card and panel border strokes inside their bounds to reduce edge artifacts across themes.
- Refresh the theme gallery using synthetic workspaces and fractional corner coverage.
- Clean up the upper-left header decoration and unify rounded window/border geometry.
- Remove the settings button from the dock; settings remain in the tray and dock context menus.
- Reorganize Chinese and English READMEs with release downloads, usage, build, privacy and contribution sections.

## 0.1.0 — 2026-11-12

First public release of **PeekJet · 闪览**.

- Live DWM previews and click-to-switch for VS Code and Insiders.
- Midnight, Paper, Ocean and Ember themes, plus system appearance following.
- Four-edge docking, drag-to-snap, cross-monitor movement and per-monitor DPI support.
- Compact preview sizes and workspace-name labels.
- 250 ms gradual hover activation and mouse click-through while idle.
- Keep visible with multiple windows; hide automatically with zero or one.
- Transparent typing avoidance that preserves the selected dock edge.
- Portable Windows executable, MIT license, bilingual documentation and Windows CI.
- Read existing VSCodePreview preferences when migrating to PeekJet.
