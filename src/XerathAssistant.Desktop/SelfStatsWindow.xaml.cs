using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

public partial class SelfStatsWindow : Window
{
    private readonly RiotLocalSelfStatsClient _local = new();
    private readonly SelfStatsSession _session = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly CancellationTokenSource _cancel = new();
    private bool _fetching;
    private bool _closed;

    public SelfStatsWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await ReadNowAsync();
    }

    private async void WindowLoaded(object sender, RoutedEventArgs e)
    {
        _timer.Start();
        await ReadNowAsync();
    }

    private void RefreshChanged(object sender, RoutedEventArgs e)
    {
        if (_timer is null) return;
        if (RefreshCheck?.IsChecked == true && !_closed) _timer.Start();
        else _timer.Stop();
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   or FormatException or InvalidOperationException
                                   or System.Text.Json.JsonException or KeyNotFoundException)
        {
            if (!_closed)
                ConnectionStatus.Text = "Chưa đọc được thông tin trong trận từ API Riot. " +
                    "Hãy vào trận rồi nhấn Cập nhật ngay. Chỉ số bên dưới (nếu có) là dữ liệu lần đọc trước.";
        }
        finally { _fetching = false; }
    }

    private void WindowClosed(object sender, EventArgs e)
    {
        _closed = true;
        _timer.Stop();
        _cancel.Cancel();
        _local.Dispose();
        _cancel.Dispose();
    }
}
