using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;

namespace InputDisplay;

public class InputHookService : INotifyPropertyChanged, IDisposable
{
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private WinApi.LowLevelKeyboardProc? _keyboardProc;
    private WinApi.LowLevelMouseProc? _mouseProc;

    private bool _disposed;

    public event EventHandler<InputEvent>? InputCaptured;

    public ObservableCollection<InputEvent> RecentInputs { get; } = new();
    public SessionStatistics Statistics { get; } = new();

    private InputEvent? _currentInput;
    public InputEvent? CurrentInput
    {
        get => _currentInput;
        private set
        {
            _currentInput = value;
            OnPropertyChanged();
            CurrentInputChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<InputEvent?>? CurrentInputChanged;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private const int MaxRecentInputs = 200;

    public void Start()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;

        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr moduleHandle = curModule != null
            ? WinApi.GetModuleHandle(curModule.ModuleName)
            : WinApi.GetModuleHandle(null);

        _keyboardHookId = WinApi.SetWindowsHookEx(
            WinApi.WH_KEYBOARD_LL,
            _keyboardProc,
            moduleHandle,
            0);

        _mouseHookId = WinApi.SetWindowsHookEx(
            WinApi.WH_MOUSE_LL,
            _mouseProc,
            moduleHandle,
            0);

        if (_keyboardHookId == IntPtr.Zero || _mouseHookId == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to install input hooks");
        }
    }

    public void Stop()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            WinApi.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
        if (_mouseHookId != IntPtr.Zero)
        {
            WinApi.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<WinApi.KBDLLHOOKSTRUCT>(lParam);
            int vkCode = (int)hookStruct.VkCode;
            int msg = wParam.ToInt32();

            bool isKeyDown = msg == WinApi.WM_KEYDOWN || msg == WinApi.WM_SYSKEYDOWN;
            bool isKeyUp = msg == WinApi.WM_KEYUP || msg == WinApi.WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                string keyName = GetKeyName(vkCode);
                bool ctrl = (WinApi.GetAsyncKeyState(WinApi.VK_CONTROL) & 0x8000) != 0;
                bool shift = (WinApi.GetAsyncKeyState(WinApi.VK_SHIFT) & 0x8000) != 0;
                bool alt = (WinApi.GetAsyncKeyState(WinApi.VK_MENU) & 0x8000) != 0;
                bool win = (WinApi.GetAsyncKeyState(WinApi.VK_LWIN) & 0x8000) != 0 ||
                           (WinApi.GetAsyncKeyState(WinApi.VK_RWIN) & 0x8000) != 0;

                string modifiers = "";
                if (win) modifiers += "Win+";
                if (ctrl) modifiers += "Ctrl+";
                if (alt) modifiers += "Alt+";
                if (shift) modifiers += "Shift+";

                string description;
                bool isModifier = IsModifierKey(vkCode);

                // 修饰键单独显示（不叠加到其他键上），其他键按正常流程
                if (isModifier && (isKeyDown || isKeyUp))
                {
                    var modEvent = new InputEvent
                    {
                        Type = InputType.Keyboard,
                        Description = keyName,
                        Category = "⌨ 键盘",
                        RawKey = keyName
                    };
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        CurrentInput = modEvent;
                        RecentInputs.Insert(0, modEvent);
                        while (RecentInputs.Count > MaxRecentInputs)
                            RecentInputs.RemoveAt(RecentInputs.Count - 1);
                    });
                    return CallNextHook(wParam);
                }

                if (isKeyUp)
                {
                    description = $"{modifiers}{keyName} ↑";
                }
                else
                {
                    description = $"{modifiers}{keyName}";
                }

                var inputEvent = new InputEvent
                {
                    Type = InputType.Keyboard,
                    Description = description.TrimEnd('↑').Trim(),
                    Category = "⌨ 键盘",
                    RawKey = keyName
                };

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    CurrentInput = inputEvent;
                    RecentInputs.Insert(0, inputEvent);
                    while (RecentInputs.Count > MaxRecentInputs)
                        RecentInputs.RemoveAt(RecentInputs.Count - 1);

