<p align="center"><img src="docs/icon.png" width="96" alt="StickerBoard 图标"></p>

<h1 align="center">StickerBoard</h1>

<p align="center">
一款轻量的 Windows 桌面看板 —— 贴图片、记便签、随手涂鸦，<br>
像便利贴一样放在桌面壁纸上随时使用。
</p>

<p align="center">
<a href="README.md">English</a> · <a href="README.ko.md">한국어</a> · <a href="README.ja.md">日本語</a> · <b>简体中文</b> · <a href="README.zh-Hant.md">繁體中文</a>
</p>

<p align="center"><img src="docs/screenshot.png" alt="StickerBoard 截图"></p>

## 功能

- **图片** —— 用 `Ctrl+V` 粘贴（截图、网页图片、复制的文件），或直接拖入文件。拖动角上的手柄或滚动鼠标滚轮调整大小。
- **便签** —— 双击空白处新建。支持加粗、斜体、下划线、删除线、文字颜色、荧光标记、局部字号、字体、项目符号和**清单**。
- **纯文字便签** —— 没有便签纸，只有文字，柔和的描边让它在任何壁纸上都清晰可读。
- **画笔** —— 可以在任何内容上绘画和标记，支持撤销。
- **常驻桌面** —— 背景不透明度滑块（可完全透明）、置顶 / 普通 / 位于其他窗口后面、锁定位置和大小、最大化、窗口适应内容。
- **轻量** —— 约 0.6 MB 的可执行文件，空闲时 CPU 占用 0%，所有内容自动保存。
- **语言** —— English、한국어、日本語、简体中文、繁體中文（自动跟随 Windows 语言，也可在右键菜单中切换）。

## 下载

从 **[Releases](https://github.com/Eastar0102/StickerBoard/releases/latest)** 获取最新版本（Windows 10/11，x64）：

| 文件 | 大小 | 说明 |
| --- | --- | --- |
| `StickerBoard-<版本>-win-x64.exe` | 约 0.6 MB | 需要 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| `StickerBoard-<版本>-win-x64-standalone.exe` | 较大 | 无需安装任何组件，直接运行 |

无需安装，直接运行 exe 即可。程序没有代码签名，首次运行时 Windows SmartScreen 可能会提示警告（**更多信息 → 仍要运行**）。

## 使用方法

| 操作 | 方法 |
| --- | --- |
| 粘贴图片 / 文字 | `Ctrl+V`（文字会变成便签） |
| 新建便签 | 双击空白处，或点击 **+** 按钮 |
| 移动 | 直接拖动图片；便签拖动顶部的条 |
| 调整大小 | 角上的手柄（按住 `Shift` 不保持比例），或在图片上滚动鼠标滚轮 |
| 删除 / 撤销删除 | `Del` / `Ctrl+Z` |
| 便签格式 | `Ctrl+B` `Ctrl+I` `Ctrl+U`，删除线 `Ctrl+Shift+X`，项目符号 `Ctrl+Shift+L` |
| 清单 | `Ctrl+Enter` 添加复选框或勾选；点击 ☐ 也可勾选 |
| 便签字号 | `Ctrl+滚轮` |
| 画笔 | `P`（`H` 荧光笔，`E` 橡皮擦，`Ctrl+Z` 撤销，`Esc` 退出） |
| 最大化 | `F11` 或双击标题栏 |
| 设置 | 右键点击看板 |

## 数据

所有内容保存在 `%APPDATA%\StickerBoard`（`board.json`、`ink.isf`、`images\`）。
可以通过环境变量 `STICKERBOARD_DATA` 指定其他文件夹，便于测试或同时运行另一个独立的看板。

## 构建

需要在 Windows 上安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
dotnet run

# 小体积单文件 exe（需要 .NET 8 Desktop Runtime）
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

# 独立运行的单文件 exe
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish-standalone
```

图标由 `tools/make-icon.ps1` 生成。

## 翻译

界面文字位于 [`Strings/`](Strings) 文件夹中的 JSON 文件（以 `en.json` 为准，缺少的键会显示英文）。
如需添加新语言，请将 `en.json` 复制为 `Strings/<代码>.json` 并翻译，在 [`Localization.cs`](Localization.cs) 的 `L.Languages` 中加入语言代码，然后提交 Pull Request。也欢迎改进现有翻译。

## 许可证

[MIT](LICENSE)
