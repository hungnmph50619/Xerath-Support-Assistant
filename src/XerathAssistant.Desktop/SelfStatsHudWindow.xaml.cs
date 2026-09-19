using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

public enum HudCorner { TopRight, TopLeft, BottomLeft, BottomRight }

/// <summary>
/// Opt-in click-through, topmost window. Only shows while League's game process
/// is foreground. Does not access game memory, capture pixels or inject input.
/// </summary>
public partial class SelfStatsHudWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private readonly DispatcherTimer _foregroundTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private HudCorner _corner = HudCorner.TopRight;
    private bool _ready;
    private bool _closed;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public SelfStatsHudWindow()
    {
        InitializeComponent();
        _foregroundTimer.Tick += (_, _) => RefreshVisibility();
        _foregroundTimer.Start();
        Closed += (_, _) =>
        {
            _closed = true;
            _foregroundTimer.Stop();
        };
        SetCorner(_corner);
    }

    private void WindowSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLong(hwnd, GwlExStyle);
        _ = SetWindowLong(hwnd, GwlExStyle, styles | WsExTransparent | WsExNoActivate);
    }

    public void SetCorner(HudCorner corner)
    {
        _corner = corner;
        var area = SystemParameters.WorkArea;
        var left = corner is HudCorner.TopLeft or HudCorner.BottomLeft;
        var top = corner is HudCorner.TopLeft or HudCorner.TopRight;
        Left = left ? area.Left + 20 : area.Right - Width - 20;
        Top = top ? area.Top + 70 : area.Bottom - Height - 34;
    }

    public void SetSnapshot(SelfStatsSnapshot stats)
    {
        if (_closed) return;
        _ready = true;
        ClockText.Text = SelfStatsSnapshot.Clock(stats.GameTimeSeconds);
        HealthText.Text = $"HP {stats.HealthPercent:0}% ({stats.Health:0})";
        ManaText.Text = stats.MaxResource > 0
            ? $"Mana {stats.ResourcePercent:0}% ({stats.Resource:0})"
            : "Mana —";
        GoldText.Text = $"Vàng: {stats.Gold:0}";
        LevelApText.Text = $"Lv {stats.Level} · AP {stats.AbilityPower:0}";
        StatusText.Text = "Chỉ số cá nhân · cập nhật mỗi 5 giây";
        RefreshVisibility();
    }

    public void MarkUnavailable()
    {
        if (_closed) return;
        _ready = false;
        StatusText.Text = "Mất kết nối API trận · số liệu có thể đã cũ";
        RefreshVisibility();
    }

    private static bool GameIsForeground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return false;
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName.Equals("League of Legends", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public void RefreshVisibility()
    {
        if (_closed) return;
        var visible = GameIsForeground();
        if (visible && !IsVisible) Show();
        else if (!visible && IsVisible) Hide();
    }
}
