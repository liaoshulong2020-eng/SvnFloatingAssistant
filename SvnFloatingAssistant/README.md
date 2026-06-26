# SvnFloatingAssistant

**轻量级 Windows 桌面 SVN 悬浮助手** — 实时跟随当前 Explorer 目录显示 SVN 工作副本状态，并提供 TortoiseSVN 快捷操作入口。

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4) ![Platform](https://img.shields.io/badge/platform-Windows-0078D4) ![WPF](https://img.shields.io/badge/UI-WPF-512BD4) ![License](https://img.shields.io/badge/license-MIT-green)

---

## 概述

SvnFloatingAssistant 是一个托盘中运行的 WPF 桌面工具。它会自动感知当前正在浏览的 Explorer 窗口路径，实时解析该目录的 SVN 状态，并以**悬浮面板**的形式将版本信息、本地修改、提交日志呈现在屏幕边缘。

适合日常需要频繁查看 SVN 状态但又不想每次都打开 TortoiseSVN 或命令行的人群。

## 功能特性

| 功能 | 说明 |
| --- | --- |
| **自动路径追踪** | 检测前台 Explorer 窗口路径，目录切换自动响应 |
| **防抖刷新** | 路径变化后防抖再执行 SVN 查询，避免频繁触发 |
| **三栏版本头** | 显示本地版本 / 远程最新版本 / 落后版本数 |
| **提交日志流** | 查看最近提交记录，无限滚动加载更多 |
| **状态指示灯** | 状态栏圆点 + 文字指示 Clean / Modified / Conflict |
| **TortoiseSVN 集成** | 一键打开更新、提交、日志对话框 |
| **系统托盘** | 托盘图标运行，支持显示/隐藏/退出 |
| **粘性吸附** | 拖拽后自动吸附到屏幕左右边缘 |
| **置顶模式** | 📌 按钮切换窗口置顶 |
| **TortoiseSVN 兼容** | 找不到 svn.exe 时自动降级为 SubWCRev.exe |

### 状态颜色体系

| 颜色 | 含义 |
| --- | --- |
| 灰色 | 非 SVN 目录或等待 Explorer 窗口 |
| 绿色 | 工作副本干净 / 有本地修改 |
| 黄色 | 存在冲突 |
| 蓝色 | 刷新中 |
| 红色 | SVN 工具未找到 |

## 快速开始

### 前置要求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 / Windows 11（依赖 Explorer COM 接口）
- （可选）[TortoiseSVN](https://tortoisesvn.net/) — 获得完整 SVN 状态检测 + 快捷操作

### 运行

`powershell
dotnet run --project .\SvnFloatingAssistant.csproj
`

### 构建

`powershell
# Debug
dotnet build .\SvnFloatingAssistant.csproj

# Release
dotnet build .\SvnFloatingAssistant.csproj -c Release

# 发布为单文件
dotnet publish .\SvnFloatingAssistant.csproj -c Release -o .\publish
`

## 配置

首次启动会在以下路径自动创建配置文件：

`
%APPDATA%\SvnFloatingAssistant\settings.json
`

`json
{
  "AutoRefresh": true,
  "ExplorerPollMilliseconds": 500,
  "DebounceMilliseconds": 400,
  "BubbleSize": 72,
  "Opacity": 0.95,
  "DarkMode": false,
  "SvnPath": null,
  "SubWCRevPath": null,
  "TortoiseSvnProcPath": null
}
`

### 配置项说明

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| ExplorerPollMilliseconds | 500 | Explorer 窗口轮询间隔（毫秒） |
| DebounceMilliseconds | 400 | 目录变化后的防抖等待时间 |
| Opacity | 0.95 | 窗口不透明度 0.2 ~ 1.0 |
| DarkMode | false | 暗色模式（预留） |
| SvnPath | null | svn.exe 绝对路径，不填则自动搜索 PATH |
| SubWCRevPath | null | SubWCRev.exe 绝对路径 |
| TortoiseSvnProcPath | null | TortoiseProc.exe 绝对路径 |

如果 svn.exe / SubWCRev.exe / TortoiseProc.exe 不在 PATH 中，可在 JSON 中填写绝对路径。

## 技术架构

`
MainWindow (WPF)
  TitleBar      — 项目名 / 分支 / 控制按钮
  RevisionBar   — 本地版本 / 远程版本 / 落后数
  LogList       — 提交日志 / 无限滚动
  ActionBar     — 更新 / 提交 / 日志按钮
  StatusBar     — 状态圆点 / 文字 / 刷新时间

ViewModels
  MainViewModel — 状态管理 / INotifyPropertyChanged

Services
  SvnService             — SVN 命令执行 + 解析
  SvnCache               — 60s/10s/60s 三级缓存
  ExplorerPathMonitor    — 前台 Explorer 路径探测
  TortoiseSvnLauncher    — TortoiseProc 快捷启动
  AppSettings            — JSON 配置读写

Models
  SvnInfo / SvnStatusSummary / SvnLogEntry
  SvnSnapshot / SvnHealth 枚举
`

### 技术栈

- **.NET 8** + **WPF** + **Windows Forms**（纯 Windows 平台）
- COM 互操作（Shell.Application + user32 P/Invoke）
- 零外部 NuGet 依赖

## 设计边界

本工具定位为**信息展示 + 快捷入口**，**不实现**以下功能：

- SVN Checkout（检出）
- Merge（合并）
- Resolve（冲突解决）
- Diff / Blame（差异/溯源）
- Branch / Tag 管理
- Repository Browser（仓库浏览器）

如需上述完整功能，请使用 [TortoiseSVN](https://tortoisesvn.net/) 或 SVN 命令行。

## 常见问题

**点击「更新/提交/日志」没反应？**

请确保已安装 TortoiseSVN，且 TortoiseProc.exe 在 PATH 中，或在 settings.json 中设置 TortoiseSvnProcPath。

**悬浮面板上的状态显示「未找到 svn.exe」？**

安装 TortoiseSVN 或 SlikSVN/VisualSVN。工具会自动搜索常见安装路径，也可以手动在 settings.json 中配置 SvnPath。

**Explorer 切换目录后工具没有更新？**

确保焦点在 Explorer（文件资源管理器）窗口上。工具只检测前台窗口为 explorer.exe 时的路径变化。

## 许可证

MIT
