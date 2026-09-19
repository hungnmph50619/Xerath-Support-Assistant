using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

public partial class CompanionWindow : Window
{
    private const string MapPhrase = "Nhìn minimap, kiểm tra vị trí đồng đội và đối thủ.";
    private const string VisionPhrase = "Kiểm tra mắt, máy quét và tầm nhìn ở sông.";
    private const string JunglePhrase = "Kiểm tra minimap xem rừng đồng minh đang ở đâu.";
    private const string AdcPhrase = "Kiểm tra vị trí ADC và khả năng hỗ trợ.";
    private const string ManaPhrase = "Kiểm tra năng lượng, kỹ năng E và vị trí đứng của Xerath.";
    private const string ObjectivePhrase = "Kiểm tra đồng hồ mục tiêu lớn và tầm nhìn khu vực.";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _elapsed = new();
    private readonly ReminderEngine _reminders = new(TimeSpan.FromSeconds(30));
    private readonly FptVietnameseVoiceService _voice = new();
    private readonly MediaPlayer _player = new() { Volume = 0.7 };
    private readonly Queue<string> _audioQueue = new();
    private CancellationTokenSource? _preparation;
    private bool _started;
    private bool _paused;
    private bool _isMatchRunning;
    private bool _closed;

    public CompanionWindow()
    {
        InitializeComponent();
        _player.MediaEnded += (_, _) => FinishAudioAndPlayNext();
        _player.MediaFailed += (_, args) =>
        {
            VoiceStatus.Text = "Không phát được âm thanh tiếng Việt: " +
                (args.ErrorException?.Message ?? "Định dạng âm thanh không được hỗ trợ.");
            FinishAudioAndPlayNext();
        };
        _timer.Tick += TimerTick;
        _timer.Start();
        UpdateGameStatus();
        RefreshVoiceReadiness();
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
            items.Add(new("map", MapPhrase, TimeSpan.FromSeconds(45)));
        if (VisionCheck.IsChecked == true)
            items.Add(new("vision", VisionPhrase, TimeSpan.FromMinutes(3)));
        if (JungleCheck.IsChecked == true)
            items.Add(new("jungle", JunglePhrase, TimeSpan.FromSeconds(90)));
        if (AdcCheck.IsChecked == true)
            items.Add(new("adc", AdcPhrase, TimeSpan.FromMinutes(2)));
        if (ManaCheck.IsChecked == true)
            items.Add(new("mana", ManaPhrase, TimeSpan.FromSeconds(150)));
        if (ObjectiveCheck.IsChecked == true)
            items.Add(new("objective", ObjectivePhrase, TimeSpan.FromMinutes(3)));
        return items.ToArray();
    }

    private string[] SelectedPhrases() => SelectedReminders().Select(x => x.Message).ToArray();

    private bool AllVoicesReady() => SelectedPhrases().All(_voice.IsReady);

    private void RefreshVoiceReadiness()
    {
        var phrases = SelectedPhrases();
        var ready = phrases.Count(_voice.IsReady);
        TestVoiceButton.IsEnabled = _voice.IsReady(MapPhrase);
        if (!_started && _preparation is null)
            VoiceStatus.Text = $"Giọng AI Ban Mai (nữ miền Bắc): đã lưu {ready}/{phrases.Length} lời nhắc. " +
                (ready == phrases.Length && ready > 0
                    ? "Có thể phát offline."
                    : "Nhập API key FPT.AI, bấm Tạo và lưu giọng tiếng Việt.");
    }

