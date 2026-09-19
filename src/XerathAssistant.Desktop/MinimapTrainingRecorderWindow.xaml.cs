using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Threading;

namespace XerathAssistant.Desktop;

/// <summary>
/// Opt-in, LOCAL training-data collection. Samples only the visible bottom-right
/// minimap region of the foreground League window during practice/replay.
/// No neural model, tracking, background desktop recording or live HUD advice.
/// </summary>
public partial class MinimapTrainingRecorderWindow : Window
{
    private const int MaximumSamples = 120;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "minimap-training");
    private string? _session;
    private int _count;
    private bool _running;

    public MinimapTrainingRecorderWindow()
    {
        InitializeComponent();
        _timer.Tick += CaptureTick;
        FolderText.Text = "Nơi lưu: " + _root +
            "\nChỉ lưu ảnh minimap; không gửi dữ liệu ra mạng và không nhận diện đối thủ.";
    }

    private void StartClick(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        var answer = MessageBox.Show(this,
            "Bạn đồng ý lưu ảnh minimap của cửa sổ Liên Minh đang hiển thị trên máy " +
            "mỗi 3 giây (tối đa 120 ảnh) để tự xem lại hoặc chuẩn bị dữ liệu kiểm thử AI? " +
            "Không gửi ảnh lên máy chủ hay hiện cảnh báo vị trí rừng địch trong trận.",
            "Cho phép thu ảnh luyện tập?", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            Directory.CreateDirectory(_root);
            _session = Path.Combine(_root,
                $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_session);
            _count = 0;
            _running = true;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "Đang chờ bạn chuyển sang cửa sổ trận Liên Minh; " +
                "ảnh chỉ được lưu khi trò chơi là cửa sổ được chọn.";
            _timer.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Không thể tạo thư mục ảnh: " + ex.Message;
        }
    }

    private void StopClick(object sender, RoutedEventArgs e) => StopRecording();

    private void StopRecording()
    {
        _timer.Stop();
        _running = false;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = $"Đã dừng. Đã lưu {_count} ảnh minimap vào: {_session ?? "chưa có thư mục"}";
    }

    private void CaptureTick(object? sender, EventArgs e)
    {
        if (!_running || _session is null) return;
        if (_count >= MaximumSamples)
        {
            StopRecording();
            return;
        }

        // Do not capture an unrelated foreground window, even if LoL is running
        // in the background. All captured pixels come from its visible client area.
        if (!TryGetForegroundGameClient(out var client))
        {
            StatusText.Text = $"Đã lưu {_count} ảnh. Đang chờ cửa sổ Liên Minh được chọn...";
            return;
        }

        // Percentage-based region is an uncalibrated, bounded training crop, not
        // a detector. A future model must calibrate the actual minimap boundaries.
        var region = new Rectangle(
            client.Left + (int)(client.Width * 0.78),
            client.Top + (int)(client.Height * 0.70),
            Math.Max(1, (int)(client.Width * 0.22)),
            Math.Max(1, (int)(client.Height * 0.30)));
        region.Intersect(client);
        region.Intersect(System.Windows.Forms.SystemInformation.VirtualScreen);
        if (region.Width < 90 || region.Height < 90)
        {
            StatusText.Text = "Cửa sổ trò chơi quá nhỏ hoặc không hiển thị đủ minimap.";
            return;
        }

        try
        {
            using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(region.Location, System.Drawing.Point.Empty,
                    region.Size, CopyPixelOperation.SourceCopy);
            }
            var filename = Path.Combine(_session, $"minimap_{++_count:000}_{DateTime.UtcNow:HHmmss}.jpg");
            bitmap.Save(filename, ImageFormat.Jpeg);
            StatusText.Text = $"Đã lưu {_count}/{MaximumSamples} ảnh minimap từ cửa sổ game. " +
                "Đây là dữ liệu thô để xem lại, chưa có AI nhận diện rừng địch.";
            if (_count >= MaximumSamples) StopRecording();
        }
        catch (Exception ex) when (ex is ExternalException or IOException or UnauthorizedAccessException
                                   or ArgumentException)
        {
            StopRecording();
            StatusText.Text = "Đã dừng vì không thể lưu ảnh: " + ex.Message;
        }
    }

    private void OpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_root);
            var folder = _session is not null && Directory.Exists(_session) ? _session : _root;
            Process.Start(new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
                ArgumentList = { folder }
            });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "Không mở được thư mục ảnh: " + ex.Message;
        }
    }

    private static bool TryGetForegroundGameClient(out Rectangle client)
    {
        client = Rectangle.Empty;
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(window, out var pid);
        if (pid == 0) return false;
        try
        {
            using var process = Process.GetProcessById(unchecked((int)pid));
            if (!string.Equals(process.ProcessName, "League of Legends",
                StringComparison.OrdinalIgnoreCase))
                return false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                   or System.ComponentModel.Win32Exception)
        {
            return false;
        }

        if (!GetClientRect(window, out var rect)) return false;
        var topLeft = new NativePoint { X = 0, Y = 0 };
        if (!ClientToScreen(window, ref topLeft)) return false;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 640 || height < 400) return false;
        client = new Rectangle(topLeft.X, topLeft.Y, width, height);
        return true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _running = false;
        base.OnClosed(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
}
