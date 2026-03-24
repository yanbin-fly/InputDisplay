using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace InputDisplay;

public enum InputType
{
    Keyboard,
    Mouse
}

public enum MouseBtn
{
    Left,
    Right,
    Middle,
    Wheel,
    XButton
}

public enum MouseAction
{
    Click,
    DoubleClick,
    Down,
    Up,
    Move,
    WheelUp,
    WheelDown,
    WheelLeft,
    WheelRight
}

public class InputEvent : INotifyPropertyChanged
{
    private DateTime _time = DateTime.Now;
    private InputType _type;
    private string _description = string.Empty;
    private string _category = string.Empty;
    private string _rawKey = string.Empty;

    public DateTime Time
    {
        get => _time;
        set { _time = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeStr)); }
    }

    public InputType Type
    {
        get => _type;
        set { _type = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsKeyboard)); OnPropertyChanged(nameof(IsMouse)); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(); }
    }

    public string RawKey
    {
        get => _rawKey;
        set { _rawKey = value; OnPropertyChanged(); }
    }

    public string TimeStr => _time.ToString("HH:mm:ss");

    public bool IsKeyboard => _type == InputType.Keyboard;
    public bool IsMouse => _type == InputType.Mouse;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class KeyStatistic
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class MouseStatistic
{
    public MouseBtn Button { get; set; }
    public MouseAction Action { get; set; }
    public int Count { get; set; }
}

public class SessionStatistics : INotifyPropertyChanged
{
    private DateTime _sessionStart = DateTime.Now;
    private int _totalKeyPresses;
    private int _totalMouseClicks;
    private int _totalMouseMoves;
    private int _totalScrolls;
    private int _leftClickCount;
    private int _rightClickCount;
    private int _middleClickCount;
    private int _wheelScrollCount;
    public Dictionary<string, int> KeyFrequency { get; set; } = new();

    public ObservableCollection<KeyStatistic> TopKeys { get; } = new();

    public DateTime SessionStart
    {
        get => _sessionStart;
        set { _sessionStart = value; OnPropertyChanged(); OnPropertyChanged(nameof(SessionDurationStr)); }
    }

    public int TotalKeyPresses
    {
        get => _totalKeyPresses;
        set { _totalKeyPresses = value; OnPropertyChanged(); }
    }

    public int TotalMouseClicks
    {
        get => _totalMouseClicks;
        set { _totalMouseClicks = value; OnPropertyChanged(); }
    }

    public int TotalMouseMoves
    {
        get => _totalMouseMoves;
        set { _totalMouseMoves = value; OnPropertyChanged(); }
    }

    public int TotalScrolls
    {
        get => _totalScrolls;
        set { _totalScrolls = value; OnPropertyChanged(); }
    }

    public int LeftClickCount
    {
        get => _leftClickCount;
        set { _leftClickCount = value; OnPropertyChanged(); }
    }

    public int RightClickCount
    {
        get => _rightClickCount;
        set { _rightClickCount = value; OnPropertyChanged(); }
    }

    public int MiddleClickCount
    {
        get => _middleClickCount;
        set { _middleClickCount = value; OnPropertyChanged(); }
    }

    public int WheelScrollCount
    {
        get => _wheelScrollCount;
        set { _wheelScrollCount = value; OnPropertyChanged(); }
    }

    public string SessionDurationStr
    {
        get
        {
            var d = DateTime.Now - _sessionStart;
            return $"{(int)d.TotalHours}h {d.Minutes}m {d.Seconds}s";
        }
    }

    public void RefreshTopKeys()
    {
        TopKeys.Clear();
        foreach (var kv in KeyFrequency.OrderByDescending(kv => kv.Value).Take(5))
            TopKeys.Add(new KeyStatistic { Key = kv.Key, Count = kv.Value });
        OnPropertyChanged(nameof(TopKeys));
    }

    public void RefreshDuration()
        => OnPropertyChanged(nameof(SessionDurationStr));

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
