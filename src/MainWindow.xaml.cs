using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace InputDisplay;

public partial class MainWindow : Window
{
    private readonly InputHookService _hookService;
    private readonly DispatcherTimer _durationTimer;
    private bool _statsVisible;
    private bool _started;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private readonly string _settingsPath;
    private int _inputCountBeforeStats = -1;

    public MainWindow()
    {
        InitializeComponent();

        _hookService = new InputHookService();
        DataContext = _hookService;

        // 仅用于刷新会话时长显示（每5秒）
        _durationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _durationTimer.Tick += (_, _) =>
        {
            _hookService.Statistics.RefreshDuration();
        };

        _settingsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InputDisplay", "settings.txt");

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetupNotifyIcon();
        LoadWindowPosition();
        StartHook();
    }

    private void StartHook()
    {
        if (_started) return;
        try
        {
            _hookService.Start();
            _started = true;
            _durationTimer.Start();

            // 监听集合变化用于空状态和当前输入显示
            _hookService.RecentInputs.CollectionChanged += (_, _) =>
            {
                Dispatcher.Invoke(UpdateEmptyState);
            };

            // 监听当前输入变化用于显示卡片
            _hookService.CurrentInputChanged += (_, input) =>
            {
                Dispatcher.Invoke(() =>
                {
                    CurrentInputCard.Visibility = input != null ? Visibility.Visible : Visibility.Collapsed;
                });
            };

            UpdateEmptyState();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"启动输入监控失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _hookService.RecentInputs.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;

        // 展开统计时，如果按键数据有更新则刷新 TopKeys
        if (_statsVisible)
        {
            int count = _hookService.Statistics.TotalKeyPresses;
            if (count != _inputCountBeforeStats)
            {
                _inputCountBeforeStats = count;
                _hookService.Statistics.RefreshTopKeys();
            }
        }
    }

    private void SetupNotifyIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "输入监控",
            Visible = true,
            Icon = System.Drawing.SystemIcons.Application
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("显示窗口", null, (_, _) => ShowWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("重置统计", null, (_, _) => ResetStatistics());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("退出", null, (_, _) => ExitApp());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ResetStatistics()
    {
        Dispatcher.Invoke(() =>
        {
            _hookService.RecentInputs.Clear();
            _hookService.Statistics.TotalKeyPresses = 0;
            _hookService.Statistics.TotalMouseClicks = 0;
            _hookService.Statistics.TotalMouseMoves = 0;
            _hookService.Statistics.TotalScrolls = 0;
            _hookService.Statistics.LeftClickCount = 0;
            _hookService.Statistics.RightClickCount = 0;
            _hookService.Statistics.MiddleClickCount = 0;
            _hookService.Statistics.WheelScrollCount = 0;
            _hookService.Statistics.KeyFrequency.Clear();
            _hookService.Statistics.TopKeys.Clear();
            _hookService.Statistics.SessionStart = DateTime.Now;
            _inputCountBeforeStats = 0;
            CurrentInputCard.Visibility = Visibility.Collapsed;
            UpdateEmptyState();
        });
    }

    private void ExitApp()
    {
        _notifyIcon!.Visible = false;
        _notifyIcon.Dispose();
        _hookService.Stop();
        System.Windows.Application.Current.Shutdown();
    }

    private void StatsToggle_Click(object sender, RoutedEventArgs e)
    {
        _statsVisible = !_statsVisible;
        StatsPanel.Visibility = _statsVisible ? Visibility.Visible : Visibility.Collapsed;

        StatsToggleBtn.Content = _statsVisible ? "📉" : "📊";
        StatsToggleBtn.ToolTip = _statsVisible ? "隐藏统计" : "显示统计";

        // 展开时刷新一次统计数据
        if (_statsVisible)
        {
            _inputCountBeforeStats = -1; // 强制刷新
            _hookService.Statistics.RefreshDuration();
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _notifyIcon?.ShowBalloonTip(1000, "输入监控", "已最小化到托盘，双击托盘图标恢复",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _notifyIcon?.ShowBalloonTip(1000, "输入监控", "窗口已隐藏，运行于后台。继续监控中...",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void InputListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left)
            return;
        if (_hookService.RecentInputs.Count == 0)
            return;

        var point = e.GetPosition(InputListBox);
        var item = InputListBox.InputHitTest(point) as DependencyObject;
        while (item != null && item != InputListBox)
        {
            if (item is ListBoxItem lbi && lbi.DataContext is InputEvent evt)
            {
                try
                {
                    System.Windows.Clipboard.SetText($"{evt.Category} {evt.Description}");
                }
                catch { }
                return;
            }
            item = System.Windows.Media.VisualTreeHelper.GetParent(item);
        }
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    private void Window_LocationChanged(object? sender, EventArgs e) => SaveWindowPosition();
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => SaveWindowPosition();

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        _notifyIcon?.ShowBalloonTip(1000, "输入监控", "窗口已关闭，运行于后台。继续监控中...",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void SaveWindowPosition()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_settingsPath)!;
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(_settingsPath,
                $"{(int)Left},{(int)Top},{(int)ActualWidth},{(int)ActualHeight}");
        }
        catch { }
    }

    private void LoadWindowPosition()
    {
        try
        {
            if (System.IO.File.Exists(_settingsPath))
            {
                var parts = System.IO.File.ReadAllText(_settingsPath).Split(',');
                if (parts.Length >= 4)
                {
                    int left = int.Parse(parts[0]);
                    int top = int.Parse(parts[1]);
                    int width = int.Parse(parts[2]);
                    int height = int.Parse(parts[3]);

                    var screen = SystemParameters.WorkArea;
                    left = Math.Max(0, Math.Min(left, (int)screen.Width - 100));
                    top = Math.Max(0, Math.Min(top, (int)screen.Height - 100));

                    Left = left;
                    Top = top;
                    Width = Math.Max(280, width);
                    Height = Math.Max(160, height);
                    return;
                }
            }
        }
        catch { }

        // 默认位置：右下角
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 20;
        Top = workArea.Bottom - Height - 20;
    }

    protected override void OnClosed(EventArgs e)
    {
        _durationTimer.Stop();
        _hookService.Dispose();
        _notifyIcon?.Dispose();
        base.OnClosed(e);
    }
}
