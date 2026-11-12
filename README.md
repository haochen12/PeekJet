# PeekJet · 闪览

多个工作区，一眼切换。一个安静、轻巧、支持多主题的 Windows 实时窗口预览坞。

**v0.1.0 · Windows 10/11 · .NET Framework 4.8 · MIT**

[下载最新版](https://github.com/haochen12/PeekJet/releases/latest) · [English](README.en.md) · [更新日志](CHANGELOG.md) · [参与贡献](CONTRIBUTING.md)

![四套主题，使用示例工作区渲染](docs/themes/gallery.png)

## 快速开始

解压便携包，双击 `PeekJet.exe`。无需安装、VS Code 扩展、账号或管理员权限。

打开两个及以上 VS Code 窗口后，预览会自动出现。鼠标停稳 **250 毫秒**，面板平滑渐亮，此时点击项目即可切换。切换后持续显示；只剩一个窗口或全部关闭时自动隐藏，重新打开多个窗口后自动恢复。

右键托盘图标，或激活面板后点击齿轮图标，即可打开设置。`Ctrl + Alt + V` 或单击托盘图标可显示/隐藏，退出请使用托盘菜单。

## 外观主题

在 **外观主题** 中实时切换，选择会自动保存。

| 主题 | 风格 |
| --- | --- |
| 午夜深色 | 冷灰底色，柔和蓝色高亮，默认主题 |
| 云白浅色 | 轻盈白色卡片，深色文字与清晰边界 |
| 深海青蓝 | 深青色底色，薄荷色高亮 |
| 暖砂琥珀 | 暖灰棕底色，琥珀色高亮 |
| 跟随系统 | 随 Windows 应用颜色模式切换深浅主题 |

主题覆盖面板、卡片、项目名称、状态、按钮和菜单。预览里的 VS Code 内容仍保持各窗口原本的配色。上图由程序真实界面绘制器与内置示例编辑器生成，不包含用户项目或桌面内容。

## 交互

- **四边停靠**：顶部/底部横向排列，左右侧纵向排列，默认顶部居中，并避开任务栏。
- **拖拽移动**：激活后按住预览框或顶部空白处拖动，松开吸附到最近边缘并记住位置；轻微移动仍按普通点击处理。
- **双屏与 DPI**：自动跟随正在使用的 VS Code，或手动固定显示器。跨屏拖动会固定到目标屏，可从菜单恢复自动跟随。断开固定屏幕后会回退到可用屏幕。
- **紧凑大小**：每张预览可选 80 / 100 / 120 / 160 逻辑像素，默认 100，并随显示器缩放比例调整。
- **鼠标穿透**：闲置时半透明，点击与滚轮交给下方窗口。停稳 250 毫秒后才可选择；移开立即恢复，拖动其他内容经过时不会误激活。
- **输入避让**：在与预览重叠的 VS Code 中打字时，预览完全透明且保持穿透；停打约 1.2 秒后，用 180 毫秒在原位置渐显。底部停靠不会自动跳到顶部。菜单可关闭避让，托盘或快捷键可立即唤回。
- **项目名称**：优先显示项目/工作区名，悬停提示保留完整标题。支持 VS Code 与 VS Code Insiders。
- **分页与单实例**：窗口过多时通过箭头或滚轮翻页；重复启动只会唤回现有实例。手动隐藏会保留到下一次主动显示。

## 从源码构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

产物为 `dist/PeekJet.exe`。脚本使用 Windows 自带的 .NET Framework C# 编译器，不下载 SDK 或 NuGet 包。也可使用 Visual Studio 打开 `PeekJet.csproj`；需要 .NET Framework 4.8 目标包。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1 -SkipBuild
```

生成便携包 `PeekJet-v0.1.0-windows.zip` 和源码包 `PeekJet-v0.1.0-source.zip`。源码包采用文件白名单，不包含编译产物、日志和用户设置。

## 验证与主题展示

```powershell
Start-Process .\dist\PeekJet.exe '--self-test-themes' -Wait
Get-Content .\dist\theme-test.log
```

| 参数 | 用途 |
| --- | --- |
| `--self-test-themes` | 离屏主题渲染、文字对比度、系统主题和状态保持检查，可用于 Windows CI |
| `--self-test` | 完整交互检查，需要解锁桌面；覆盖 DWM、切换、拖拽、四边布局、双屏与输入避让 |
| `--self-test-avoidance` | 锁屏时检查避让状态与恢复，跳过真实键盘与截图检查 |
| `--render-themes` | 生成 `docs/themes/` 下的示例主题展示图，不捕获用户桌面 |

完整检查会短暂显示测试窗口，并将 F24 发送到测试窗口以验证输入转发；不会输入文本或终端命令。GitHub Actions 中只运行无需桌面交互的主题检查并生成发布附件，工作流上传仓库后生效。

## 隐私与已知限制

程序使用 Windows DWM 原生缩略图，无网络连接、统计遥测、录屏或定时截图。键盘活动检测只保留事件发生时间与前台窗口句柄，不读取或保存键值和输入文本，按键原样转发。

最小化窗口可能显示系统缓存画面；其他虚拟桌面上被系统隐藏的窗口不列入预览。Windows 可能限制切换到管理员权限的 VS Code。项目名依赖标准窗口标题，用户自定义标题格式可能导致识别不准确。

设置保存在 `HKEY_CURRENT_USER\Software\PeekJet`，旧版 VSCodePreview 设置会在首次运行时读取迁移。错误日志位于 `%LOCALAPPDATA%\PeekJet\error.log`。不自动设置开机启动。删除程序即可卸载，设置与日志可单独删除。

## 项目结构

| 文件 | 职责 |
| --- | --- |
| `Program.cs` | 窗口发现、预览布局、拖拽、交互与输入避让 |
| `Native.cs` | Win32 / DWM 接口 |
| `Themes.cs` | 主题色板、圆角绘制和菜单颜色 |
| `SelfTest.cs` | 集成检查与主题检查 |
| `ThemeGallery.cs` | 示例工作区和主题图生成 |
| `build.ps1` / `package.ps1` | 本地编译与打包 |

## 开源协议

采用 [MIT License](LICENSE)。这是独立社区工具，并非 Microsoft 官方产品。
