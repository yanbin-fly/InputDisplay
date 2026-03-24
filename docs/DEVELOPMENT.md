# InputDisplay 技术开发文档

> 本文档面向具备编程基础的开发者，详细记录 InputDisplay 项目的技术架构、核心原理、模块设计与实现细节。

---

## 目录

1. [项目概述](#1-项目概述)
2. [技术选型](#2-技术选型)
3. [系统架构](#3-系统架构)
4. [核心模块详解](#4-核心模块详解)
5. [Windows API 详解](#5-windows-api-详解)
6. [数据流分析](#6-数据流分析)
7. [线程模型](#7-线程模型)
8. [关键设计决策](#8-关键设计决策)
9. [编译与发布](#9-编译与发布)
10. [扩展方向](#10-扩展方向)

---

## 1. 项目概述

### 1.1 项目背景

InputDisplay 是一款 Windows 桌面工具，用于实时显示用户的键盘和鼠标输入。它以浮窗形式呈现，覆盖在其他应用窗口之上，帮助用户了解自己的操作历史、按键频率等。

典型应用场景：
- 教学演示：向观众展示操作步骤
- 自我审视：了解自己的键盘使用习惯
- 辅助工具：记录游戏或应用的快捷键使用情况

### 1.2 功能范围

| 功能 | 状态 | 说明 |
|------|------|------|
| 全局键盘捕获 | ✅ | WH_KEYBOARD_LL 钩子 |
| 全局鼠标捕获 | ✅ | WH_MOUSE_LL 钩子 |
| 实时当前输入展示 | ✅ | INPC 绑定驱动 |
| 输入历史记录 | ✅ | ObservableCollection |
| 输入统计面板 | ✅ | Top5 + 会话数据 |
| 透明浮窗 UI | ✅ | AllowsTransparency |
| 系统托盘集成 | ✅ | NotifyIcon |
| 窗口位置记忆 | ✅ | LocalAppData 持久化 |

---

## 2. 技术选型

### 2.1 为什么选择 WPF

| 候选方案 | 优点 | 缺点 |
|----------|------|------|
| **WPF** | 声明式 UI、数据绑定、样式系统成熟 | 需要 .NET 运行时 |
| WinUI 3 | 现代化 UI、官方推荐 | 配置复杂、需要 VS2022 |
| Electron | 跨平台、生态丰富 | 体积大、启动慢、内存占用高 |
| Qt | 跨平台、C++ 生态 | 体积大、配置复杂 |

**选择 WPF 的理由**：
1. **配置简单** — 一个 `.csproj` 文件即可完成配置
2. **数据绑定** — 内置的 MVVM 风格数据绑定，减少 UI 更新代码
3. **半透明窗口** — `AllowsTransparency="True"` + 无边框窗口实现毛玻璃效果
4. **Win32 互操作** — 方便调用 `SetWindowsHookEx` 等底层 API
5. **轻量** — 生成的 exe 文件小，启动速度快

### 2.2 .NET 8.0 特性使用

- **源生成器（ImplicitUsings）** — 自动插入常用命名空间
- **文件级命名空间** — `namespace InputDisplay;` 直接写在文件顶部
- **记录类型（Record）** — 当前未使用，为后续扩展保留
- **顶级语句（Top-level Statements）** — App.xaml.cs 中使用

---

## 3. 系统架构

### 3.1 整体架构图

```
┌──────────────────────────────────────────────────────────┐
│                      Windows 系统                         │
│  ┌─────────────────────────────────────────────────────┐  │
│  │              SetWindowsHookEx (全局钩子)              │  │
│  │   ┌──────────┐          ┌──────────┐                │  │
│  │   │ WH_KEYBOARD_LL │    │ WH_MOUSE_LL │             │  │
│  │   └──────┬───────┘          └──────┬───────┘         │  │
│  └──────────┼─────────────────────────┼────────────────┘  │
│             │                         │                   │
│             ▼                         ▼                   │
│  ┌──────────────────────────────────────────────────────┐ │
│  │            InputHookService (托管层)                  │ │
│  │  • KeyboardHookCallback()                             │ │
│  │  • MouseHookCallback()                                │ │
│  │  • GetKeyName() / IsModifierKey()                     │ │
│  │  • ObservableCollection<InputEvent> RecentInputs       │ │
│  │  • SessionStatistics Statistics                       │ │
│  │  • CurrentInput (INPC 驱动)                           │ │
│  └────────────────────────────┬─────────────────────────┘ │
│                               │ Dispatcher.Invoke()        │
│                               ▼                             │
│  ┌──────────────────────────────────────────────────────┐ │
│  │               MainWindow (UI 线程)                    │ │
│  │  • 当前输入卡片 (INPC 数据绑定)                        │ │
│  │  • 统计面板 (INPC 数据绑定)                           │ │
│  │  • 输入历史列表 (ObservableCollection 绑定)            │ │
│  │  • 托盘图标 (NotifyIcon)                             │ │
│  └──────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

### 3.2 模块职责

| 模块 | 文件 | 职责 |
|------|------|------|
| **WinApi** | WinApi.cs | Windows API 的 P/Invoke 声明 |
| **Models** | Models.cs | 数据模型 + INotifyPropertyChanged |
| **InputHookService** | InputHookService.cs | 全局钩子注册、事件分发、统计计算 |
| **MainWindow** | MainWindow.xaml/.cs | UI 展示、用户交互、托盘管理 |

---

## 4. 核心模块详解

### 4.1 WinApi.cs — Windows API 封装

本模块声明了所有与 Windows 系统交互所需的 P/Invoke。

```csharp
// 核心钩子设置 API
[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
public static extern IntPtr SetWindowsHookEx(
    int idHook,       // 钩子类型 (WH_KEYBOARD_LL=13, WH_MOUSE_LL=14)
    LowLevelKeyboardProc lpfn,  // 回调委托
    IntPtr hMod,      // 模块句柄
    uint dwThreadId   // 线程ID，0表示全局
);

// 钩子链调用（必须调用，将事件传递给下一个钩子）
[DllImport("user32.dll")]
public static extern IntPtr CallNextHookEx(
    IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

// 获取模块句柄（用于 SetWindowsHookEx）
[DllImport("kernel32.dll")]
public static extern IntPtr GetModuleHandle(string? lpModuleName);

// 判断按键是否按下
[DllImport("user32.dll")]
public static extern short GetAsyncKeyState(int vKey);

// 获取前台窗口（用于敏感内容过滤）
[DllImport("user32.dll")]
public static extern IntPtr GetForegroundWindow();
```

#### 钩子类型常量

```csharp
public const int WH_KEYBOARD_LL = 13;  // 低级键盘钩子
public const int WH_MOUSE_LL = 14;     // 低级鼠标钩子
```

#### 消息常量

```csharp
// 键盘消息
public const int WM_KEYDOWN = 0x0100;    // 普通键按下
public const int WM_KEYUP = 0x0101;       // 普通键释放
public const int WM_SYSKEYDOWN = 0x0104;  // 系统键按下（Alt组合）
public const int WM_SYSKEYUP = 0x0105;    // 系统键释放

// 鼠标消息
public const int WM_MOUSEMOVE = 0x0200;
public const int WM_LBUTTONDOWN = 0x0201;
public const int WM_RBUTTONDOWN = 0x0204;
public const int WM_MOUSEWHEEL = 0x020A;
```

#### 数据结构

```csharp
// 键盘钩子数据结构
[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    public uint VkCode;    // 虚拟键码
    public uint ScanCode;  // 扫描码
    public uint Flags;     // 标志
    public uint Time;      // 时间戳
    public IntPtr DwExtraInfo;
}

// 鼠标钩子数据结构
[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    public POINT Pt;           // 鼠标坐标
    public uint MouseData;     // 滚轮数据（高位为滚动量）
    public uint Flags;
    public uint Time;
    public IntPtr DwExtraInfo;
}
```

### 4.2 Models.cs — 数据模型

#### InputEvent（输入事件）

```csharp
public class InputEvent : INotifyPropertyChanged
{
    public DateTime Time { get; set; }
    public InputType Type { get; set; }     // Keyboard / Mouse
    public string Description { get; set; } // 格式化后的描述
    public string Category { get; set; }    // "⌨ 键盘" / "🖱 鼠标"
    public string RawKey { get; set; }      // 原始键名

    // 用于 XAML 绑定的计算属性
    public string TimeStr => _time.ToString("HH:mm:ss");
    public bool IsKeyboard => _type == InputType.Keyboard;
    public bool IsMouse => _type == InputType.Mouse;
}
```

#### SessionStatistics（会话统计）

```csharp
public class SessionStatistics : INotifyPropertyChanged
{
    // 基础计数
    public int TotalKeyPresses { get; set; }
    public int TotalMouseClicks { get; set; }
    public int TotalMouseMoves { get; set; }
    public int TotalScrolls { get; set; }

    // 鼠标按钮计数
    public int LeftClickCount { get; set; }
    public int RightClickCount { get; set; }
    public int MiddleClickCount { get; set; }
    public int WheelScrollCount { get; set; }

    // 按键频率字典
    public Dictionary<string, int> KeyFrequency { get; set; }

    // Top5 高频按键（ObservableCollection 供 XAML 直接绑定）
    public ObservableCollection<KeyStatistic> TopKeys { get; }

    // 会话时长（自动计算）
    public string SessionDurationStr => (DateTime.Now - _sessionStart).ToString();

    // 刷新 TopKeys（由 UI 线程调用）
    public void RefreshTopKeys()
    {
        TopKeys.Clear();
        foreach (var kv in KeyFrequency.OrderByDescending(kv => kv.Value).Take(5))
            TopKeys.Add(new KeyStatistic { Key = kv.Key, Count = kv.Value });
    }
}
```

### 4.3 InputHookService.cs — 核心钩子服务

#### 启动流程

```csharp
public void Start()
{
    // 1. 获取当前进程模块句柄（用于钩子）
    using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
    using var curModule = curProcess.MainModule;
    IntPtr moduleHandle = WinApi.GetModuleHandle(curModule.ModuleName);

    // 2. 注册键盘钩子
    _keyboardHookId = WinApi.SetWindowsHookEx(
        WinApi.WH_KEYBOARD_LL,  // 钩子类型
        _keyboardProc,          // 回调委托（实例方法，保持 GC 不回收）
        moduleHandle,            // 模块句柄
        0                       // 0 = 全局钩子
    );

    // 3. 注册鼠标钩子
    _mouseHookId = WinApi.SetWindowsHookEx(
        WinApi.WH_MOUSE_LL,
        _mouseProc,
        moduleHandle,
        0
    );
}
```

#### 键盘回调处理流程

```
KeyboardHookCallback(nCode, wParam, lParam)
    │
    ├─ nCode < 0？→ 直接 CallNextHook（不处理）
    │
    ├─ 解析 KBDLLHOOKSTRUCT（lParam）
    │     • vkCode = hookStruct.VkCode
    │     • msg = wParam.ToInt32()
    │
    ├─ 判断消息类型
    │     • WM_KEYDOWN / WM_SYSKEYDOWN → isKeyDown = true
    │     • WM_KEYUP / WM_SYSKEYUP → isKeyUp = true
    │
    ├─ 获取当前修饰键状态（GetAsyncKeyState）
    │     • Ctrl = GetAsyncKeyState(VK_CONTROL) & 0x8000
    │     • Shift / Alt / Win 同理
    │
    ├─ 是修饰键本身被按下？
    │     • Yes → 创建 InputEvent，插入 RecentInputs，返回 CallNextHook
    │     • No  → 继续
    │
    ├─ 格式化描述
    │     • modifiers + keyName（按下）
    │     • modifiers + keyName + ↑（释放）
    │
    └─ Dispatcher.Invoke() 更新 UI
          • CurrentInput = event（触发 INPC）
          • RecentInputs.Insert(0, event)
          • Statistics.TotalKeyPresses++
          • KeyFrequency[keyName]++
```

#### 修饰键特殊处理

修饰键（Ctrl/Shift/Alt/Win）的处理策略：

- **修饰键作为唯一按键时**：显示其名称（如 `LCtrl`、`RWin`）
- **修饰键与其他键组合时**：不重复显示修饰键名称，而是作为前缀组合（如 `Ctrl+C`）

```csharp
// GetAsyncKeyState 获取的是"当前"状态，可能包含其他键已按下的修饰键
// 所以需要在回调中实时检测
bool ctrl = (WinApi.GetAsyncKeyState(WinApi.VK_CONTROL) & 0x8000) != 0;
bool shift = (WinApi.GetAsyncKeyState(WinApi.VK_SHIFT) & 0x8000) != 0;
bool alt = (WinApi.GetAsyncKeyState(WinApi.VK_MENU) & 0x8000) != 0;
bool win = (WinApi.GetAsyncKeyState(WinApi.VK_LWIN) & 0x8000) != 0 ||
           (WinApi.GetAsyncKeyState(WinApi.VK_RWIN) & 0x8000) != 0;
```

#### 虚拟键码映射（GetKeyName）

```csharp
// 部分键码映射示例
private static string GetKeyName(int vkCode)
{
    return vkCode switch
    {
        0x08 => "Backspace",  // 0x08 = BS 字符
        0x0D => "Enter",      // 回车
        0x20 => "Space",      // 空格
        0x41 => "A",           // 'A' = 0x41
        ...
        // 注意：0x14 = CapsLock 不等于 'A' (0x41)
        0x14 => "CapsLock",
        ...
    };
}
```

**常见误区**：虚拟键码（VK_CODE）与 ASCII 码不完全相同。例如 `A` 的 VK_CODE 是 `0x41`（等于 ASCII 'A'），但 `0` 的 VK_CODE 是 `0x30`，而 `CapsLock` 是 `0x14`（不等于任何 ASCII 字符）。

### 4.4 MainWindow.xaml — UI 布局

```
Border (主容器，毛玻璃背景)
├── Border (标题栏，渐变背景)
│   ├── TextBlock ("输入监控")
│   ├── Badge ("● 录制中")
│   └── Button (📊) | Button (─) | Button (✕)
│
├── Border (当前输入卡片，动态背景色)
│   ├── TextBlock (⌨/🖱)        ← DataTrigger 切换
│   ├── TextBlock (描述)
│   └── TextBlock (时间)
│
├── Border (统计面板，Collapsed)
│   ├── Grid (基础统计)
│   ├── StackPanel (鼠标按钮)
│   ├── ItemsControl (Top5)
│   └── TextBlock (会话时长)
│
└── ListBox (历史列表)
    └── DataTemplate (每行样式)
```

#### 关键技术点

**1. 半透明窗口**
```xml
WindowStyle="None"
AllowsTransparency="True"
Background="Transparent"
```

**2. 圆角边框**
```xml
Border CornerRadius="12"
Border Brush="#20000000" BorderThickness="1"
```

**3. 动态样式（键盘蓝、鼠标绿）**
```xml
<DataTrigger Binding="{Binding CurrentInput.IsKeyboard}" Value="True">
    <Setter Property="Background" Value="#EEF2FF"/>
    <Setter Property="BorderBrush" Value="#AABBFF"/>
</DataTrigger>
```

**4. 数据绑定（无 Code-Behind 的 UI 更新）**
```xml
Text="{Binding CurrentInput.Description}"
Visibility="{Binding CurrentInput, Converter={StaticResource NullToVisibility}}"
```

---

## 5. Windows API 详解

### 5.1 全局钩子原理

Windows 提供了 `SetWindowsHookEx` 函数，允许向系统注入一个回调 DLL，当特定类型事件发生时，系统会调用该 DLL 中的回调函数。

```
应用程序进程                    系统进程
     │                              │
     ▼                              ▼
┌─────────┐                   ┌─────────────┐
│ Hook DLL │ ←─────────────── │   系统消息   │
│ (托管的回调) │                │    队列     │
└────┬────┘                   └─────────────┘
     │ CallNextHookEx()              ▲
     └──────────────────────────────┘
```

对于 **Low-Level 钩子**（`_LL` 后缀），不需要单独的 DLL，托管代码中直接注册回调即可。

### 5.2 钩子回调的线程模型

```
系统线程（CRT DLLHook）          UI 线程（主窗口）
         │                              │
         │ CallNextHookEx()             │
         ├─────────────────────────────► │
         │ Dispatcher.Invoke()           │
         │ (Marshal 到 UI 线程)          │
         │                              ▼
         │                    ┌──────────────────┐
         │                    │ 更新 Observable  │
         │                    │ 触发 INPC        │
         │                    │ 更新 UI 绑定     │
         │                    └──────────────────┘
```

**关键点**：
- 钩子回调运行在系统 CRT 线程上
- `Dispatcher.Invoke()` 将代码调度到 UI 线程
- 所有 UI 操作（ObservableCollection 修改、INPC 通知）必须在 UI 线程执行

### 5.3 为什么不直接操作 UI

```csharp
// ❌ 错误：在钩子回调线程中直接操作 UI
RecentInputs.Insert(0, inputEvent);  // 可能崩溃：集合非线程安全

// ✅ 正确：调度到 UI 线程
Application.Current?.Dispatcher.Invoke(() =>
{
    RecentInputs.Insert(0, inputEvent);
});
```

---

## 6. 数据流分析

```
输入事件 → 钩子回调 → 格式化 → Dispatcher.Invoke
                                     │
           ┌─────────────────────────┼─────────────────────────┐
           │                         │                         │
           ▼                         ▼                         ▼
    CurrentInput (INPC)       RecentInputs              Statistics
    ┌──────────────┐         (ObservableCollection)      ┌──────────┐
    │ 触发 INPC    │         ┌────────────────┐           │ KeyCount │
    │ 刷新卡片绑定 │         │ Insert(0, evt) │           │ ClickCnt │
    │              │         │ （自动通知UI）  │           │ TopKeys  │
    └──────────────┘         └────────────────┘           └──────────┘
           │                         │                         │
           ▼                         ▼                         ▼
    "当前输入卡片"              ListBox (历史)             统计面板
    TextBlock.Text             自动渲染新行              数据刷新
```

---

## 7. 线程模型

| 线程 | 来源 | 职责 |
|------|------|------|
| UI 线程 | WPF 应用主线程 | 所有 UI 操作、INPC 通知 |
| CRT 钩子线程 | Windows 系统 | 键盘/鼠标事件回调 |
| Timer 线程 | DispatcherTimer | 定期刷新会话时长显示 |

### 7.1 线程安全设计

```csharp
// InputHookService 中所有 UI 操作都在 Dispatcher 内
System.Windows.Application.Current?.Dispatcher.Invoke(() =>
{
    CurrentInput = inputEvent;           // INPC 触发
    RecentInputs.Insert(0, inputEvent); // 集合变更通知
    Statistics.TotalKeyPresses++;        // 属性变更通知
});
```

---

## 8. 关键设计决策

### 8.1 为什么用 ObservableCollection 而不是 List

`List<T>` 的 `Add/Insert/Remove` 不会触发 UI 更新。`ObservableCollection<T>` 实现了 `INotifyCollectionChanged`，集合变更会自动通知 UI。

```csharp
// ✅ ObservableCollection — UI 自动更新
public ObservableCollection<InputEvent> RecentInputs { get; } = new();

// ❌ List — 不会触发 UI 更新
public List<InputEvent> RecentInputs { get; } = new();
```

### 8.2 为什么 CurrentInput 用 INotifyPropertyChanged

`CurrentInput` 是引用类型赋值，每次替换时 WPF 不会自动刷新绑定。必须触发 INPC 的 `PropertyChanged` 事件。

```csharp
// ❌ 缺少 INPC：WPF 不知道属性已变更
private InputEvent? _currentInput;
public InputEvent? CurrentInput => _currentInput;

// ✅ 正确：属性变更时通知 WPF
private set
{
    _currentInput = value;
    OnPropertyChanged();  // WPF 重新读取 CurrentInput
}
```

### 8.3 为什么用 AllowsTransparency + NoneWindowStyle

标准 WPF 窗口无法实现圆角和透明。组合 `AllowsTransparency="True"` + `WindowStyle="None"` + `Background="Transparent"` 实现自定义形状窗口。

### 8.4 为什么用 NotifyIcon（WinForms）

WPF 没有内置的托盘图标组件。`System.Windows.Forms.NotifyIcon` 是最轻量的选择（只需要 `UseWindowsForms="true"`），无需引入额外依赖。

---

## 9. 编译与发布

### 9.1 项目配置（InputDisplay.csproj）

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <AssemblyName>InputDisplay</AssemblyName>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
```

关键配置说明：
- `UseWPF=true`：启用 WPF 支持
- `UseWindowsForms=true`：启用 WinForms（托盘图标需要）
- `AllowUnsafeBlocks=true`：允许 unsafe 代码（DLLImport 不需要，但保留）

### 9.2 编译命令

```bash
# Debug 版本（开发用）
dotnet build

# Release 版本（依赖系统 .NET 运行时）
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish

# 自包含版本（无需 .NET 运行时，但文件较大）
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish-standalone
```

### 9.3 运行时依赖

| 文件 | 说明 |
|------|------|
| `InputDisplay.exe` | 主程序 |
| `InputDisplay.dll` | 托管程序集 |
| `InputDisplay.deps.json` | 依赖清单 |
| `InputDisplay.runtimeconfig.json` | 运行时配置 |
| `dotnet` (系统安装) | .NET 8.0 Runtime |

---

## 10. 扩展方向

### 10.1 已知的可以改进的方向

- **敏感内容过滤**：检测前台窗口是否为密码框，跳过密码输入的记录
- **数据导出**：将统计历史导出为 CSV/JSON 文件
- **热键配置**：自定义呼出/隐藏浮窗的快捷键
- **多语言支持**：中英文界面切换
- **开机自启**：添加注册表自启动支持
- **自定义主题**：支持深色模式切换
- **录制回放**：将输入历史导出为可回放的序列

### 10.2 代码质量提升方向

- 添加单元测试（xUnit）
- 使用 CommunityToolkit.Mvvm 重构为 MVVM 模式
- 添加日志框架（Serilog）
- 配置管理重构为 JSON/YAML 格式

---

*文档版本：v1.0.0 | 最后更新：2026-03-24*
