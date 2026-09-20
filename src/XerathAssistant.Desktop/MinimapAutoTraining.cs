using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace XerathAssistant.Desktop;

/// <summary>
/// Tọa độ tham chiếu lấy từ nhãn YOLO do người dùng vẽ trên ảnh ROI.
/// Chỉ dùng với đúng độ phân giải đã xác nhận, không tự suy đoán khi HUD thay đổi.
/// </summary>
internal static class MinimapAutoTraining
{
    internal const int MaximumSessionImages = 30;
    internal const int MaximumLibraryImages = 500;

    internal static bool TryLoadReference(string folder, int clientWidth, int clientHeight,
        out MinimapCropProfile crop, out string message)
    {
        crop = MinimapCropProfile.Default;
        message = "";
        if (clientWidth < 640 || clientHeight < 400)
        {
            message = "Chưa có kích thước game đã xác nhận. Hãy căn một khung và xác nhận trước.";
            return false;
        }
        if (!Directory.Exists(folder))
        {
            message = "Chưa có thư mục ảnh huấn luyện.";
            return false;
        }

        var roi = MinimapLocalAiDetector.SearchRegion(clientWidth, clientHeight);
        foreach (var path in Directory.EnumerateFiles(folder, "roi-*.png")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var labelPath = Path.ChangeExtension(path, ".txt");
            if (!File.Exists(labelPath)) continue;
            try
            {
                using var img = Image.FromFile(path);
                if (img.Width != roi.Width || img.Height != roi.Height) continue;
                if (!TryParseYolo(File.ReadAllText(labelPath), roi.Width, roi.Height,
                        out var box)) continue;
                var full = new Rectangle(
                    roi.Left + box.Left, roi.Top + box.Top, box.Width, box.Height);
                var proposal = new MinimapCropProfile(
                    Math.Round(full.Left / (double)clientWidth, 3),
                    Math.Round(full.Top / (double)clientHeight, 3),
                    Math.Round(full.Width / (double)clientWidth, 3),
                    Math.Round(full.Height / (double)clientHeight, 3));
                if (!proposal.IsValid || !roi.Contains(full)) continue;
                crop = proposal;
                message = "Đã nạp nhãn từ " + Path.GetFileName(path) +
                    ". Hãy bấm Xem trước lại, kiểm tra ảnh rồi Dùng khung này.";
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       ArgumentException or OutOfMemoryException)
            {
                // Skip broken or inaccessible pairs; never guess missing labels.
            }
        }
        message = "Không tìm thấy cặp PNG/TXT hợp lệ cho độ phân giải game đã lưu. " +
            "Hãy xem lại nhãn hoặc căn khung thủ công.";
        return false;
    }

    private static bool TryParseYolo(string raw, int width, int height, out Rectangle box)
    {
        box = Rectangle.Empty;
        var lines = raw.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        if (lines.Length != 1) return false;
        var fields = lines[0].Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5 || fields[0] != "0") return false;
        var values = new double[4];
        for (var i = 0; i < values.Length; i++)
            if (!double.TryParse(fields[i + 1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out values[i]) ||
                !double.IsFinite(values[i]))
                return false;
        var (cx, cy, w, h) = (values[0], values[1], values[2], values[3]);
        if (w <= 0 || h <= 0 || cx - w / 2 < 0 || cy - h / 2 < 0 ||
            cx + w / 2 > 1 || cy + h / 2 > 1) return false;
        var x1 = (int)Math.Round((cx - w / 2) * width);
        var y1 = (int)Math.Round((cy - h / 2) * height);
        var x2 = (int)Math.Round((cx + w / 2) * width);
        var y2 = (int)Math.Round((cy + h / 2) * height);
        box = Rectangle.FromLTRB(x1, y1, x2, y2);
        return box.Width >= 90 && box.Height >= 90 &&
            new Rectangle(0, 0, width, height).Contains(box);
    }

    internal static bool TryBuildYolo(MinimapCropProfile reference,
        int width, int height, out string label, out string message)
    {
        label = "";
        message = "";
        if (!reference.IsValid || reference.ConfirmedClientWidth != width ||
            reference.ConfirmedClientHeight != height)
        {
            message = "Khung chưa được xác nhận cho độ phân giải hiện tại.";
            return false;
        }
        var client = new Rectangle(0, 0, width, height);
        var roi = MinimapLocalAiDetector.SearchRegion(width, height);
        var minimap = reference.Crop(client);
        if (!roi.Contains(minimap) || minimap.Width < 90 || minimap.Height < 90)
        {
            message = "Khung minimap nằm ngoài vùng ảnh ROI hoặc quá nhỏ.";
            return false;
        }
        var x = (minimap.Left - roi.Left) / (double)roi.Width;
        var y = (minimap.Top - roi.Top) / (double)roi.Height;
        var w = minimap.Width / (double)roi.Width;
        var h = minimap.Height / (double)roi.Height;
        label = string.Format(CultureInfo.InvariantCulture,
            "0 {0:F7} {1:F7} {2:F7} {3:F7}\n",
            x + w / 2, y + h / 2, w, h);
        return true;
    }

    internal static string SaveOnePair(Bitmap roiFrame, string label, string folder)
    {
        Directory.CreateDirectory(folder);
        // Never overwrite previously collected data. Stop before excess disk use.
        if (Directory.EnumerateFiles(folder, "roi-*.png")
                .Take(MaximumLibraryImages).Count() >= MaximumLibraryImages)
            throw new IOException("Đã đạt giới hạn 500 ảnh trong thư viện huấn luyện.");

        var name = "roi-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N")[..8];
        var imagePath = Path.Combine(folder, name + ".png");
        var labelPath = Path.Combine(folder, name + ".txt");
        try
        {
            roiFrame.Save(imagePath, ImageFormat.Png);
            File.WriteAllText(labelPath, label);
            return name;
        }
        catch
        {
            if (File.Exists(labelPath)) File.Delete(labelPath);
            if (File.Exists(imagePath)) File.Delete(imagePath);
            throw;
        }
    }
}
