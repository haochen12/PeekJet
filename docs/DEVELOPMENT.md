# 开发指南

[返回首页](../README.md) · [English](DEVELOPMENT.en.md) · [贡献约定](../CONTRIBUTING.md)

## 构建与打包

需要 Windows 10 / 11 和 .NET Framework 4.8。脚本使用 Windows 自带编译器，不下载 SDK 或 NuGet 包。

```powershell
git clone https://github.com/haochen12/PeekJet.git
cd PeekJet
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1 -SkipBuild
```

程序输出到 `dist/PeekJet.exe`，两个发布 ZIP 输出到项目根目录。便携包包含离线说明与文档；源码包使用目录白名单，排除构建产物、日志和用户数据。

默认优先以 32 位进程运行，仍可预览 64 位 VS Code。需要 64 位版本时：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Platform x64
```

也可用 Visual Studio 打开根目录的 `PeekJet.csproj`，需安装 .NET Framework 4.8 目标包。

## 项目结构

| 路径 | 职责 |
| --- | --- |
| `src/PeekJet/` | 应用入口、预览交互、Win32/DWM 接口与主题 |
| `tests/SelfTest.cs` | 主题、窗口交互、布局和输入避让检查 |
| `assets/` | 程序图标与 Windows manifest |
| `tools/ThemeGallery.cs` | 使用合成工作区生成主题展示图 |
| `tools/Generate-Icon.ps1` | 重新生成多尺寸 ICO、PNG 和 SVG |
| `docs/` | 用户/开发指南、主题图和品牌素材 |
| `.github/` | Windows CI 与问题模板 |
| `build.ps1` / `package.ps1` | 根目录构建、打包入口 |

诊断与展示图命令仍编译进同一个程序，方便本地检查；开发工具不会在正常运行时自动执行。

## 验证

```powershell
$check = Start-Process .\dist\PeekJet.exe '--self-test-themes' -Wait -PassThru
Get-Content .\dist\theme-test.log
if ($check.ExitCode -ne 0) { throw 'Theme checks failed' }
```

| 参数 | 用途 |
| --- | --- |
| `--self-test-themes` | 离屏主题渲染、对比度与系统模式；用于 CI |
| `--self-test-controls` | 双屏四边布局、分页、最小化、悬停时间和缩略图复用 |
| `--self-test-avoidance` | 避让状态检查，不依赖真实按键与屏幕像素 |
| `--self-test` | 完整交互检查，需要解锁桌面 |
| `--render-themes` | 更新 `docs/themes/` 中的合成主题展示图 |

完整交互检查会打开测试窗口，并向测试窗口发送 F24，不输入文本或终端命令。锁屏时不能用状态检查替代真实点击、键盘和像素验证。

## 主题与图标

在 `src/PeekJet/Themes.cs` 的 `ThemeCatalog.All` 中添加主题；菜单与展示图会自动发现。检查主次文字对比度，并用示例工作区更新展示图。

```powershell
Start-Process .\dist\PeekJet.exe '--render-themes' -Wait
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Generate-Icon.ps1
```

图标脚本更新 `assets/app.ico` 和 `docs/branding/`，无需外部依赖。重新构建后，程序、托盘和窗口共用嵌入图标。
