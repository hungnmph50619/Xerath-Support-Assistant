using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;

namespace XerathAssistant.Desktop;

public partial class MinimapTrainingRecorderWindow
{
    private readonly DispatcherTimer _autoTrainingTimer = new()
        { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _autoTrainingRunning;
    private DateTime _autoTrainingNextUtc;
    private DateTime _autoTrainingDeadlineUtc;
    private DateTime _autoTrainingLastFocusedUtc;
    private int _autoTrainingLimit;
    private int _autoTrainingIntervalSeconds;
    private int _autoTrainingCaptured;
    private MinimapCropProfile? _autoTrainingReference;
    private string? _autoTrainingYoloLabel;

    private void InitializeAutoTraining()
    {
        _autoTrainingTimer.Tick += AutoTrainingTick;
    }

    private void LoadTrainingLabelClick(object sender, RoutedEventArgs e)
    {
        if (_closed || _running || _busy || _previewBusy || _autoTrainingRunning ||
            _previewTimer.IsEnabled || _aiTrainingTimer.IsEnabled ||
            _sequenceTimer.IsEnabled || _sequenceFrames.Count > 0) return;
        try
        {
            if (!MinimapAutoTraining.TryLoadReference(
                    MinimapLocalAiDetector.TrainingFolder,
                    _cropProfile.ConfirmedClientWidth, _cropProfile.ConfirmedClientHeight,
                    out var proposed, out var message))
            {
                AutoTrainingStatusText.Text = message;
                return;
            }

            // Nạp nhãn không xác nhận lại khung cũ, và không cho phép tự động thu.
            ClearPendingFrame();
            _previewProfile = null;
            _previewClient = Rectangle.Empty;
            SaveCropButton.IsEnabled = false;
            ApplyCropToSliders(proposed);
            ManualCropExpander.IsExpanded = false;
            CropStatusText.Text = message;
            AutoTrainingStatusText.Text = "Đã nạp nhãn. Bấm Xem trước lại, chuyển về game, " +
                "kiểm tra ảnh bên trái và bấm Dùng khung này trước khi thu tự động.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or InvalidOperationException)
        {
            AutoTrainingStatusText.Text = "Không đọc được nhãn đã lưu: " + ex.Message;
        }
    }

    private void StartAutoTrainingClick(object sender, RoutedEventArgs e)
    {
        if (_closed || _running || _busy || _previewBusy || _autoTrainingRunning ||
            _previewTimer.IsEnabled || _aiTrainingTimer.IsEnabled ||
            _sequenceTimer.IsEnabled || _sequenceFrames.Count > 0) return;

        if (!_cropProfile.IsValid || _cropProfile.ConfirmedClientWidth < 640 ||
            !SameCrop(CurrentCropDraft(), _cropProfile))
        {
            AutoTrainingStatusText.Text = "Chưa xác nhận khung minimap hiện tại. " +
                "Nạp nhãn đã có → Xem trước lại → Dùng khung này trước.";
            return;
        }
        if (!MinimapAutoTraining.TryBuildYolo(_cropProfile,
                _cropProfile.ConfirmedClientWidth, _cropProfile.ConfirmedClientHeight,
                out var label, out var reason))
        {
            AutoTrainingStatusText.Text = reason;
            return;
        }
        if (!IsGameRunning())
        {
            AutoTrainingStatusText.Text = "Hãy vào Phòng Tập trong game trước.";
            return;
        }

        var requested = AutoTrainingCountBox.SelectedIndex switch
        {
            1 => 20, 2 => 30, _ => 10
        };
        var interval = AutoTrainingIntervalBox.SelectedIndex switch
        {
            0 => 5, 2 => 20, _ => 10
        };
        if (MessageBox.Show(this,
            $"Bắt đầu tự lưu tối đa {requested} cặp PNG/TXT, cách nhau {interval} giây? " +
            "Mỗi ảnh chứa vùng GÓC DƯỚI BÊN PHẢI của cửa sổ trận (gồm cả HUD/địa hình ngoài minimap). " +
            "Nhãn được sao từ khung BẠN ĐÃ XÁC NHẬN, không phải nhãn AI suy luận. " +
            $"Chỉ dùng trong Phòng Tập, giữ nguyên độ phân giải {_cropProfile.ConfirmedClientWidth}×" +
            $"{_cropProfile.ConfirmedClientHeight}, kích thước và vị trí minimap. " +
            "Dừng ngay nếu bạn đổi giao diện, vì phần mềm không phát hiện mọi thay đổi UI. " +
            "Ảnh được lưu vào ổ đĩa đến khi bạn tự xóa, KHÔNG gửi lên mạng/Gemini. " +
            "Hãy kiểm tra nhãn của các ảnh sau mỗi lô, loại bỏ ảnh sai trước khi huấn luyện. " +
            "Bạn có thể bấm Dừng thu bất kỳ lúc nào. Bạn đồng ý?",
            "Xác nhận tự thu và gắn nhãn huấn luyện", MessageBoxButton.YesNo,
            MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _autoTrainingReference = _cropProfile;
        _autoTrainingYoloLabel = label;
        _autoTrainingLimit = Math.Min(requested, MinimapAutoTraining.MaximumSessionImages);
        _autoTrainingIntervalSeconds = interval;
        _autoTrainingCaptured = 0;
        _autoTrainingLastFocusedUtc = DateTime.UtcNow;
        _autoTrainingDeadlineUtc = DateTime.UtcNow.AddMinutes(15);
        _autoTrainingNextUtc = DateTime.UtcNow;
        _autoTrainingRunning = true;
        SetCropControls(false);
        StartButton.IsEnabled = false;
        SequenceStartButton.IsEnabled = false;
        TrainingCaptureButton.IsEnabled = false;
        LoadTrainingLabelButton.IsEnabled = false;
        AutoTrainingCountBox.IsEnabled = false;
        AutoTrainingIntervalBox.IsEnabled = false;
        AutoTrainingStartButton.IsEnabled = false;
        AutoTrainingStopButton.IsEnabled = true;
        _autoTrainingTimer.Start();
        AutoTrainingStatusText.Text = $"Đang chờ bạn chuyển về Phòng Tập: 0/{_autoTrainingLimit} ảnh. " +
            "Không đổi minimap; quay lại để bấm Dừng nếu cần.";
    }

    private void StopAutoTrainingClick(object sender, RoutedEventArgs e) =>
        StopAutoTraining("Bạn đã dừng thu.");

    private void StopAutoTraining(string reason)
    {
        _autoTrainingTimer.Stop();
        if (!_autoTrainingRunning) return;
        _autoTrainingRunning = false;
        _autoTrainingReference = null;
        _autoTrainingYoloLabel = null;
        if (_closed) return;
        SetCropControls(true);
        StartButton.IsEnabled = true;
        SequenceStartButton.IsEnabled = true;
        LoadTrainingLabelButton.IsEnabled = true;
        AutoTrainingCountBox.IsEnabled = true;
        AutoTrainingIntervalBox.IsEnabled = true;
        AutoTrainingStartButton.IsEnabled = true;
        AutoTrainingStopButton.IsEnabled = false;
        AutoTrainingStatusText.Text = reason + $" Đã lưu {_autoTrainingCaptured}/{_autoTrainingLimit} cặp PNG/TXT. " +
            "Mở thư mục minimap-ai-training để kiểm tra ảnh và nhãn trước khi huấn luyện.";
    }

    private void AutoTrainingTick(object? sender, EventArgs e)
    {
        if (!_autoTrainingRunning || _closed) return;
        if (!IsGameRunning() || DateTime.UtcNow >= _autoTrainingDeadlineUtc)
        {
            StopAutoTraining("Game đã đóng hoặc phiên hết thời gian (15 phút).");
            return;
        }
        if (!TryGetForegroundGameClient(out var client))
        {
            if (DateTime.UtcNow - _autoTrainingLastFocusedUtc > TimeSpan.FromMinutes(2))
                StopAutoTraining("Không thấy cửa sổ trận được chọn trong 2 phút.");
            else
                AutoTrainingStatusText.Text = "Đang chờ bạn chuyển về game; " +
                    $"đã lưu {_autoTrainingCaptured}/{_autoTrainingLimit}. Không chụp cửa sổ khác.";
            return;
        }
        _autoTrainingLastFocusedUtc = DateTime.UtcNow;
        var reference = _autoTrainingReference;
        if (reference is null || !reference.MatchesConfirmedResolution(client) ||
            !SameCrop(reference, _cropProfile) ||
            !SameCrop(CurrentCropDraft(), reference))
        {
            StopAutoTraining("Kích thước game hoặc khung tham chiếu thay đổi; " +
                "không tiếp tục tạo nhãn bằng tọa độ cũ.");
            return;
        }
        if (DateTime.UtcNow < _autoTrainingNextUtc) return;
        _autoTrainingNextUtc = DateTime.UtcNow.AddSeconds(_autoTrainingIntervalSeconds);
        try
        {
            if (!MinimapAutoTraining.TryBuildYolo(reference, client.Width, client.Height,
                    out var label, out var error) || label != _autoTrainingYoloLabel)
                throw new InvalidOperationException(error.Length > 0 ? error :
                    "Nhãn tham chiếu không còn khớp.");
            var search = MinimapLocalAiDetector.SearchRegion(client.Width, client.Height);
            var region = new Rectangle(client.Left + search.Left, client.Top + search.Top,
                search.Width, search.Height);
            if (!System.Windows.Forms.SystemInformation.VirtualScreen.Contains(region))
                throw new InvalidOperationException("Vùng dò nằm ngoài màn hình; đã dừng thu.");
            using var frame = new Bitmap(region.Width, region.Height,
                PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(frame))
                graphics.CopyFromScreen(region.Location, System.Drawing.Point.Empty,
                    region.Size, CopyPixelOperation.SourceCopy);
            var name = MinimapAutoTraining.SaveOnePair(frame, label,
                MinimapLocalAiDetector.TrainingFolder);
            _autoTrainingCaptured++;
            AutoTrainingStatusText.Text = $"Đã lưu {_autoTrainingCaptured}/{_autoTrainingLimit} cặp PNG/TXT " +
                $"(gần nhất: {name}). Giữ nguyên minimap; ảnh mới cần được kiểm tra.";
            if (_autoTrainingCaptured >= _autoTrainingLimit)
                StopAutoTraining("Đã thu đủ số ảnh bạn chọn.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ExternalException or ArgumentException or
                                   InvalidOperationException)
        {
            StopAutoTraining("Thu ảnh bị dừng: " + ex.Message);
        }
    }
}
