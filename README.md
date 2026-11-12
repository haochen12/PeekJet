<div align="center">

<img src="docs/branding/peekjet.png" alt="PeekJet" width="72" height="72">

# PeekJet · 闪览

**预览多个 VS Code 项目，点击即可切换。**

[![Release](https://img.shields.io/github/v/release/haochen12/PeekJet)](https://github.com/haochen12/PeekJet/releases/latest)
[![Windows build](https://github.com/haochen12/PeekJet/actions/workflows/build.yml/badge.svg)](https://github.com/haochen12/PeekJet/actions/workflows/build.yml)
[![MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**[下载 Windows 版](https://github.com/haochen12/PeekJet/releases/latest)** · [English](README.en.md) · [反馈问题](https://github.com/haochen12/PeekJet/issues)

</div>

## 开始使用

需要 **Windows 10 / 11、.NET Framework 4.8**，无需安装 VS Code 扩展。

1. 下载 `PeekJet-v版本号-windows.zip`，解压后运行 `PeekJet.exe`。
2. 打开至少两个 VS Code 窗口，预览面板会自动出现。
3. 鼠标停稳 **150ms**，面板渐亮后，点击项目预览即可切换。

## 常用操作

| 操作 | 用途 |
| --- | --- |
| 激活后拖动面板 | 移动到屏幕四边，支持跨屏 |
| 激活后滚轮 / 点击箭头 | 翻页，每页最多 10 个预览 |
| 点击最右侧按钮 | 最小化全部 VS Code 窗口 |
| 右键托盘 / 激活后的面板 | 设置主题、大小、显示器和激活时间，或退出 |
| `Ctrl + Alt + V` / 单击托盘 | 显示、隐藏或唤回面板 |

闲置时半透明、鼠标穿透；打字时自动淡出避让；只剩一个窗口时自动隐藏。支持 VS Code / Insiders、四套主题及跟随系统，设置自动保存。

<details>
<summary>查看主题效果</summary>

![四套主题，使用内置示例工作区](docs/themes/gallery.png)

</details>

## 更多

[使用指南与常见问题](docs/USAGE.md) · [开发与构建](docs/DEVELOPMENT.md) · [参与贡献](CONTRIBUTING.md) · [更新日志](CHANGELOG.md)

README 对应 main 分支；已发布版本的功能以更新日志为准。

本地运行，无联网或遥测。[MIT 开源](LICENSE)，独立社区项目，非 Microsoft 官方产品。
