# SVN Floating Assistant

轻量级 Windows 桌面 SVN 悬浮助手，目标是跟随当前资源管理器目录显示 SVN 状态，并提供 TortoiseSVN 快捷入口。

## 当前能力

- 置顶悬浮球，按 SVN 状态显示颜色：
  - 灰色：非 SVN 目录或等待 Explorer
  - 绿色：工作正常
  - 黄色：存在修改或 SVN 响应慢
  - 红色：存在冲突
  - 蓝色：刷新中
- 监听当前前台 Explorer 窗口路径。
- 路径变化后防抖刷新。
- 后台执行 `svn info`、`svn status`、`svn log -l 5 --xml`。
- 如果没有安装 `svn.exe`，会自动降级为 TortoiseSVN 的 `SubWCRev.exe` 兼容模式，显示基础 Revision 和是否有本地变化。
- `info/status/log` 分别做 60 秒、10 秒、60 秒缓存。
- 所有 SVN 命令有超时控制，避免阻塞 UI。
- 右键菜单支持刷新、打开目录、日志、提交、更新、设置、退出。

## 运行

```powershell
dotnet run --project .\SvnFloatingAssistant.csproj
```

## 配置

首次启动会创建：

```text
%APPDATA%\SvnFloatingAssistant\settings.json
```

可配置项：

```json
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
```

如果 `svn.exe`、`SubWCRev.exe` 或 TortoiseSVN 的 `TortoiseProc.exe` 不在 PATH 中，可以在 JSON 中填写绝对路径。

## 设计边界

本工具只做信息展示和快捷入口，不实现 Checkout、Merge、Resolve、Diff、Branch、Repository Browser 等完整 SVN 客户端功能。