                    Statistics.TotalKeyPresses++;
                    if (!string.IsNullOrEmpty(keyName))
                    {
                        if (!Statistics.KeyFrequency.ContainsKey(keyName))
                            Statistics.KeyFrequency[keyName] = 0;
                        Statistics.KeyFrequency[keyName]++;
                    }
                });

                InputCaptured?.Invoke(this, inputEvent);
            }
        }

        return CallNextHook(wParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<WinApi.MSLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            int x = hookStruct.Pt.X;
            int y = hookStruct.Pt.Y;

            string? description = null;
            InputType type = InputType.Mouse;

            switch (msg)
            {
                case WinApi.WM_MOUSEMOVE:
                    description = $"移动 @ ({x}, {y})";
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => Statistics.TotalMouseMoves++);
                    break;
                case WinApi.WM_LBUTTONDOWN:
                    description = $"左键按下 @ ({x}, {y})";
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Statistics.LeftClickCount++;
                        Statistics.TotalMouseClicks++;
                    });
                    break;
                case WinApi.WM_LBUTTONUP:
                    description = $"左键释放 @ ({x}, {y})";
                    break;
                case WinApi.WM_RBUTTONDOWN:
                    description = $"右键按下 @ ({x}, {y})";
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Statistics.RightClickCount++;
                        Statistics.TotalMouseClicks++;
                    });
                    break;
                case WinApi.WM_RBUTTONUP:
                    description = $"右键释放 @ ({x}, {y})";
                    break;
                case WinApi.WM_MBUTTONDOWN:
                    description = $"中键按下 @ ({x}, {y})";
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Statistics.MiddleClickCount++;
                        Statistics.TotalMouseClicks++;
                    });
                    break;
                case WinApi.WM_MBUTTONUP:
                    description = $"中键释放 @ ({x}, {y})";
                    break;
                case WinApi.WM_MOUSEWHEEL:
                    short delta = (short)((hookStruct.MouseData >> 16) & 0xFFFF);
                    string wheelDir = delta > 0 ? "↑" : "↓";
                    description = $"滚轮 {Math.Abs(delta) / 120}格 {wheelDir}";
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Statistics.WheelScrollCount++;
                        Statistics.TotalScrolls++;
                    });
                    break;
                case WinApi.WM_MOUSEHWHEEL:
                    short hDelta = (short)((hookStruct.MouseData >> 16) & 0xFFFF);
                    string hWheelDir = hDelta > 0 ? "→" : "←";
                    description = $"横滚 {Math.Abs(hDelta) / 120}格 {hWheelDir}";
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Statistics.WheelScrollCount++;
                        Statistics.TotalScrolls++;
                    });
                    break;
            }

            if (description != null)
            {
                var inputEvent = new InputEvent
                {
                    Type = type,
                    Description = description,
                    Category = "🖱 鼠标"
                };

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    CurrentInput = inputEvent;
                    RecentInputs.Insert(0, inputEvent);
                    while (RecentInputs.Count > MaxRecentInputs)
                        RecentInputs.RemoveAt(RecentInputs.Count - 1);
                });
            }
        }

        return CallNextHook(wParam);
    }

    private IntPtr CallNextHook(IntPtr wParam)
    {
        IntPtr hookId = wParam == (IntPtr)WinApi.WM_KEYDOWN ||
                        wParam == (IntPtr)WinApi.WM_KEYUP ||
                        wParam == (IntPtr)WinApi.WM_SYSKEYDOWN ||
                        wParam == (IntPtr)WinApi.WM_SYSKEYUP
            ? _keyboardHookId : _mouseHookId;
        return WinApi.CallNextHookEx(hookId, 0, wParam, IntPtr.Zero);
    }

    private static string GetKeyName(int vkCode)
    {
        return vkCode switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "←",
            0x26 => "↑",
            0x27 => "→",
            0x28 => "↓",
            0x2D => "Insert",
            0x2E => "Delete",
            0x30 => "0", 0x31 => "1", 0x32 => "2", 0x33 => "3",
            0x34 => "4", 0x35 => "5", 0x36 => "6", 0x37 => "7",
            0x38 => "8", 0x39 => "9",
            0x41 => "A", 0x42 => "B", 0x43 => "C", 0x44 => "D",
            0x45 => "E", 0x46 => "F", 0x47 => "G", 0x48 => "H",
            0x49 => "I", 0x4A => "J", 0x4B => "K", 0x4C => "L",
            0x4D => "M", 0x4E => "N", 0x4F => "O", 0x50 => "P",
            0x51 => "Q", 0x52 => "R", 0x53 => "S", 0x54 => "T",
            0x55 => "U", 0x56 => "V", 0x57 => "W", 0x58 => "X",
            0x59 => "Y", 0x5A => "Z",
            0x60 => "Num0", 0x61 => "Num1", 0x62 => "Num2", 0x63 => "Num3",
            0x64 => "Num4", 0x65 => "Num5", 0x66 => "Num6", 0x67 => "Num7",
            0x68 => "Num8", 0x69 => "Num9",
            0x6A => "Num*", 0x6B => "Num+", 0x6D => "Num-", 0x6E => "Num.",
            0x6F => "Num/",
            0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
            0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
            0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
            0x90 => "NumLock",
            0xA0 => "LShift", 0xA1 => "RShift",
            0xA2 => "LCtrl", 0xA3 => "RCtrl",
            0xA4 => "LAlt", 0xA5 => "RAlt",
            0x5B => "LWin", 0x5C => "RWin",
            0x14 => "CapsLock",
            0x2C => "PrintScreen",
            0x13 => "Pause",
            0x5D => "Menu",
            0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-",
            0xBE => ".", 0xBF => "/", 0xC0 => "`",
            0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
            _ => $"Key{vkCode}"
        };
    }

    private static bool IsModifierKey(int vkCode)
    {
        return vkCode == 0xA0 || vkCode == 0xA1 || // Shift
               vkCode == 0xA2 || vkCode == 0xA3 || // Ctrl
               vkCode == 0xA4 || vkCode == 0xA5 || // Alt
               vkCode == 0x5B || vkCode == 0x5C;   // Win
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
