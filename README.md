# InputDisplay - 键盘鼠标输入监控工具

[English](README_EN.md) | 中文

<div align="center">

![Platform](https://img.shields.io/badge/Platform-Windows%2011-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/License-MIT-green)

**一款运行在 Windows 11 上的轻量级浮窗工具，实时显示键盘和鼠标的输入操作。**

</div>

---

## 功能特性

- **键盘输入监控** — 捕获并显示所有按键，包括普通键、功能键、修饰键组合（Ctrl+Shift+A 等）
- **鼠标操作监控** — 记录左键、右键、中键点击，滚轮滚动，鼠标移动坐标
- **实时当前输入** — 顶部卡片实时展示当前输入内容
- **输入统计面板** — 可展开查看按键频率 Top5、鼠标按钮统计、会话时长
- **透明浮窗** — 浅色毛玻璃风格浮窗，悬停在其他应用上不影响操作
- **系统托盘** — 关闭按钮仅隐藏窗口，后台持续监控，双击托盘图标恢复
- **位置记忆** — 自动保存窗口位置，重启后恢复上次位置

## 界面预览

```
┌──────────────────────────────────┐
│  ⌨  当前输入                     │  ← 当前输入卡片
│     Ctrl+V           14:32:15    │
├──────────────────────────────────┤
│  输入监控   ● 录制中    📊 │ ─ │ ✕ │  ← 标题栏
├──────────────────────────────────┤
│  ⌨ Ctrl+C                       │
│  ⌨ Ctrl+V                       │
│  🖱 左键按下 @ (420, 315)        │  ← 历史列表
│  ⌨ CapsLock                      │
│  ⌨ Esc                           │
└──────────────────────────────────┘
```

## 快速开始

### 运行环境

- **操作系统**：Windows 11（Windows 10 理论上也支持）
- **运行时**：.NET 8.0 Runtime（[下载地址](https://dotnet.microsoft.com/download/dotnet/8.0)）

> 如果你不想安装运行时，可以下载 **自包含版本（self-contained）**，无需安装 .NET 运行时。

### 下载与运行

1. 进入 [Releases](https://github.com/your-repo/InputDisplay/releases) 页面
2. 下载最新版本的 `InputDisplay.exe`
3. 双击运行即可

### 从源码编译

```bash
# 克隆项目
git clone https://github.com/your-repo/InputDisplay.git
cd InputDisplay

# 编译 Debug 版本
dotnet build

# 编译 Release 版本
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```

## 使用说明

### 基础操作

| 操作 | 说明 |
|------|------|
| **拖拽窗口** | 拖动标题栏移动浮窗位置 |
| **最小化** | 点击 `─` 按钮，窗口隐藏到托盘 |
| **关闭** | 点击 `✕` 按钮，仅隐藏窗口，后台继续监控 |
| **恢复窗口** | 双击托盘图标 |
| **展开统计** | 点击 `📊` 按钮查看输入统计 |
| **重置统计** | 右键托盘图标 → 重置统计 |
| **退出程序** | 右键托盘图标 → 退出 |
| **复制历史** | 双击历史条目，复制到剪贴板 |

### 统计面板说明

点击 `📊` 按钮展开统计面板，显示以下信息：

| 统计项 | 说明 |
|--------|------|
| 键盘按键 | 本会话键盘按键总次数 |
| 鼠标点击 | 本会话鼠标点击总次数 |
| 鼠标移动 | 本会话鼠标移动次数 |
| 滚轮滚动 | 本会话滚轮滚动次数 |
| 左/中/右键 | 各按钮点击次数 |
| 高频按键 Top5 | 频率最高的 5 个按键及次数 |
| 会话时长 | 当前监控会话持续时间 |

## 支持的键位

| 类别 | 支持键位 |
|------|----------|
| 字母 | A ~ Z |
| 数字 | 0 ~ 9 |
| 符号 | ; = , - . / ` [ \ ] ' |
| 功能键 | F1 ~ F12 |
| 控制键 | Enter, Esc, Space, Tab, Backspace, Delete, Insert |
| 方向键 | ↑ ↓ ← → |
| 编辑键 | Home, End, PageUp, PageDown |
| 修饰键 | Ctrl, Shift, Alt, Win, CapsLock, NumLock |
| 小键盘 | Num0 ~ Num9, Num*, Num/, Num+, Num-, Num. |

## 常见问题

### Q: 程序为什么需要管理员权限？

A: 全局键盘鼠标钩子（Low-Level Hook）需要在较高级别权限下运行，以确保能捕获所有进程的输入。如果遇到权限不足的提示，请尝试以管理员身份运行。

### Q: 程序会被杀毒软件拦截吗？

A: 因为程序使用了系统级钩子（`SetWindowsHookEx`），部分杀毒软件可能会误报。建议将程序添加到白名单。这是工具类软件的正常行为。

### Q: 如何让程序开机自启动？

A: 可以将程序快捷方式放入「启动」文件夹：
`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`

### Q: 统计面板数据会保存吗？

A: 当前版本统计面板数据仅保存在内存中，程序退出后重置。每次会话独立统计。

## 项目结构

```
InputDisplay/
├── WinApi.cs              # Windows API 底层封装
├── Models.cs               # 数据模型（输入事件、统计数据）
├── InputHookService.cs     # 核心钩子服务（键盘+鼠标捕获）
├── MainWindow.xaml         # 浮窗 UI（XAML）
├── MainWindow.xaml.cs      # 窗口逻辑代码
├── App.xaml / App.xaml.cs  # WPF 应用入口
└── InputDisplay.csproj     # 项目配置文件
```

## 技术栈

- **框架**：WPF（Windows Presentation Foundation）
- **语言**：C# 12 / .NET 8.0
- **底层调用**：Win32 API（SetWindowsHookEx, GetAsyncKeyState）
- **UI 特性**：半透明窗口、毛玻璃效果、数据绑定

## 许可证

本项目基于 MIT 许可证开源，你可以自由使用、修改和分发。

---

*如果这个项目对你有帮助，欢迎 Star ⭐*
