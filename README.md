# SvnFloatingAssistant

> 轻量级 Windows 桌面 SVN 悬浮助手 · 实时显示文件管理器目录的 SVN 状态面板，集成 TortoiseSVN 快捷操作

![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Windows](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows)

---

## 概览

**SvnFloatingAssistant** 是一款运行在 Windows 托盘区域的桌面工具，它会在屏幕右侧边缘悬浮显示当前文件管理器目录的 **SVN 工作副本** 状态——无需打开命令行或 TortoiseSVN 即可一目了然。

当你在 Windows 资源管理器（Explorer）或 **Directory Opus** 中切换文件夹时，悬浮面板自动刷新，展示：

- 当前目录的 **SVN 信息**
- **本地版本号** 与 **远端版本号**
- 落后/超前提交数
- 最近 **提交日志**（带作者高亮）
- **变更文件列表**
- 一键打开 TortoiseSVN 的 **更新 / 提交 / 日志**

---

## 功能特性

- **实时跟踪** — 切换到文件管理器的不同目录，面板自动跟踪并刷新 SVN 状态
- **双文件管理器兼容** — 原生支持 Windows Explorer 和 **Directory Opus**
- **TortoiseSVN 集成** — 悬浮面板上直接点击按钮打开更新、提交、日志对话框
- **置顶模式** — 点击图钉可将窗口置顶在其他窗口之上
- **折叠模式** — 双击标题栏或点击折叠按钮，可将面板压缩到最小，不遮挡屏幕
- **右边缘吸附** — 拖拽后自动吸附到屏幕边缘
- **托盘图标** — 最小化到系统托盘，随时唤出
- **本地提交高亮** — 日志列表中自动高亮您自己的提交记录
- **完整提交消息** — 日志条目显示完整提交说明，不截断

---

## 系统要求

- **操作系统**: Windows 10 / Windows 11（64 位）
- **运行环境**: .NET 8.0 Desktop Runtime
- **SVN 客户端**: 需要安装 TortoiseSVN （提供 svn.exe）或任意 SVN 命令行客户端
- **文件管理器**: Windows Explorer 或 Directory Opus（推荐）

---

## 快速开始

### 下载

从 Releases 页面下载最新版本的 SvnFloatingAssistant.exe（单文件发布）。

### 运行

直接双击运行即可，无需安装。程序启动后会在系统托盘显示 SVN 图标，悬浮面板自动出现在屏幕右侧。

提示：建议将程序添加到开机启动项，方便日常使用。

### 从源码构建

```
# 克隆仓库
git clone https://github.com/liaoshulong2020-eng/SvnFloatingAssistant.git
cd SvnFloatingAssistant

# 发布单文件
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

# 输出位于 bin/Release/net8.0-windows/win-x64/publish/
```

---

## 使用指南

### 基础操作

| 操作 | 说明 |
|------|------|
| 图钉 | 点击切换窗口置顶 |
| 折叠按钮 | 将面板折叠为窄条，仅保留标题栏 |
| 标题栏拖拽 | 按住标题栏拖动窗口位置，释放后自动吸附到最近边缘 |
| 目录切换 | 在文件管理器中切换目录，面板自动跟踪该目录的 SVN 状态 |
| 刷新 | 鼠标中键点击刷新时间标签，或通过托盘菜单强制刷新 |

### TortoiseSVN 快捷操作

面板底部的操作栏提供四个快捷按钮（从左到右）：

| 按钮 | 操作 | 说明 |
|------|------|------|
| 更新 | SVN Update | 打开 TortoiseSVN 更新对话框 |
| 提交 | SVN Commit | 打开 TortoiseSVN 提交对话框 |
| 日志 | SVN Log | 打开 TortoiseSVN 日志对话框 |
| 更多 | 右键菜单 | 展开更多操作（diff、blame、revert 等） |

### 托盘菜单

右键点击系统托盘的 SVN 图标：

- 显示 / 隐藏 — 切换悬浮面板可见性
- 强制刷新 — 立即重新查询 SVN 信息
- 退出 — 关闭程序

---

## Directory Opus 用户

本工具对 Directory Opus 有深度优化支持：

1. 程序自动检测 DOpus 窗口
2. 通过 dopusrt.exe /info ...,pathtab 获取当前激活标签页的路径
3. 切换目录时面板自动跟踪

---

## 技术架构

```
SvnFloatingAssistant/
├── Services/
│   ├── ExplorerPathMonitor.cs    # 文件管理器窗口检测与路径获取
│   ├── SvnCache.cs               # SVN 查询结果缓存
│   ├── SvnService.cs             # SVN 命令行调用封装
│   ├── TortoiseSvnLauncher.cs    # TortoiseSVN GUI 调用
│   └── AppSettings.cs            # 应用设置持久化
├── Models/
│   └── SvnModels.cs              # SVN 数据模型
├── ViewModels/
│   └── MainViewModel.cs          # 主视图模型
├── Converters/
│   └── VisibilityConverters.cs   # WPF 值转换器
├── MainWindow.xaml[.cs]          # 主窗口
└── App.xaml[.cs]                 # 应用入口
```

### 核心实现

- **路径检测**：使用 Win32 API 定位文件管理器窗口，通过 Shell COM 或 dopusrt 获取当前目录
- **SVN 查询**：调用 svn info、svn log、svn status 等命令，解析输出为结构化数据
- **缓存**：SvnCache 对相同的路径结果缓存 5 秒，避免频繁调用
- **UI**：纯 WPF 实现，无第三方 UI 框架依赖

---

## 常见问题

**Q: 为什么面板显示"非 SVN 工作副本"？**
A: 当前目录不在 SVN 版本控制下，或者 .svn 目录损坏。

**Q: 刷新间隔能调整吗？**
A: 目前刷新间隔固定为 800ms，在 AppSettings.cs 中可配置 ExplorerPollMilliseconds。

**Q: 支持多显示器吗？**
A: 当前版本悬浮窗口固定在主显示器右侧，不支持自动跟随多显示器。

---

## License

本项目基于 MIT License 开源。

---

## 作者

- GitHub: @liaoshulong2020-eng

---

> 持续提升硬件工程师的 SVN 使用体验
