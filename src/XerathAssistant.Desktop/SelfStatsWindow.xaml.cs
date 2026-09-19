using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

public partial class SelfStatsWindow : Window
{
    private readonly RiotLocalSelfStatsClient _local = new();
    private readonly PersonalAiBridgeClient _bridge = new();
    private readonly OwnLifeStateDetector _life = new();
    private readonly FreeVietnameseVoiceService _voice = new();
    private readonly MediaPlayer _hudPlayer = new() { Volume = 0.7 };
    private DateTime _lastHudSpeechUtc = DateTime.MinValue;
    private string? _lastHudSpeechPhrase;
    private double _latestLifeTransitionTime = -1;
    private readonly SelfStatsSession _session = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _cancel = new();
    private SelfStatsHudWindow? _hud;
    private readonly PersonalStatAlerts _personalAlerts = new();
    private readonly OwnHealthChangeDetector _healthChangeDetector = new();
    private readonly OwnDangerAnalyzer _dangerAnalyzer = new();
    private double _dangerStatusUntilGameTime = double.NegativeInfinity;
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
        _hudPlayer.MediaEnded += (_, _) => _hudPlayer.Close();
        _hudPlayer.MediaFailed += (_, _) =>
        {
            if (!_closed) HudVoiceStatus.Text = "Không phát được tệp âm thanh tiếng Việt. Hãy thử nút Nghe thử.";
            _hudPlayer.Close();
        };
        UpdateHudVoiceStatus();
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
        if (_hud is null || !_hudElapsed.IsRunning || TimeRemindersCheck.IsChecked != true) return;
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
        _hud.SetLifeState(_life.IsDead);
        UpdateHudVoiceStatus();
        _timer.Start();
        ToggleHudButton.Content = "Tắt HUD";
        HudHint.Text = "HUD đang bật: chỉ hiện khi Liên Minh là cửa sổ được chọn. Mở lại Chỉ số trực tiếp & tổng hợp để tắt hoặc đổi góc.";
        _hud.RefreshVisibility();
        if (hidePanel) Hide();
    }

    private void DisableHud()
    {
        _hudElapsed.Reset();
        _hudPlayer.Stop();
        _hudPlayer.Close();
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

    private void UpdateHudVoiceStatus()
    {
        var ready = InGameVoicePrompts.All.Count(_voice.IsReady);
        HudVoiceStatus.Text = ready == InGameVoicePrompts.All.Length
            ? "Đã lưu đủ giọng cảnh báo HUD bằng tiếng Việt; sẽ phát offline khi sự kiện xuất hiện."
            : $"Đã lưu {ready}/{InGameVoicePrompts.All.Length} câu cảnh báo HUD. Trong Companion nhấn Tạo giọng tiếng Việt miễn phí trước khi chơi.";
    }

    private void TestHudVoiceClick(object sender, RoutedEventArgs e)
    {
        if (!PlayHudVoice(InGameVoicePrompts.HealthLow, test: true))
            UpdateHudVoiceStatus();
    }

    /// <summary>Only plays a pre-generated Vietnamese file; never calls TTS over the network in a match.</summary>
    private bool PlayHudVoice(string phrase, bool urgent = false, bool test = false)
    {
        if (_closed || (!test && (HudVoiceCheck.IsChecked != true ||
            _hud is null || !_hud.IsVisible ||
            (_hud.IsDead && phrase != InGameVoicePrompts.Died))))
            return false;
        if (!_voice.IsReady(phrase))
        {
            HudVoiceStatus.Text = "Thiếu âm thanh cảnh báo HUD. Trong Companion nhấn Tạo giọng tiếng Việt miễn phí rồi Nghe thử; HUD chữ vẫn hoạt động.";
            return false;
        }

        var now = DateTime.UtcNow;
        if (!test && !urgent && now - _lastHudSpeechUtc < TimeSpan.FromSeconds(5)) return false;
        if (!test && phrase == _lastHudSpeechPhrase &&
            now - _lastHudSpeechUtc < TimeSpan.FromSeconds(10)) return false;
        try
        {
            _hudPlayer.Stop();
            _hudPlayer.Close();
            _hudPlayer.Open(new Uri(Path.GetFullPath(_voice.CachePath(phrase))));
            _hudPlayer.Volume = 0.7;
            _hudPlayer.Play();
            _lastHudSpeechPhrase = phrase;
            _lastHudSpeechUtc = now;
            if (test) HudVoiceStatus.Text = "Đang nghe thử tiếng Việt. Nếu không nghe thấy, kiểm tra âm lượng Windows và tệp âm thanh.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            HudVoiceStatus.Text = "Chưa phát được giọng tiếng Việt: " + ex.Message;
            return false;
        }
    }

    private async void CheckAiBridgeClick(object sender, RoutedEventArgs e)
    {
        AiBridgeStatus.Text = "Đang kiểm tra kết nối AI cá nhân trên máy...";
        try
        {
            var connected = await _bridge.CheckAsync(_cancel.Token);
            if (!_closed)
                AiBridgeStatus.Text = connected
                    ? "Đã kết nối API nội bộ · hiện chưa chạy mô hình AI."
                    : "Chưa kết nối: mở AI cá nhân ở http://127.0.0.1:5188 rồi thử lại.";
        }
        catch (OperationCanceledException) when (_closed || _cancel.IsCancellationRequested) { }
    }

    private async Task ShowContextNoticeAsync(string kind, double gameTime,
        double? healthPercent, string localMessage, bool priority = false)
    {
        var hud = _hud;
        if (hud is null || _closed || _life.IsDead ||
            gameTime < _latestLifeTransitionTime) return;
        var startedAt = DateTime.UtcNow;

        if (AiBridgeCheck.IsChecked != true)
        {
            if (hud.ShowNotice(localMessage, priority))
                PlayHudVoice(kind == "own-health-loss"
                    ? InGameVoicePrompts.HealthLoss : InGameVoicePrompts.CompletedKill, priority);
            return;
        }

        string? bridgeMessage = null;
        try
        {
            bridgeMessage = await _bridge.GetNoticeAsync(kind, gameTime,
                healthPercent, _cancel.Token);
        }
        catch (OperationCanceledException) when (_closed || _cancel.IsCancellationRequested)
        {
            return;
        }

        if (_closed || !ReferenceEquals(_hud, hud) || _life.IsDead ||
            gameTime < _latestLifeTransitionTime ||
            DateTime.UtcNow - startedAt > TimeSpan.FromSeconds(2)) return;
        if (hud.ShowNotice(bridgeMessage ?? localMessage, priority))
            PlayHudVoice(kind == "own-health-loss"
                ? InGameVoicePrompts.HealthLoss : InGameVoicePrompts.CompletedKill, priority);
    }

    private async void FetchNowClick(object sender, RoutedEventArgs e) => await ReadNowAsync();

    private void ResetSessionClick(object sender, RoutedEventArgs e)
    {
        _session.Reset();
        _dangerAnalyzer.Reset();
        _dangerStatusUntilGameTime = double.NegativeInfinity;
        DangerAnalysisStatus.Text = "Đã xóa thống kê nguy hiểm của phiên. Chờ các mẫu mới.";
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
            var lifeTransition = _life.Observe(snapshot);
            if (lifeTransition != OwnLifeTransition.None)
            {
                _latestLifeTransitionTime = snapshot.GameTimeSeconds;
                _personalAlerts.Reset();
                _healthChangeDetector.Reset();
                _dangerAnalyzer.ResetBaseline();
            }
            // Death is the highest priority: pin its notice, suppress stale warnings,
            // then clear it on confirmed respawn.
            _hud?.SetLifeState(_life.IsDead);
            if (lifeTransition == OwnLifeTransition.Died)
                PlayHudVoice(InGameVoicePrompts.Died, urgent: true);
            else if (lifeTransition == OwnLifeTransition.Respawned &&
                     _hud?.ShowNotice(InGameVoicePrompts.Respawned, priority: true) == true)
                PlayHudVoice(InGameVoicePrompts.Respawned, urgent: true);

            if (!_life.IsDead)
            {
                var personalWarnings = _personalAlerts.Observe(snapshot);
                var healthLoss = _healthChangeDetector.Observe(snapshot);
                // Always maintain an accurate local baseline while alive, even if
                // the user temporarily switches this optional HUD alert off.
                var danger = _dangerAnalyzer.Observe(snapshot);
                if (DangerAnalysisCheck.IsChecked != true)
                    DangerAnalysisStatus.Text = "Đã tắt lời cảnh báo nguy hiểm; số liệu phiên vẫn được tổng hợp.";
                else if (danger is not null)
                {
                    _dangerStatusUntilGameTime = snapshot.GameTimeSeconds + 7;
                    DangerAnalysisStatus.Text = danger.Message + " (quan sát vừa xảy ra)";
                }
                else if (lifeTransition == OwnLifeTransition.Respawned)
                {
                    _dangerStatusUntilGameTime = double.NegativeInfinity;
                    DangerAnalysisStatus.Text = "Đã hồi sinh: đang xây dựng đường cơ sở máu mới.";
                }
                else if (snapshot.GameTimeSeconds > _dangerStatusUntilGameTime)
                    DangerAnalysisStatus.Text = "Không có cảnh báo nguy hiểm mới trong các mẫu vừa đọc; không suy ra an toàn.";

                if (_hud is not null && lifeTransition != OwnLifeTransition.Respawned)
                {
                    // One useful warning per observation. A danger observation is
                    // always more urgent than generic damage, mana or gold notices.
                    if (danger is not null && DangerAnalysisCheck.IsChecked == true)
                    {
                        var level = danger.Severity switch
                        {
                            OwnDangerSeverity.Critical => 5,
                            OwnDangerSeverity.High => 4,
                            _ => 3
                        };
                        if (_hud.ShowNotice(danger.Message, priority: true, dangerPriority: level))
                        {
                            var voice = danger.Severity switch
                            {
                                OwnDangerSeverity.Critical => InGameVoicePrompts.DangerCritical,
                                OwnDangerSeverity.High => InGameVoicePrompts.DangerHigh,
                                _ => InGameVoicePrompts.DangerElevated
                            };
                            PlayHudVoice(voice, urgent: true);
                        }
                    }
                    else if (healthLoss is not null && HealthChangeCheck.IsChecked == true)
                        _ = ShowContextNoticeAsync("own-health-loss",
                            snapshot.GameTimeSeconds, snapshot.HealthPercent,
                            healthLoss, priority: true);
                    else if (personalWarnings.Count > 0 &&
                             _hud.ShowNotice(string.Join(" ", personalWarnings), priority: true))
                    {
                        if (personalWarnings.Any(w => w.StartsWith("Máu", StringComparison.Ordinal)))
                            PlayHudVoice(InGameVoicePrompts.HealthLow, urgent: true);
                        else if (personalWarnings.Any(w => w.StartsWith("Năng lượng", StringComparison.Ordinal)))
                            PlayHudVoice(InGameVoicePrompts.ManaLow);
                        else if (personalWarnings.Any(w => w.StartsWith("Vàng", StringComparison.Ordinal)))
                            PlayHudVoice(InGameVoicePrompts.GoldHigh);
                    }
                }
            }
            else
                DangerAnalysisStatus.Text = "Bạn đã bị hạ gục; tạm dừng phân tích các đợt giảm máu.";

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
                $"Lượng vàng hiện có cao nhất ghi nhận: {_session.HighestObservedGold:0}.\n" +
                $"Các đợt giảm máu gây cảnh báo đã quan sát: {_dangerAnalyzer.ObservedDangerEpisodes} " +
                $"(nguy hiểm cao: {_dangerAnalyzer.ObservedCriticalEpisodes}).\n" +
                $"Mức máu mất lớn nhất trong một khoảng lấy mẫu liên tiếp có cảnh báo: " +
                $"{_dangerAnalyzer.GreatestObservedLossPercent:0}% máu tối đa.";
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
            if (message is not null)
                await ShowContextNoticeAsync("completed-kill", gameTime,
                    null, message);
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
        _hudPlayer.Stop();
        _hudPlayer.Close();
        _local.Dispose();
        _bridge.Dispose();
        _voice.Dispose();
        _cancel.Dispose();
    }
}
