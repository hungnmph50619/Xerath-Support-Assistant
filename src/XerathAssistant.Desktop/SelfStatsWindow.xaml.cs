using System.Net.Http;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

public partial class SelfStatsWindow : Window
{
    private readonly RiotLocalSelfStatsClient _local = new();
    private readonly SelfStatsSession _session = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _cancel = new();
    private SelfStatsHudWindow? _hud;
    private readonly PersonalStatAlerts _personalAlerts = new();
    private readonly PublicKillEventTracker _publicEvents = new();
    private readonly ReminderEngine _hudReminders = new(TimeSpan.FromSeconds(15));
    private readonly Stopwatch _hudElapsed = new();
    private ReminderItem[] _hudReminderItems =
    {
        new("minimap", "Kiểm tra minimap.", TimeSpan.FromSeconds(45)),
        new("vision", "Kiểm tra tầm nhìn khu vực sông.", TimeSpan.FromMinutes(3))
    };
    private int _eventPollCount;
    private bool _eventFetching;
    private bool _fetching;
    private bool _closed;

    public SelfStatsWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) =>
        {
            ShowDueHudReminder();
            await ReadNowAsync();
        };
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (RefreshCheck.IsChecked == true) _timer.Start();
        await ReadNowAsync();
    }

    private void RefreshChanged(object sender, RoutedEventArgs e)
    {
        if (_timer is null) return;
        // HUD never displays stale values silently because polling was turned off.
        if (_hud is not null && RefreshCheck?.IsChecked != true)
        {
            if (RefreshCheck is not null) RefreshCheck.IsChecked = true;
            return;
        }
        if (RefreshCheck?.IsChecked == true && !_closed) _timer.Start();
        else _timer.Stop();
    }

    public void ConfigureHudReminders(IEnumerable<ReminderItem> reminders)
    {
        _hudReminderItems = reminders.ToArray();
        if (_hud is not null) StartHudReminders();
    }

    private void StartHudReminders()
    {
        _hudReminders.Start(_hudReminderItems);
        _hudElapsed.Restart();
    }

    private void ShowDueHudReminder()
    {
        if (_hud is null || !_hudElapsed.IsRunning) return;
        var message = _hudReminders.Tick(_hudElapsed.Elapsed);
        if (!string.IsNullOrWhiteSpace(message))
            _hud.ShowNotice(message.Replace(" | ", " · "));
    }

    public void EnableHud(bool hidePanel = false)
    {
        if (_closed) return;
        if (_hud is null)
        {
            _hud = new SelfStatsHudWindow();
            SetHudCorner();
            StartHudReminders();
        }
        RefreshCheck.IsChecked = true;
        _timer.Start();
        ToggleHudButton.Content = "Tắt HUD";
        HudHint.Text = "HUD đang bật: chỉ hiện khi Liên Minh là cửa sổ được chọn. Mở lại Chỉ số trực tiếp & tổng hợp để tắt hoặc đổi góc.";
        _hud.RefreshVisibility();
        if (hidePanel) Hide();
    }

    private void DisableHud()
    {
        _hudElapsed.Reset();
        _hud?.Close();
        _hud = null;
        ToggleHudButton.Content = "Bật HUD trên game";
        HudHint.Text = "HUD đã tắt. Bật lại trước khi quay lại game; dùng chế độ cửa sổ Không viền (Borderless).";
    }

    private void ToggleHudClick(object sender, RoutedEventArgs e)
    {
        if (_hud is not null) DisableHud();
        else EnableHud(hidePanel: true);
    }

    private void HudCornerChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => SetHudCorner();

    private void SetHudCorner()
    {
        if (_hud is null || HudCornerBox?.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
        if (Enum.TryParse<HudCorner>(item.Tag?.ToString(), out var corner))
            _hud.SetCorner(corner);
    }

    private async void FetchNowClick(object sender, RoutedEventArgs e) => await ReadNowAsync();

    private void ResetSessionClick(object sender, RoutedEventArgs e)
    {
        _session.Reset();
        SummaryValue.Text = "Đã xóa số liệu phiên. Chờ lần đọc tiếp theo.";
    }

    private async Task ReadNowAsync()
    {
        if (_fetching || _closed) return;
        _fetching = true;
        try
        {
            var snapshot = await _local.ReadAsync(_cancel.Token);
            if (_closed) return;
            _session.Add(snapshot);
            _hud?.SetSnapshot(snapshot);
            var personalWarnings = _personalAlerts.Observe(snapshot);
            if (_hud is not null && personalWarnings.Count > 0)
                _hud.ShowNotice(string.Join(" ", personalWarnings), priority: true);

            GameClock.Text = SelfStatsSnapshot.Clock(snapshot.GameTimeSeconds);
            LevelValue.Text = snapshot.Level.ToString();
            HealthValue.Text = $"{snapshot.Health:0} / {snapshot.MaxHealth:0}  ({snapshot.HealthPercent:0}%)";
            HealthBar.Value = Math.Clamp(snapshot.HealthPercent, 0, 100);
            ResourceLabel.Text = snapshot.ResourceType.Equals("MANA", StringComparison.OrdinalIgnoreCase)
                ? "Năng lượng" : "Tài nguyên (" + snapshot.ResourceType + ")";
            ResourceValue.Text = snapshot.MaxResource > 0
                ? $"{snapshot.Resource:0} / {snapshot.MaxResource:0}  ({snapshot.ResourcePercent:0}%)"
                : "Không sử dụng tài nguyên";
            ResourceBar.Value = snapshot.MaxResource > 0
                ? Math.Clamp(snapshot.ResourcePercent, 0, 100) : 0;
            GoldValue.Text = $"{snapshot.Gold:0} vàng";
            ApValue.Text = $"{snapshot.AbilityPower:0} AP";

            if (EventsCheck.IsChecked == true && ++_eventPollCount % 2 == 0)
                _ = PollPublicEventsAsync(snapshot.GameTimeSeconds);

            ConnectionStatus.Text = $"Đã kết nối API Riot trên máy · cập nhật lúc {DateTime.Now:HH:mm:ss}";
            SummaryValue.Text =
                $"Số lần đọc dữ liệu: {_session.Samples}.\n" +
                $"Máu thấp nhất đã quan sát: {_session.LowestHealthPercent:0}%.\n" +
                $"Năng lượng thấp nhất đã quan sát: " +
                (snapshot.ResourceType.Equals("MANA", StringComparison.OrdinalIgnoreCase)
                    ? $"{_session.LowestResourcePercent:0}%." : "không áp dụng.") + "\n" +
                $"Thời gian quan sát có năng lượng dưới 25%: {_session.LowResourceObservedSeconds:0} giây (xấp xỉ).\n" +
                $"Lượng vàng hiện có cao nhất ghi nhận: {_session.HighestObservedGold:0}.";
        }
        catch (OperationCanceledException) when (_closed || _cancel.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_closed)
            {
                _hud?.MarkUnavailable();
                ConnectionStatus.Text = "Chưa đọc được thông tin trong trận từ API Riot. " +
                    "Hãy vào trận rồi nhấn Cập nhật ngay. Chỉ số bên dưới (nếu có) là dữ liệu lần đọc trước.";
            }
        }
        finally { _fetching = false; }
    }

    private async Task PollPublicEventsAsync(double gameTime)
    {
        if (_eventFetching || _closed) return;
        _eventFetching = true;
        try
        {
            var json = await _local.ReadEventsJsonAsync(_cancel.Token);
            if (_closed) return;
            var message = _publicEvents.Observe(json, gameTime);
            if (message is not null) _hud?.ShowNotice(message);
        }
        catch (OperationCanceledException) when (_closed || _cancel.IsCancellationRequested) { }
        catch (Exception) { /* Optional feed cannot interrupt stats or reminders. */ }
        finally { _eventFetching = false; }
    }

    private void WindowClosed(object sender, EventArgs e)
    {
        _closed = true;
        _hudElapsed.Stop();
        _hud?.Close();
        _hud = null;
        _timer.Stop();
        _cancel.Cancel();
        _local.Dispose();
        _cancel.Dispose();
    }
}
