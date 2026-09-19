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
using System.Windows.Input;
using System.Windows.Media.Imaging;
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
    private readonly MinimapSampleStore _sampleStore = new();
    private sealed record SequenceFrame(byte[] Jpeg, DateTimeOffset TakenAt);
    private readonly List<SequenceFrame> _sequenceFrames = new();
    private readonly DispatcherTimer _sequenceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private DateTime _sequenceUntilUtc;
    private string? _sequenceGroup;
    private string? _currentSequenceGroup;
    private int? _currentSequenceIndex;
    private DateTimeOffset? _currentFrameTakenAt;
    private readonly MinimapCropProfileStore _cropStore = new();
    private MinimapCropProfile _cropProfile = MinimapCropProfile.Default;
    private MinimapCropProfile? _previewProfile;
    private Rectangle _previewClient;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DateTime _previewUntilUtc;
    private bool _autoDetectPreview;
    private bool _suppressCropSliderChanged;
    private byte[]? _selectedFrame;
    private double? _markX;
    private double? _markY;
    private readonly List<MinimapChampionMark> _marks = new();
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
        _previewTimer.Tick += PreviewCropTick;
        _sequenceTimer.Tick += CaptureSequenceTick;
        _cropProfile = _cropStore.Load();
        ApplyCropToSliders(_cropProfile);
        CropStatusText.Text = _cropProfile.ConfirmedClientWidth > 0
            ? $"Đã lưu khung cho cửa sổ game {_cropProfile.ConfirmedClientWidth}×{_cropProfile.ConfirmedClientHeight}. " +
              "Nếu đổi kích thước game hoặc vị trí minimap, hãy xem trước và xác nhận lại."
            : "Chưa có vùng minimap được kiểm tra. Hãy xem trước và xác nhận khung trước khi gửi ảnh Gemini.";
        FolderText.Text = "Ảnh phân tích mặc định không lưu. Chỉ mẫu bạn chọn mới ghi vào: " +
            _sampleStore.Folder + ". Ảnh từ phiên bản cũ: " + _legacyRoot;
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
        if (_sequenceTimer.IsEnabled || _sequenceFrames.Count > 0)
        {
            StatusText.Text = "Hãy kết thúc và xóa chuỗi ảnh RAM trước khi bắt đầu gửi ảnh tới Gemini.";
            return;
        }
        if (_previewTimer.IsEnabled)
        {
            StatusText.Text = "Hãy chờ ảnh xem trước, kiểm tra và lưu khung minimap trước.";
            return;
        }
        if (_cropProfile.ConfirmedClientWidth < 640 ||
            !SameCrop(CurrentCropDraft(), _cropProfile))
        {
            StatusText.Text = "Chưa xác nhận vùng cắt minimap hoặc thanh trượt đã thay đổi. " +
                "Bấm Xem trước, chuyển về game rồi xác nhận Lưu khung trước khi phân tích.";
            return;
        }
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
        ClearPendingFrame();
        _running = true;
        _lastGameFocusedUtc = DateTime.UtcNow;
        _timer.Interval = TimeSpan.FromSeconds(seconds);
        _timer.Start();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        IntervalBox.IsEnabled = false;
        SetCropControls(false);
        AnalysisText.Text = "Đang chờ ảnh minimap đầu tiên. Kết quả chỉ tồn tại trong phiên hiện tại.";
        StatusText.Text = $"Đang theo dõi cửa sổ game được chọn · tối đa {MaximumFramesPerSession} ảnh · " +
            $"mỗi {seconds} giây. Không lưu ảnh trên ổ đĩa.";
    }

    private void StopClick(object sender, RoutedEventArgs e) =>
        StopRecording("Đã dừng. Đã giải phóng ảnh trong bộ nhớ của phiên này.");

    private void StopRecording(string reason)
    {
        _timer.Stop();
        _previewTimer.Stop();
        _sequenceTimer.Stop();
        _running = false;
        SetCropControls(true);
        ClearPendingFrame();
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

        if (!_cropProfile.MatchesConfirmedResolution(client))
        {
            StopRecording("Kích thước game đã thay đổi. Hãy xem trước và xác nhận lại khung minimap trước khi gửi thêm ảnh.");
            return;
        }
        var region = _cropProfile.Crop(client);
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
            // Exactly one preview JPEG may remain in RAM for explicit manual selection.
            // No frame is ever saved automatically by the timer or by Gemini.
            SetPendingFrame(jpeg.ToArray());
            // Discard the previous analysis text; retain no replay log or video.
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

    private void ClearSequenceFrames()
    {
        _sequenceTimer.Stop();
        foreach (var item in _sequenceFrames)
            Array.Clear(item.Jpeg, 0, item.Jpeg.Length);
        _sequenceFrames.Clear();
        _sequenceGroup = null;
        _currentSequenceGroup = null;
        _currentSequenceIndex = null;
        _currentFrameTakenAt = null;
        if (SequenceFrameBox is not null) SequenceFrameBox.Items.Clear();
        ClearPendingFrame();
        if (!_running) SetCropControls(true);
        if (SequenceStartButton is not null) SequenceStartButton.IsEnabled = !_running;
    }

    private void ClearSequenceClick(object sender, RoutedEventArgs e)
    {
        ClearSequenceFrames();
        SequenceStatus.Text = "Đã xóa chuỗi chưa lưu khỏi RAM. " +
            "Những ảnh bạn đã chủ động lưu trong thư viện không bị xóa.";
    }

    private void StartSequenceClick(object sender, RoutedEventArgs e)
    {
        if (_closed || _running || _busy || _previewTimer.IsEnabled ||
            _sequenceTimer.IsEnabled) return;
        if (!_cropProfile.IsValid || _cropProfile.ConfirmedClientWidth < 640 ||
            !SameCrop(CurrentCropDraft(), _cropProfile))
        {
            SequenceStatus.Text = "Hãy xác nhận vùng cắt minimap trước khi thu chuỗi.";
            return;
        }
        if (!IsGameRunning())
        {
            SequenceStatus.Text = "Chưa có trận luyện tập hoặc trận xem lại đang chạy.";
            return;
        }
        if (MessageBox.Show(this, "Bạn đồng ý lấy TỐI ĐA 5 ảnh minimap " +
            "cách nhau khoảng 2 giây khi cửa sổ TRẬN Liên Minh được chọn? " +
            "Ảnh CHỈ ở RAM; không gửi tới Gemini, không lưu tự động, " +
            "và sẽ xóa khi bạn chọn Xóa chuỗi hoặc đóng cửa sổ.",
            "Xác nhận thu chuỗi ảnh minimap cục bộ",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        ClearSequenceFrames();
        _sequenceGroup = "seq-" + Guid.NewGuid().ToString("N")[..12];
        _sequenceUntilUtc = DateTime.UtcNow.AddMinutes(2);
        SetCropControls(false);
        SequenceStartButton.IsEnabled = false;
        _sequenceTimer.Start();
        SequenceStatus.Text = "Đang chờ bạn chuyển về cửa sổ TRẬN Liên Minh. " +
            "Sẽ lấy tối đa 5 ảnh trong RAM, không gửi AI. Trở về cửa sổ này sau khoảng 10 giây.";
    }

    private void CaptureSequenceTick(object? sender, EventArgs e)
    {
        if (_closed || _running || _sequenceGroup is null ||
            DateTime.UtcNow >= _sequenceUntilUtc)
        {
            _sequenceTimer.Stop();
            if (!_closed)
            {
                FinishSequenceCapture("Đã kết thúc thời gian thu chuỗi. ");
            }
            return;
        }
        if (!TryGetForegroundGameClient(out var client)) return;
        try
        {
            if (!_cropProfile.MatchesConfirmedResolution(client))
            {
                FinishSequenceCapture("Kích thước game đã thay đổi; cần xác nhận khung trước khi thu tiếp. ");
                return;
            }
            var region = _cropProfile.Crop(client);
            region.Intersect(client);
            region.Intersect(System.Windows.Forms.SystemInformation.VirtualScreen);
            if (region.Width < 90 || region.Height < 90)
                throw new InvalidOperationException("Vùng cắt minimap quá nhỏ.");
            using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(region.Location, System.Drawing.Point.Empty,
                    region.Size, CopyPixelOperation.SourceCopy);
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Jpeg);
            if (memory.Length is < 24 or > MaximumImageBytes)
                throw new InvalidDataException("Ảnh vượt giới hạn 2 MB.");

            _sequenceFrames.Add(new SequenceFrame(memory.ToArray(), DateTimeOffset.UtcNow));
            SequenceStatus.Text = $"Đã lấy {_sequenceFrames.Count}/5 ảnh trong RAM; " +
                "không gửi Gemini và không lưu JPG tự động.";
            if (_sequenceFrames.Count >= 5)
            {
                FinishSequenceCapture("Đã thu đủ 5 ảnh. ");
            }
        }
        catch (Exception ex) when (ex is IOException or ExternalException or
                                   ArgumentException or InvalidOperationException)
        {
            FinishSequenceCapture("Thu chuỗi chưa hoàn tất: " + ex.Message + ". ");
        }
    }

    private void FinishSequenceCapture(string reason)
    {
        _sequenceTimer.Stop();
        SetCropControls(true);
        SequenceStartButton.IsEnabled = true;
        SequenceFrameBox.Items.Clear();
        for (var i = 0; i < _sequenceFrames.Count; i++)
            SequenceFrameBox.Items.Add($"Khung {i + 1}/{_sequenceFrames.Count} · " +
                _sequenceFrames[i].TakenAt.ToLocalTime().ToString("HH:mm:ss"));
        if (_sequenceFrames.Count > 0) SequenceFrameBox.SelectedIndex = 0;
        SequenceStatus.Text = reason + (_sequenceFrames.Count > 0
            ? $"Có {_sequenceFrames.Count} ảnh trong RAM. Chọn từng khung để xem và chỉ lưu ảnh bạn chọn; " +
              "các ảnh khác bị xóa khi đóng cửa sổ hoặc bấm Xóa chuỗi."
            : "Chưa lấy được ảnh nào. Kiểm tra cửa sổ trận rồi thử lại.");
    }

    private void GoToLabelClick(object sender, RoutedEventArgs e)
    {
        if (_selectedFrame is null)
        {
            SequenceStatus.Text = "Hãy xem trước minimap hoặc thu ảnh rồi chọn một khung trước khi gắn nhãn.";
            return;
        }
        WorkflowTabs.SelectedIndex = 1;
    }

    private void SequenceFrameChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = SequenceFrameBox.SelectedIndex;
        if (index < 0 || index >= _sequenceFrames.Count || _sequenceGroup is null ||
            _sequenceTimer.IsEnabled) return;
        var frame = _sequenceFrames[index];
        SetPendingFrame(frame.Jpeg.ToArray());
        _currentSequenceGroup = _sequenceGroup;
        _currentSequenceIndex = index;
        _currentFrameTakenAt = frame.TakenAt;
        AnalysisText.Text = $"Khung {index + 1}: ảnh minimap cục bộ, không gửi Gemini. " +
            "Bạn cần tự xác minh tên tướng và các điểm đã nhìn thấy.";
    }

    private MinimapCropProfile CurrentCropDraft() => new(
        Math.Round(CropLeftSlider.Value / 100d, 3),
        Math.Round(CropTopSlider.Value / 100d, 3),
        Math.Round(CropWidthSlider.Value / 100d, 3),
        Math.Round(CropHeightSlider.Value / 100d, 3));

    private void ApplyCropToSliders(MinimapCropProfile profile)
    {
        _suppressCropSliderChanged = true;
        try
        {
            CropLeftSlider.Value = profile.Left * 100;
            CropTopSlider.Value = profile.Top * 100;
            CropWidthSlider.Value = profile.Width * 100;
            CropHeightSlider.Value = profile.Height * 100;
        }
        finally { _suppressCropSliderChanged = false; }
    }

    private static bool SameCrop(MinimapCropProfile a, MinimapCropProfile b) =>
        a.Left == b.Left && a.Top == b.Top &&
        a.Width == b.Width && a.Height == b.Height;

    private void SetCropControls(bool enabled)
    {
        CropLeftSlider.IsEnabled = CropTopSlider.IsEnabled =
            CropWidthSlider.IsEnabled = CropHeightSlider.IsEnabled = enabled;
        PreviewCropButton.IsEnabled = enabled;
        AutoDetectButton.IsEnabled = enabled;
        SaveCropButton.IsEnabled = enabled && _previewProfile is not null &&
            _previewClient.Width >= 640 && _selectedFrame is not null;
    }

    private void CropSliderChanged(object sender,
        System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressCropSliderChanged) return;
        // Any adjustment invalidates the old preview; it is never proof that the
        // NEW bounds actually show a minimap at the current game resolution.
        _previewProfile = null;
        if (SaveCropButton is not null) SaveCropButton.IsEnabled = false;
        if (CropStatusText is not null && _cropProfile.ConfirmedClientWidth > 0)
            CropStatusText.Text = "Khung vừa được chỉnh sửa: cần xem trước và xác nhận lại trước khi phân tích.";
    }

    private void AutoDetectMinimapClick(object sender, RoutedEventArgs e)
    {
        if (_running || _busy || _previewTimer.IsEnabled ||
            _sequenceTimer.IsEnabled || _sequenceFrames.Count > 0) return;
        if (!IsGameRunning())
        {
            CropStatusText.Text = "Chưa vào trận Liên Minh. Hãy vào Phòng Tập hoặc xem lại trước.";
            return;
        }
        ClearPendingFrame();
        _autoDetectPreview = true;
        _previewProfile = CurrentCropDraft();
        _previewClient = Rectangle.Empty;
        SaveCropButton.IsEnabled = false;
        _previewUntilUtc = DateTime.UtcNow.AddMinutes(2);
        _previewTimer.Start();
        CropStatusText.Text = "Chuyển về cửa sổ trận Liên Minh. Phần mềm sẽ thử tìm " +
            "minimap từ một ảnh cục bộ trong RAM; không gửi Gemini hoặc lưu ảnh.";
    }

    private void PreviewCropClick(object sender, RoutedEventArgs e)
    {
        if (_running || _busy || _previewTimer.IsEnabled ||
            _sequenceTimer.IsEnabled || _sequenceFrames.Count > 0) return;
        var draft = CurrentCropDraft();
        if (!draft.IsValid)
        {
            CropStatusText.Text = "Khung cắt vượt mép game hoặc quá nhỏ. " +
                "Tổng vị trí + chiều rộng/chiều cao không được vượt 100%.";
            return;
        }
        if (!IsGameRunning())
        {
            CropStatusText.Text = "Hãy vào trận luyện tập hoặc mở trận xem lại trước khi xem khung.";
            return;
        }
        ClearPendingFrame();
        _autoDetectPreview = false;
        _previewProfile = draft;
        _previewClient = Rectangle.Empty;
        SaveCropButton.IsEnabled = false;
        _previewUntilUtc = DateTime.UtcNow.AddMinutes(2);
        _previewTimer.Start();
        CropStatusText.Text = "Đang chờ bạn chuyển về cửa sổ TRẬN Liên Minh được chọn. " +
            "Ứng dụng sẽ lấy MỘT ảnh trong RAM để xem trước; không gửi Gemini hay lưu JPG.";
    }

    private void PreviewCropTick(object? sender, EventArgs e)
    {
        if (_closed || _running || _previewProfile is null ||
            DateTime.UtcNow >= _previewUntilUtc)
        {
            _previewTimer.Stop();
            if (!_closed) CropStatusText.Text = "Hết thời gian xem trước. Hãy thử lại.";
            return;
        }
        if (!TryGetForegroundGameClient(out var client)) return;

        Bitmap? capturedClient = null;
        try
        {
            var finding = "";
            if (_autoDetectPreview)
            {
                if (client.Width > 5000 || client.Height > 3000 ||
                    (long)client.Width * client.Height > 12_000_000)
                    throw new InvalidOperationException("Cửa sổ game quá lớn để tự tìm khung.");

                // Keep the detected frame in RAM until the preview is built.
                // A second screen capture may show a different moment of gameplay.
                capturedClient = new Bitmap(client.Width, client.Height,
                    PixelFormat.Format24bppRgb);
                using (var graphics = Graphics.FromImage(capturedClient))
                    graphics.CopyFromScreen(client.Location, System.Drawing.Point.Empty,
                        client.Size, CopyPixelOperation.SourceCopy);

                var detected = MinimapAutoCropDetector.Detect(capturedClient);
                var suggested = detected is { Confident: true }
                    ? detected.Crop
                    : MinimapAutoCropDetector.CornerSuggestion(client.Width, client.Height);
                ApplyCropToSliders(suggested);
                _previewProfile = CurrentCropDraft();
                // Reveal the manual fallback only when the detector cannot identify a reliable candidate.
                ManualCropExpander.IsExpanded = detected is not { Confident: true };
                if (!_previewProfile.IsValid)
                    throw new InvalidOperationException(
                        "Khung gợi ý vượt mép game. Hãy mở Chỉnh tay nếu lệch.");
                finding = detected is { Confident: true }
                    ? "Đã tìm thấy khung có dấu hiệu là minimap (chưa được xác nhận). "
                    : "Bộ dò chưa xác định được minimap; ảnh bên trái CHỈ là khung gợi ý. " +
                      "Nếu bị lệch, mở Chỉnh tay nếu lệch rồi bấm Xem trước lại. ";
            }

            var region = _previewProfile.Crop(client);
            region.Intersect(client);
            region.Intersect(System.Windows.Forms.SystemInformation.VirtualScreen);
            if (region.Width < 90 || region.Height < 90)
                throw new InvalidOperationException("Vùng cắt nhỏ hoặc nằm ngoài màn hình.");

            // Auto-detect: preview the EXACT captured frame used for detection.
            // Manual preview: capture only the selected rectangle to save memory.
            using var bitmap = capturedClient is null
                ? new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb)
                : capturedClient.Clone(new Rectangle(
                    region.Left - client.Left, region.Top - client.Top,
                    region.Width, region.Height), PixelFormat.Format24bppRgb);
            if (capturedClient is null)
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(region.Location, System.Drawing.Point.Empty,
                    region.Size, CopyPixelOperation.SourceCopy);
            }

            using var buffer = new MemoryStream();
            bitmap.Save(buffer, ImageFormat.Jpeg);
            if (buffer.Length is < 24 or > MaximumImageBytes)
                throw new InvalidDataException("Ảnh xem trước vượt giới hạn 2 MB.");

            _previewClient = client;
            SetPendingFrame(buffer.ToArray());
            SaveCropButton.IsEnabled = _selectedFrame is not null;
            AnalysisText.Text = "Ảnh xem trước chỉ tồn tại trong RAM; chưa gửi AI để phân tích.";
            CropStatusText.Text = finding + $"Ảnh xem trước của cửa sổ {client.Width}×{client.Height} đã sẵn sàng. " +
                "Chỉ bấm Dùng khung này khi ảnh chứa ĐÚNG toàn bộ minimap; " +
                "nếu còn lệch, mở Chỉnh tay nếu lệch và xem trước lại.";
        }
        catch (Exception ex) when (ex is IOException or ExternalException or
                                   ArgumentException or InvalidOperationException)
        {
            _previewProfile = null;
            _previewClient = Rectangle.Empty;
            SaveCropButton.IsEnabled = false;
            ClearPendingFrame();
            CropStatusText.Text = "Chưa xem được vùng minimap: " + ex.Message;
        }
        finally
        {
            capturedClient?.Dispose();
            _autoDetectPreview = false;
            _previewTimer.Stop();
        }
    }

    private void SaveCropClick(object sender, RoutedEventArgs e)
    {
        if (_running || _previewTimer.IsEnabled || _previewProfile is null ||
            _previewClient.Width < 640 || _selectedFrame is null ||
            !SameCrop(CurrentCropDraft(), _previewProfile))
        {
            CropStatusText.Text = "Ảnh xem trước đã cũ hoặc thanh trượt đã thay đổi. Hãy xem trước lại.";
            return;
        }
        if (MessageBox.Show(this,
            "Bạn đã KIỂM TRA ảnh xem trước và xác nhận vùng này CHỈ chứa minimap " +
            $"trên cửa sổ game {_previewClient.Width}×{_previewClient.Height}? " +
            "Sau khi xác nhận, ứng dụng mới cho phép gửi ảnh theo khung này tới Gemini.",
            "Xác nhận căn chỉnh minimap", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var confirmed = _previewProfile with
            {
                ConfirmedClientWidth = _previewClient.Width,
                ConfirmedClientHeight = _previewClient.Height
            };
            _cropStore.Save(confirmed);
            _cropProfile = confirmed;
            _previewProfile = null;
            SaveCropButton.IsEnabled = false;
            CropStatusText.Text = $"Đã lưu khung minimap cho {confirmed.ConfirmedClientWidth}×" +
                $"{confirmed.ConfirmedClientHeight}. Bây giờ có thể thu 5 ảnh KHÔNG gửi AI.";
            WorkflowTabs.SelectedIndex = 0; // Capture and calibration now share the same main tab.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException)
        {
            CropStatusText.Text = "Chưa lưu được cấu hình khung: " + ex.Message;
        }
    }

    private void ClearPendingFrame()
    {
        if (_selectedFrame is not null) Array.Clear(_selectedFrame, 0, _selectedFrame.Length);
        _selectedFrame = null;
        _currentSequenceGroup = null;
        _currentSequenceIndex = null;
        _currentFrameTakenAt = null;
        _markX = _markY = null;
        _marks.Clear();
        if (ChampionMarksStatus is not null)
            ChampionMarksStatus.Text = "Chưa có điểm đánh dấu. Nhấp lên biểu tượng nhìn thấy được rồi Thêm điểm.";
        if (MarkCoordinateStatus is not null)
            MarkCoordinateStatus.Text = "Chưa chọn tọa độ.";
        if (SamplePreview is not null) SamplePreview.Source = null;
        if (PreviewEmptyText is not null) PreviewEmptyText.Visibility = Visibility.Visible;
        if (SaveSampleButton is not null) SaveSampleButton.IsEnabled = false;
    }

    private void SetPendingFrame(byte[] jpeg)
    {
        ClearPendingFrame();
        try
        {
            using var stream = new MemoryStream(jpeg, writable: false);
            var preview = new BitmapImage();
            preview.BeginInit();
            preview.CacheOption = BitmapCacheOption.OnLoad;
            preview.StreamSource = stream;
            preview.EndInit();
            preview.Freeze();
            _selectedFrame = jpeg;
            SamplePreview.Source = preview;
            PreviewEmptyText.Visibility = Visibility.Collapsed;
            SaveSampleButton.IsEnabled = true;
            SampleLabelBox.Clear(); // User must confirm EACH frame; never carry forward an old label.
            EvidenceTypeBox.SelectedIndex = 2; // Never carry over a confirmed-looking tag.
            TrainingStatus.Text = "Ảnh vừa phân tích đang nằm trong RAM. Kiểm tra đúng minimap, " +
                "ghi nhãn của chính bạn rồi nhấn Lưu. Không tự tạo tập huấn luyện.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or
                                   NotSupportedException or ArgumentException)
        {
            Array.Clear(jpeg, 0, jpeg.Length);
            ClearPendingFrame();
            TrainingStatus.Text = "Không xem được ảnh để xác nhận: " + ex.Message;
        }
    }

    private void SamplePreviewClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedFrame is null || SamplePreview.Source is not BitmapSource source) return;
        var panel = SamplePreview;
        var pos = e.GetPosition(panel);
        if (panel.ActualWidth <= 0 || panel.ActualHeight <= 0 ||
            source.PixelWidth <= 0 || source.PixelHeight <= 0) return;

        // Stretch=Uniform may letterbox the JPEG inside the Image layout.
        // Convert from rendered pixels (not entire Image bounds) to normalized
        // JPEG coordinates; clicking the blank margin is never treated as an icon.
        var scale = Math.Min(panel.ActualWidth / source.PixelWidth,
            panel.ActualHeight / source.PixelHeight);
        var shownWidth = scale * source.PixelWidth;
        var shownHeight = scale * source.PixelHeight;
        var left = (panel.ActualWidth - shownWidth) / 2;
        var top = (panel.ActualHeight - shownHeight) / 2;
        if (pos.X < left || pos.X > left + shownWidth ||
            pos.Y < top || pos.Y > top + shownHeight) return;

        _markX = Math.Clamp((pos.X - left) / shownWidth, 0, 1);
        _markY = Math.Clamp((pos.Y - top) / shownHeight, 0, 1);
        MarkCoordinateStatus.Text =
            $"Đã chọn tọa độ ảnh x={_markX:P1}, y={_markY:P1}. " +
            "Chọn tên, đội, vai trò (không rõ thì giữ Chưa rõ), sau đó nhấn Thêm điểm.";
        e.Handled = true;
    }

    private void AddChampionMarkClick(object sender, RoutedEventArgs e)
    {
        if (_selectedFrame is null || _markX is null || _markY is null)
        {
            TrainingStatus.Text = "Chưa chọn điểm: hãy nhấp lên biểu tượng nhìn thấy được trong ảnh hiện tại.";
            return;
        }
        if (_marks.Count >= 10)
        {
            TrainingStatus.Text = "Giới hạn tối đa 10 điểm biểu tượng trong một ảnh.";
            return;
        }
        try
        {
            var team = (ChampionTeamBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "unknown";
            var role = (ChampionRoleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "unknown";
            var mark = MinimapChampionMark.Checked(_markX.Value, _markY.Value,
                ChampionNameBox.Text, team, role);
            _marks.Add(mark);
            EvidenceTypeBox.SelectedIndex = 0;
            ChampionMarksStatus.Text = $"Đã chọn {_marks.Count} điểm trên ảnh hiện tại. " +
                string.Join("; ", _marks.Select(m =>
                    $"{m.Champion} ({m.X:P0}, {m.Y:P0}; {m.Team}, {m.Role})"));
            _markX = _markY = null;
            MarkCoordinateStatus.Text = "Đã thêm điểm; hãy nhấp lên vị trí khác để tiếp tục.";
        }
        catch (ArgumentException ex)
        {
            TrainingStatus.Text = "Không thêm được điểm: " + ex.Message;
        }
    }

    private void ClearChampionMarksClick(object sender, RoutedEventArgs e)
    {
        _marks.Clear();
        _markX = _markY = null;
        ChampionMarksStatus.Text = "Đã xóa toàn bộ điểm trên ảnh đang xem; chưa thay đổi mẫu đã lưu.";
        MarkCoordinateStatus.Text = "Chưa chọn tọa độ.";
    }

    private void SaveSampleClick(object sender, RoutedEventArgs e)
    {
        if (_selectedFrame is null)
        {
            TrainingStatus.Text = "Chưa có ảnh minimap để chọn; ảnh cũ đã bị giải phóng.";
            return;
        }
        var label = SampleLabelBox.Text.Trim();
        if (label.Length is < 3 or > 300)
        {
            TrainingStatus.Text = "Hãy mô tả bằng 3–300 ký tự điều thật sự nhìn thấy; " +
                "ghi riêng mọi suy luận. Không xác định thì ghi 'không xác định'.";
            return;
        }
        if (MessageBox.Show(this,
            "Bạn đã xem ảnh và xác nhận CHỈ LƯU MỘT ảnh hiện tại cùng ghi chú thủ công? " +
            "Ảnh sẽ lưu tại ổ đĩa cục bộ trong thư mục mẫu V1.8, KHÔNG tự xóa cuối trận. " +
            "Nhãn bạn ghi chưa phải sự thật được mô hình xác minh. Không gửi ảnh tới bên thứ ba khi lưu.",
            "Xác nhận lưu mẫu V1.8", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var evidenceKind = (EvidenceTypeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                ?? "uncertain";
            var (name, count) = _sampleStore.SaveSelected(_selectedFrame, label,
                evidenceKind, _marks, _currentSequenceGroup, _currentSequenceIndex,
                _currentFrameTakenAt);
            TrainingStatus.Text = $"Đã lưu mẫu {name} ({count}/250) cùng nhãn thủ công tại: " +
                                  _sampleStore.Folder;
            ClearPendingFrame(); // A given preview must not be saved repeatedly by accident.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or InvalidDataException)
        {
            TrainingStatus.Text = "Chưa lưu mẫu: " + ex.Message;
        }
    }

    private void OpenSampleLibraryClick(object sender, RoutedEventArgs e)
    {
        new MinimapSampleLibraryWindow { Owner = this }.ShowDialog();
        TrainingStatus.Text = "Đã đóng thư viện mẫu. Ảnh đang xem trong RAM vẫn không được tự lưu.";
    }

    private void DeleteTrainingSamplesClick(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            TrainingStatus.Text = "Hãy dừng phiên và giải phóng ảnh RAM trước khi xóa mẫu.";
            return;
        }
        if (MessageBox.Show(this, "XÓA VĨNH VIỄN tất cả mẫu JPG và nhãn JSON do V1.8 " +
            "lưu trong thư mục riêng? Việc này không xóa ảnh của phiên bản cũ hoặc tệp ở nơi khác.",
            "Xác nhận xóa toàn bộ dữ liệu mẫu V1.8",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            ClearPendingFrame();
            var count = _sampleStore.DeleteAllSelected();
            TrainingStatus.Text = $"Đã xóa {count} tệp JPG/JSON của bộ mẫu V1.8. " +
                "Thư mục khác và dữ liệu đã gửi Gemini không bị ảnh hưởng.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TrainingStatus.Text = "Không thể xóa hết bộ mẫu: " + ex.Message;
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
        _previewTimer.Stop();
        ClearSequenceFrames();
        StopRecording("Cửa sổ đã đóng.");
        ClearPendingFrame();
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
