using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace XerathAssistant.Desktop;

/// <summary>
/// Chỉ gửi ảnh ROI đã lưu, theo yêu cầu riêng của người dùng, tới API localhost
/// của AI Cá Nhân. API đó dùng Gemini bên ngoài máy: tuyệt đối không tự gọi trong
/// vòng lặp chụp ảnh game hoặc đánh dấu nhãn AI là nhãn đã xác minh.
/// </summary>
public partial class MinimapTrainingRecorderWindow
{
    private readonly HttpClient _aiReviewClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:5188/"),
        Timeout = TimeSpan.FromSeconds(55)
    };
    private CancellationTokenSource? _aiReviewCts;
    private bool _aiReviewRunning;

    private async void StartAiReviewClick(object sender, RoutedEventArgs e)
    {
        if (_closed || _aiReviewRunning || _autoTrainingRunning || _running ||
            _busy || _previewBusy || _aiTrainingTimer.IsEnabled ||
            _previewTimer.IsEnabled || _sequenceTimer.IsEnabled) return;
        var folder = MinimapLocalAiDetector.TrainingFolder;
        if (!Directory.Exists(folder))
        {
            AiReviewStatusText.Text = "Chưa có thư mục ảnh ROI đã lưu.";
            return;
        }

        string[] images;
        try
        {
            images = Directory.EnumerateFiles(folder, "roi-*.png")
                .Where(p => !File.Exists(Path.ChangeExtension(p, ".ai-review.json")))
                .OrderByDescending(File.GetLastWriteTimeUtc).Take(5).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AiReviewStatusText.Text = "Không đọc được thư mục ảnh: " + ex.Message;
            return;
        }
        if (images.Length == 0)
        {
            AiReviewStatusText.Text = "Các ảnh đã được kiểm tra hoặc chưa có ảnh mới. " +
                "Không gửi ảnh nào lên Gemini.";
            return;
        }

        // Kiểm tra kết nối trước khi yêu cầu người dùng cho phép gửi ảnh.
        try
        {
            using var status = await _aiReviewClient.GetAsync(
                "api/vision/minimap/locate/status");
            if (!status.IsSuccessStatusCode)
            {
                AiReviewStatusText.Text = "AI Cá Nhân chưa có API tọa độ minimap. " +
                    "Hãy cập nhật và khởi động AI Cá Nhân trên localhost:5188.";
                return;
            }
            using var json = JsonDocument.Parse(
                await status.Content.ReadAsStreamAsync());
            if (!json.RootElement.TryGetProperty("available", out var available) ||
                available.ValueKind != JsonValueKind.True)
            {
                AiReviewStatusText.Text = "AI Cá Nhân chưa cấu hình Gemini hỗ trợ ảnh. " +
                    "Mở Cài đặt AI của AI Cá Nhân rồi thử lại.";
                return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or
                                   JsonException or IOException)
        {
            AiReviewStatusText.Text = "Chưa kết nối được AI Cá Nhân tại localhost:5188. " +
                "Hãy chạy AI Cá Nhân trước; không ảnh nào được gửi.";
            return;
        }

        if (_closed) return;
        if (MessageBox.Show(this,
            $"Bạn đồng ý gửi TỐI ĐA {images.Length} ảnh ROI ĐÃ LƯU từ thư mục " +
            "minimap-ai-training tới AI Cá Nhân trên localhost:5188, và từ đó " +
            "chuyển từng ảnh tới GOOGLE GEMINI để tìm khung minimap? " +
            "Ảnh ROI có thể chứa cảnh game, HUD, tên hoặc chữ ngoài bản đồ. " +
            "Dịch vụ bên ngoài có thể có giới hạn/phí và chính sách dữ liệu riêng. " +
            "Chỉ lưu tọa độ ĐỀ XUẤT cục bộ, không ghi đè nhãn TXT hiện có, " +
            "không tự chụp màn hình hoặc gửi ảnh khác. Sau khi chạy, hãy kiểm tra " +
            "các trường hợp AI và nhãn không khớp. Bạn đồng ý?",
            "Đồng ý gửi ảnh đã lưu cho AI Cá Nhân / Gemini",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _aiReviewCts = new CancellationTokenSource();
        _aiReviewRunning = true;
        AiReviewStartButton.IsEnabled = false;
        AiReviewStopButton.IsEnabled = true;
        var reviewed = 0;
        var flagged = 0;
        try
        {
            for (var index = 0; index < images.Length; index++)
            {
                _aiReviewCts.Token.ThrowIfCancellationRequested();
                if (_closed) break;
                var imagePath = images[index];
                AiReviewStatusText.Text =
                    $"AI Cá Nhân đang kiểm tra ảnh {index + 1}/{images.Length}: " +
                    Path.GetFileName(imagePath) + ".";
                var image = await File.ReadAllBytesAsync(imagePath, _aiReviewCts.Token);
                if (image.Length is < 24 or > 2 * 1024 * 1024)
                    throw new InvalidDataException("Ảnh không hợp lệ hoặc lớn hơn 2 MB: " +
                        Path.GetFileName(imagePath));
                int w, h;
                using (var ms = new MemoryStream(image, false))
                using (var bitmap = Image.FromStream(ms))
                {
                    w = bitmap.Width;
                    h = bitmap.Height;
                }
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent("true"), "confirmed");
                using var bytes = new ByteArrayContent(image);
                bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(bytes, "image", Path.GetFileName(imagePath));
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    "api/vision/minimap/locate") { Content = form };
                req.Headers.Add("X-Xerath-Vision", "1");
                using var response = await _aiReviewClient.SendAsync(req,
                    _aiReviewCts.Token);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"AI Cá Nhân trả HTTP {(int)response.StatusCode}. " +
                        "Dừng phiên để tránh tự gửi tiếp hoặc phát sinh thêm lượt Gemini.");
                using var result = JsonDocument.Parse(
                    await response.Content.ReadAsStreamAsync(_aiReviewCts.Token));
                var root = result.RootElement;
                int[]? box = null;
                if (root.TryGetProperty("found", out var found) &&
                    found.ValueKind == JsonValueKind.True &&
                    root.TryGetProperty("normalizedBox", out var coordinates) &&
                    coordinates.ValueKind == JsonValueKind.Array &&
                    coordinates.GetArrayLength() == 4)
                {
                    box = coordinates.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                    if (box.Any(v => v is < 0 or > 1000) ||
                        box[2] <= box[0] || box[3] <= box[1] ||
                        (box[2] - box[0]) / (double)(box[3] - box[1]) is < .7 or > 1.3)
                        box = null;
                }

                var hasOldLabel = TryReadExistingYolo(
                    Path.ChangeExtension(imagePath, ".txt"), out var oldBox);
                var overlap = box is null || !hasOldLabel
                    ? (double?)null : ComputeIoU(oldBox, box);
                var needsReview = box is null || !hasOldLabel || overlap < .85;
                var report = new
                {
                    image = Path.GetFileName(imagePath),
                    reviewedAtUtc = DateTimeOffset.UtcNow,
                    provider = "Gemini (qua AI Cá Nhân)",
                    imageWidth = w, imageHeight = h,
                    suggestedBox = box,
                    existingLabel = hasOldLabel,
                    intersectionOverUnion = overlap,
                    requiresManualReview = needsReview,
                    verified = false
                };
                if (box is not null)
                {
                    // Tệp riêng, KHÔNG thay ảnh hoặc TXT chuẩn do người dùng xác nhận.
                    var x = box[0] / 1000d; var y = box[1] / 1000d;
                    var bw = (box[2] - box[0]) / 1000d;
                    var bh = (box[3] - box[1]) / 1000d;
                    var proposal = string.Format(CultureInfo.InvariantCulture,
                        "0 {0:F7} {1:F7} {2:F7} {3:F7}\n",
                        x + bw / 2, y + bh / 2, bw, bh);
                    await File.WriteAllTextAsync(
                        Path.ChangeExtension(imagePath, ".ai-suggested.txt"),
                        proposal, _aiReviewCts.Token);
                }
                await File.WriteAllTextAsync(
                    Path.ChangeExtension(imagePath, ".ai-review.json"),
                    JsonSerializer.Serialize(report, new JsonSerializerOptions
                        { WriteIndented = true }), _aiReviewCts.Token);
                reviewed++;
                if (needsReview) flagged++;
                AiReviewStatusText.Text =
                    $"Đã xem {reviewed}/{images.Length}; {flagged} ảnh cần kiểm tra nhãn. " +
                    "Không ảnh hay nhãn chuẩn nào bị ghi đè.";
            }
            if (!_closed)
                AiReviewStatusText.Text =
                    $"Đã nhận đề xuất AI cho {reviewed}/{images.Length} ảnh; " +
                    $"{flagged} ảnh cần xem lại. Trong minimap-ai-training, " +
                    "mở tệp .ai-review.json và .ai-suggested.txt; " +
                    "không coi nhãn AI là nhãn đã xác minh.";
        }
        catch (OperationCanceledException)
        {
            if (!_closed) AiReviewStatusText.Text =
                $"Đã dừng theo yêu cầu. Đã kiểm tra {reviewed} ảnh, {flagged} cần xem lại.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or JsonException or
                                   ArgumentException or HttpRequestException or
                                   InvalidOperationException)
        {
            if (!_closed)
                AiReviewStatusText.Text = $"Đã dừng sau {reviewed} ảnh: {ex.Message}";
        }
        finally
        {
            _aiReviewRunning = false;
            _aiReviewCts?.Dispose();
            _aiReviewCts = null;
            if (!_closed)
            {
                AiReviewStartButton.IsEnabled = true;
                AiReviewStopButton.IsEnabled = false;
            }
        }
    }

    private void StopAiReviewClick(object sender, RoutedEventArgs e) =>
        _aiReviewCts?.Cancel();

    private static bool TryReadExistingYolo(string path, out int[] box)
    {
        box = Array.Empty<int>();
        if (!File.Exists(path)) return false;
        try
        {
            var lines = File.ReadAllText(path).Trim().Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length != 1) return false;
            var fields = lines[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 5 || fields[0] != "0") return false;
            var numbers = fields.Skip(1).Select(v => double.Parse(v,
                CultureInfo.InvariantCulture)).ToArray();
            if (numbers.Any(v => !double.IsFinite(v))) return false;
            var (cx,cy,w,h) = (numbers[0],numbers[1],numbers[2],numbers[3]);
            if (w <= 0 || h <= 0 || cx-w/2 < 0 || cy-h/2 < 0 ||
                cx+w/2 > 1 || cy+h/2 > 1) return false;
            box = new[]
            {
                (int)Math.Round((cx-w/2)*1000), (int)Math.Round((cy-h/2)*1000),
                (int)Math.Round((cx+w/2)*1000), (int)Math.Round((cy+h/2)*1000)
            };
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   FormatException or OverflowException)
        {
            return false;
        }
    }

    private static double ComputeIoU(int[] a, int[] b)
    {
        var w = Math.Max(0, Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]));
        var h = Math.Max(0, Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]));
        var intersection = (double)w * h;
        var area1 = (double)(a[2]-a[0]) * (a[3]-a[1]);
        var area2 = (double)(b[2]-b[0]) * (b[3]-b[1]);
        var union = area1 + area2 - intersection;
        return union <= 0 ? 0 : intersection/union;
    }
}
