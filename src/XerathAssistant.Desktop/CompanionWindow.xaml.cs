using System.Diagnostics;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Threading;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

public partial class CompanionWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _elapsed = new();
    private readonly ReminderEngine _reminders = new(TimeSpan.FromSeconds(30));
    private SpeechSynthesizer? _speech;
    private bool _started;
    private bool _paused;
    private bool _isMatchRunning;

    public CompanionWindow()
    {
        InitializeComponent();
        try
        {
            _speech = new SpeechSynthesizer { Volume = 70 };
            var vietnamese = _speech.GetInstalledVoices()
                .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name
                    .StartsWith("vi", StringComparison.OrdinalIgnoreCase));
            if (vietnamese is not null)
            {
                _speech.SelectVoice(vietnamese.VoiceInfo.Name);
                VoiceStatus.Text = "Đã chọn giọng tiếng Việt: " + vietnamese.VoiceInfo.Name;
            }
            else
            {
                VoiceStatus.Text = "Windows chưa có giọng Việt; có thể đọc không chuẩn. Cài thêm giọng tiếng Việt trong Windows.";
            }
        }
        catch (Exception ex)
        {
            VoiceCheck.IsChecked = false;
            VoiceCheck.IsEnabled = false;
            VoiceStatus.Text = "Không khởi tạo được giọng đọc: " + ex.Message;
            _speech?.Dispose();
            _speech = null;
        }

        _timer.Tick += TimerTick;
        _timer.Start();
        UpdateGameStatus();
    }

    private static bool ProcessPresent(string name)
    {
        try
        {
            var matches = Process.GetProcessesByName(name);
            try { return matches.Length > 0; }
            finally { foreach (var process in matches) process.Dispose(); }
        }
        catch { return false; }
    }

    private void UpdateGameStatus()
    {
        _isMatchRunning = ProcessPresent("League of Legends");
        GameStatus.Text = _isMatchRunning ? "Đã phát hiện tiến trình trận đấu Liên Minh."
            : ProcessPresent("LeagueClientUx") || ProcessPresent("LeagueClient")
                ? "Đã phát hiện client Liên Minh; chưa phát hiện tiến trình trận đấu."
                : "Chưa phát hiện Liên Minh. Hãy mở game trước khi chơi.";
    }

    private ReminderItem[] SelectedReminders()
    {
        var items = new List<ReminderItem>();
        if (MapCheck.IsChecked == true)
            items.Add(new("map", "Nhìn minimap, kiểm tra vị trí đồng đội và đối thủ.", TimeSpan.FromSeconds(45)));
        if (VisionCheck.IsChecked == true)
            items.Add(new("vision", "Kiểm tra mắt, máy quét và tầm nhìn ở sông.", TimeSpan.FromMinutes(3)));
        if (JungleCheck.IsChecked == true)
            items.Add(new("jungle", "Kiểm tra minimap xem rừng đồng minh đang ở đâu.", TimeSpan.FromSeconds(90)));
        if (AdcCheck.IsChecked == true)
            items.Add(new("adc", "Kiểm tra vị trí ADC và khả năng hỗ trợ.", TimeSpan.FromMinutes(2)));
        if (ManaCheck.IsChecked == true)
            items.Add(new("mana", "Kiểm tra năng lượng, kỹ năng E và vị trí đứng của Xerath.", TimeSpan.FromSeconds(150)));
        if (ObjectiveCheck.IsChecked == true)
            items.Add(new("objective", "Kiểm tra đồng hồ mục tiêu lớn và tầm nhìn khu vực.", TimeSpan.FromMinutes(3)));
        return items.ToArray();
    }

    private void StartClick(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        var items = SelectedReminders();
        if (items.Length == 0)
        {
            MessageBox.Show(this, "Hãy chọn ít nhất một nhóm nhắc.", "Chưa chọn lời nhắc");
            return;
        }
        UpdateGameStatus();
        if (!_isMatchRunning)
        {
            var answer = MessageBox.Show(this,
                "Chưa phát hiện tiến trình trận đấu. Bạn có thể mở Liên Minh và vào trận trước. Vẫn chạy bộ nhắc độc lập ngay bây giờ?",
                "Xác nhận bắt đầu", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }
        _reminders.Start(items);
        _elapsed.Restart();
        _started = true;
        _paused = false;
        StartButton.IsEnabled = false;
        PauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
        SetOptionsEnabled(false);
        SessionStatus.Text = "Đang nhắc việc. Thời gian: 00:00.";
        LastReminder.Text = "Lời nhắc gần nhất: chưa có.";
    }

    private void PauseClick(object sender, RoutedEventArgs e)
    {
        if (!_started) return;
        _paused = !_paused;
        if (_paused)
        {
            _elapsed.Stop();
            CancelSpeech();
        }
        else _elapsed.Start();
        PauseButton.Content = _paused ? "Tiếp tục" : "Tạm dừng";
        SessionStatus.Text = _paused ? "Đã tạm dừng lời nhắc." : "Đang tiếp tục nhắc việc.";
    }

    private void StopClick(object sender, RoutedEventArgs e)
    {
        _started = false;
        _paused = false;
        _elapsed.Reset();
        CancelSpeech();
        StartButton.IsEnabled = true;
        PauseButton.IsEnabled = false;
        PauseButton.Content = "Tạm dừng";
        StopButton.IsEnabled = false;
        SetOptionsEnabled(true);
        SessionStatus.Text = "Đã kết thúc phiên chơi.";
    }

    private void SetOptionsEnabled(bool enabled)
    {
        MapCheck.IsEnabled = VisionCheck.IsEnabled = JungleCheck.IsEnabled =
            AdcCheck.IsEnabled = ManaCheck.IsEnabled = ObjectiveCheck.IsEnabled = enabled;
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        UpdateGameStatus();
        if (!_started || _paused) return;
        SessionStatus.Text = "Đang nhắc việc. Thời gian: " + _elapsed.Elapsed.ToString(@"hh\:mm\:ss");
        var message = _reminders.Tick(_elapsed.Elapsed);
        if (message is null) return;
        LastReminder.Text = "Lời nhắc gần nhất: " + message;
        if (VoiceCheck.IsChecked != true || _speech is null) return;
        try
        {
            // Drop any stale unfinished speech instead of creating a growing notification queue.
            _speech.SpeakAsyncCancelAll();
            _speech.SpeakAsync(message);
        }
        catch (Exception ex)
        {
            VoiceStatus.Text = "Không phát được giọng nói: " + ex.Message;
        }
    }

    private void CancelSpeech()
    {
        try { _speech?.SpeakAsyncCancelAll(); } catch { /* Speech engine can be unavailable. */ }
    }

    private void OpenLabClick(object sender, RoutedEventArgs e) => new MainWindow().Show();

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _elapsed.Stop();
        CancelSpeech();
        _speech?.Dispose();
        base.OnClosed(e);
    }
}
