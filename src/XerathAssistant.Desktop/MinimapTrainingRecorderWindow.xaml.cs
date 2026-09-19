using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace XerathAssistant.Desktop;

/// <summary>
/// Opt-in minimap still-frame REVIEW during a practice match. Pixels and JPEG bytes
/// are never written to a file. One frame at a time is sent to the user's configured
/// PersonalAI local endpoint, which forwards it to Gemini. Not suitable for live HUD
/// advice, opponent tracking or automatic role inference.
/// </summary>
public partial class MinimapTrainingRecorderWindow : Window
{
    private const int MaximumFramesPerSession = 45;
    private const int MaximumImageBytes = 2 * 1024 * 1024;
    private readonly DispatcherTimer _timer = new();
    private readonly HttpClient _visionClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:5188/"),
        Timeout = TimeSpan.FromSeconds(45)
    };
    private readonly string _legacyRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "minimap-training");
    private CancellationTokenSource? _sessionCancellation;
    private DateTime _lastGameFocusedUtc;
    private int _sent;
    private bool _running;
    private bool _busy;
    private bool _closed;

    public MinimapTrainingRecorderWindow()
    {
        InitializeComponent();
        _timer.Tick += CaptureTick;
        FolderText.Text = "Ảnh mới chỉ nằm trong bộ nhớ trong lúc xử lý, KHÔNG ghi ra ổ đĩa. " +
            "Thư mục ảnh của phiên bản cũ: " + _legacyRoot;
    }

    private static bool IsGameRunning()
    {
        try
        {
            using var game = Process.GetProcessesByName("League of Legends").FirstOrDefault();
            return game is not null && !game.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or
                                   System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private void StartClick(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        if (!IsGameRunning())
        {
            StatusText.Text = "Chưa phát hiện trận Liên Minh. Hãy vào trận luyện tập trước khi bật phân tích.";
            return;
        }
        var seconds = IntervalBox.SelectedIndex switch { 0 => 30, 2 => 120, _ => 60 };
        var answer = MessageBox.Show(this,
            $"Bạn đồng ý cho ứng dụng tự động chụp phần minimap đang HIỂN THỊ của cửa sổ game được chọn, " +
            $"và GỬI TỐI ĐA {MaximumFramesPerSession} ảnh (mỗi {seconds} giây) tới AI cá nhân rồi " +
            "tới Google Gemini để phân tích trong phiên luyện tập này? " +
            "Mỗi ảnh sẽ được giải phóng khỏi bộ nhớ sau xử lý, không có tệp ảnh mới được lưu trên máy. " +
            "Gemini là dịch vụ bên ngoài: có thể sử dụng hạn mức/phát sinh chi phí và có chính sách " +
            "xử lý dữ liệu riêng. Kết quả KHÔNG dùng để báo vị trí địch hoặc phát lời nhắc trong game. " +
            "Bạn có thể bấm Dừng bất cứ lúc nào.",
            "Xác nhận gửi ảnh minimap trong một phiên luyện tập",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        _sessionCancellation?.Dispose();
        _sessionCancellation = new CancellationTokenSource();
        _sent = 0;
        _running = true;
        _lastGameFocusedUtc = DateTime.UtcNow;
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        IntervalBox.IsEnabled = false;
        AnalysisText.Text = "Đang chờ ảnh minimap đầu tiên. Kết quả chỉ tồn tại trong phiên hiện tại.";
        StatusText.Text = $"Đang theo dõi cửa sổ game được chọn · tối đa {MaximumFramesPerSession} ảnh · " +
            $"mỗi {seconds} giây. Không lưu ảnh trên ổ đĩa.";
    }

    private void StopClick(object sender, RoutedEventArgs e) =>
        StopRecording("Đã dừng. Đã giải phóng ảnh trong bộ nhớ của phiên này.");

    private void StopRecording(string reason)
    {
        _timer.Stop();
        _running = false;
        _sessionCancellation?.Cancel();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        IntervalBox.IsEnabled = true;
        if (!_closed)
        {
            StatusText.Text = $"{reason} Số ảnh đã gửi trong phiên: {_sent}. " +
                "Ứng dụng không tạo tệp ảnh mới.";
            AnalysisText.Text = "Đã xóa kết quả phiên khỏi giao diện; ảnh không được lưu vào tệp.";
        }
    }

    private async void CaptureTick(object? sender, EventArgs e)
    {
        if (!_running || _busy || _closed) return;
        if (!IsGameRunning())
        {
            StopRecording("Trận đã đóng; ứng dụng tự dừng.");
            return;
        }
        if (!TryGetForegroundGameClient(out var client))
        {
            if (DateTime.UtcNow - _lastGameFocusedUtc > TimeSpan.FromMinutes(3))
                StopRecording("Cửa sổ game không được chọn quá 3 phút; ứng dụng tự dừng.");
            else
                StatusText.Text = "Tạm dừng chụp vì cửa sổ game không được chọn. " +
                    "Ứng dụng không đọc nội dung của cửa sổ khác.";
            return;
        }
        _lastGameFocusedUtc = DateTime.UtcNow;
        if (_sent >= MaximumFramesPerSession)
        {
            StopRecording("Đã đạt giới hạn ảnh/phiên; tự dừng để tránh tăng chi phí.");
            return;
        }

        var region = new Rectangle(
            client.Left + (int)(client.Width * 0.78),
            client.Top + (int)(client.Height * 0.70),
            Math.Max(1, (int)(client.Width * 0.22)),
            Math.Max(1, (int)(client.Height * 0.30)));
        region.Intersect(client);
        region.Intersect(System.Windows.Forms.SystemInformation.VirtualScreen);
        if (region.Width < 90 || region.Height < 90)
        {
            StatusText.Text = "Minimap nằm ngoài vùng hiển thị hoặc cửa sổ quá nhỏ.";
            return;
        }

        _busy = true; // At most one image and one cloud request in flight.
        var cancellationToken = _sessionCancellation!.Token;
        try
        {
            // Pixels and compressed JPEG stay in short-lived RAM buffers only.
            using var bitmap = new Bitmap(region.Width, region.Height,
                PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(region.Location, System.Drawing.Point.Empty,
                    region.Size, CopyPixelOperation.SourceCopy);
            }
            using var jpeg = new MemoryStream();
            bitmap.Save(jpeg, ImageFormat.Jpeg);
            if (jpeg.Length is < 24 or > MaximumImageBytes)
            {
                StatusText.Text = "Ảnh minimap vượt giới hạn 2 MB hoặc không hợp lệ; không gửi ảnh.";
                return;
            }
            jpeg.Position = 0;
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("true"), "confirmed");
            var imageContent = new StreamContent(jpeg);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(imageContent, "image", "minimap.jpg");

            StatusText.Text = $"Đang phân tích ảnh thứ {_sent + 1}/{MaximumFramesPerSession}. " +
                "Không có tệp ảnh mới được lưu.";
            using var response = await _visionClient.PostAsync(
                "api/vision/minimap/analyze", form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!_running || _closed || cancellationToken.IsCancellationRequested) return;

            if (!response.IsSuccessStatusCode)
            {
                string message;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    message = doc.RootElement.TryGetProperty("error", out var error)
                        ? error.GetString() ?? "Không có lời giải thích." : "Không có lời giải thích.";
                }
                catch (JsonException) { message = "Không có lời giải thích."; }
                StopRecording($"AI chưa xử lý được ảnh (HTTP {(int)response.StatusCode}): " +
                    message[..Math.Min(180, message.Length)]);
                return;
            }
            using var result = JsonDocument.Parse(body);
            if (!result.RootElement.TryGetProperty("analysis", out var analysis) ||
                analysis.ValueKind != JsonValueKind.String ||
                !result.RootElement.TryGetProperty("suitableForLiveHud", out var liveHud) ||
                liveHud.ValueKind != JsonValueKind.False)
            {
                StopRecording("AI cá nhân trả dữ liệu không phù hợp cho chế độ xem lại.");
                return;
            }

            _sent++;
            // Discard the previous text; keep no screenshot, replay log or video.
            AnalysisText.Text = analysis.GetString() ?? "Không có kết quả.";
            AnalysisNote.Text = "Kết quả từ một ảnh minimap vừa lấy, chưa được kiểm chứng " +
                $"(mốc máy: {DateTime.Now:HH:mm:ss}). Không phải thông tin vị trí hiện tại đáng tin cậy.";
            StatusText.Text = $"Đã nhận phân tích {_sent}/{MaximumFramesPerSession}. " +
                "Ảnh vừa xử lý được giải phóng khi yêu cầu kết thúc; không ghi JPG/PNG ra đĩa.";
            if (_sent >= MaximumFramesPerSession)
                StopRecording("Đã đạt giới hạn ảnh/phiên; tự dừng để tránh tăng chi phí.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped by user or by game closing. Never retry the old frame.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or
                                   IOException or ExternalException or JsonException or
                                   ArgumentException or InvalidOperationException)
        {
            if (_running && !_closed)
                StopRecording("Không thể phân tích ảnh (kiểm tra AI cá nhân/Gemini): " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void DeleteOldImagesClick(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_legacyRoot))
        {
            FolderText.Text = "Không thấy thư mục ảnh minimap cũ để xóa.";
            return;
        }
        if (MessageBox.Show(this,
                "Xóa vĩnh viễn chỉ các tệp minimap_*.jpg nằm trong các thư mục phiên cũ của Xerath " +
                "Support Assistant? Ảnh bạn lưu từ bản trước sẽ không thể khôi phục bằng nút này. " +
                "Ảnh mới của chế độ đang chạy không nằm trên đĩa.",
                "Xác nhận xóa ảnh minimap cũ",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        var deleted = 0;
        try
        {
            foreach (var session in Directory.EnumerateDirectories(_legacyRoot))
            {
                var directory = new DirectoryInfo(session);
                if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                foreach (var image in directory.EnumerateFiles("minimap_*.jpg",
                             SearchOption.TopDirectoryOnly))
                {
                    if (image.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    image.Delete();
                    deleted++;
                }
                if (!directory.EnumerateFileSystemInfos().Any()) directory.Delete();
            }
            FolderText.Text = $"Đã xóa {deleted} tệp JPG minimap cũ có tên minimap_*.jpg. " +
                "Các tệp không khớp tên hoặc nằm ở thư mục khác vẫn được giữ nguyên.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FolderText.Text = $"Đã xóa {deleted} tệp; một số tệp chưa xóa được: {ex.Message}";
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
        _closed = true;
        StopRecording("Cửa sổ đã đóng.");
        _sessionCancellation?.Dispose();
        _visionClient.Dispose();
        base.OnClosed(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

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