    private async void PrepareVoiceClick(object sender, RoutedEventArgs e)
    {
        if (_preparation is not null) return;
        var phrases = SelectedPhrases();
        if (phrases.Length == 0)
        {
            MessageBox.Show(this, "Hãy chọn ít nhất một nhóm nhắc.", "Chưa chọn lời nhắc");
            return;
        }
        var key = ApiKeyBox.Password;
        if (phrases.Any(p => !_voice.IsReady(p)) && string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show(this, "Nhập API key FPT.AI vào ô mật khẩu. Không gửi API key vào ChatGPT hay GitHub.",
                "Thiếu API key");
            return;
        }
        using var preparation = new CancellationTokenSource();
        _preparation = preparation;
        PrepareVoiceButton.IsEnabled = false;
        try
        {
            for (var i = 0; i < phrases.Length; i++)
            {
                VoiceStatus.Text = $"Đang chuẩn bị giọng nữ miền Bắc: {i + 1}/{phrases.Length}...";
                await _voice.PrepareAsync(phrases[i], key, preparation.Token);
            }
            ApiKeyBox.Clear(); // Never persist API credentials.
            VoiceStatus.Text = "Đã tạo giọng nữ miền Bắc cho tất cả lời nhắc. Có thể chơi offline.";
            TestVoiceButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            if (!_closed) VoiceStatus.Text = "Đã hủy chuẩn bị âm thanh.";
        }
        catch (Exception ex)
        {
            if (!_closed) VoiceStatus.Text = "Không tạo được giọng AI: " + ex.Message;
        }
        finally
        {
            _preparation = null;
            if (!_closed) PrepareVoiceButton.IsEnabled = true;
        }
    }

    private void TestVoiceClick(object sender, RoutedEventArgs e)
    {
        if (!_voice.IsReady(MapPhrase)) return;
        PlayVoice(new[] { MapPhrase });
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
        if (VoiceCheck.IsChecked == true && !AllVoicesReady())
        {
            MessageBox.Show(this,
                "Chưa tạo đủ giọng AI tiếng Việt cho các mục nhắc đã chọn. " +
                "Nhập API key và nhấn 'Tạo và lưu giọng tiếng Việt', hoặc bỏ chọn giọng nói để chỉ hiển thị thông báo. " +
                "Ứng dụng KHÔNG tự chuyển sang giọng tiếng Anh.",
                "Chưa có âm thanh tiếng Việt");
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
            StopVoice();
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
        StopVoice();
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
        PrepareVoiceButton.IsEnabled = enabled && _preparation is null;
    }

    private void TimerTick(object? sender, EventArgs e)
    {
        UpdateGameStatus();
        if (!_started || _paused) return;
        SessionStatus.Text = "Đang nhắc việc. Thời gian: " + _elapsed.Elapsed.ToString(@"hh\:mm\:ss");
        var message = _reminders.Tick(_elapsed.Elapsed);
        if (message is null) return;
        LastReminder.Text = "Lời nhắc gần nhất: " + message.Replace(" | ", " ");
        if (VoiceCheck.IsChecked == true)
            PlayVoice(message.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private void PlayVoice(IEnumerable<string> phrases)
    {
        StopVoice();
        foreach (var phrase in phrases)
            if (_voice.IsReady(phrase)) _audioQueue.Enqueue(_voice.CachePath(phrase));
        PlayNext();
    }

    private void PlayNext()
    {
        if (_audioQueue.Count == 0) return;
        _player.Open(new Uri(Path.GetFullPath(_audioQueue.Dequeue())));
        _player.Volume = 0.7;
        _player.Play();
    }

    private void FinishAudioAndPlayNext()
    {
        _player.Stop();
        _player.Close();
        PlayNext();
    }

    private void StopVoice()
    {
        _audioQueue.Clear();
        _player.Stop();
        _player.Close();
    }

    private void OpenLabClick(object sender, RoutedEventArgs e) => new MainWindow().Show();

    private void OpenLastSeenReviewClick(object sender, RoutedEventArgs e) =>
        new LastSeenReviewWindow { Owner = this }.Show();

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _preparation?.Cancel();
        _timer.Stop();
        _elapsed.Stop();
        StopVoice();
        _voice.Dispose();
        base.OnClosed(e);
    }
}
