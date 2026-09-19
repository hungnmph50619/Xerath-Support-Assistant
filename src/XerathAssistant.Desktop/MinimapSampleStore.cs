using System.IO;
using System.Text.Json;

namespace XerathAssistant.Desktop;

/// <summary>Only saves a sample after an explicit click; no automatic recording.</summary>
public sealed class MinimapSampleStore
{
    private const int MaxSamples = 250;
    private const long MaxBytes = 200L * 1024 * 1024;
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "minimap-labeled-samples-v1.8");
    public string Folder => _root;

    public (string filename, int count) SaveSelected(byte[] jpeg, string label, string evidenceKind)
    {
        if (jpeg.Length is < 24 or > 2 * 1024 * 1024 ||
            jpeg[0] != 0xff || jpeg[1] != 0xd8 ||
            jpeg[^2] != 0xff || jpeg[^1] != 0xd9)
            throw new InvalidDataException("Ảnh JPEG đã chọn không hợp lệ hoặc vượt 2 MB.");
        if (evidenceKind is not ("visible-observation" or "hypothesis" or "uncertain"))
            throw new ArgumentException("Phân loại ghi chú không hợp lệ.");
        label = label.Trim();
        if (label.Length is < 3 or > 300)
            throw new ArgumentException("Hãy ghi nhãn mô tả ảnh từ 3 đến 300 ký tự.");

        Directory.CreateDirectory(_root);
        var imageFiles = Directory.EnumerateFiles(_root, "sample-*.jpg",
            SearchOption.TopDirectoryOnly).ToArray();
        if (imageFiles.Length >= MaxSamples)
            throw new IOException("Đã đạt 250 mẫu. Hãy kiểm tra hoặc xóa thủ công trước khi lưu tiếp.");
        long used = 0;
        foreach (var path in Directory.EnumerateFiles(_root, "sample-*",
                     SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(path);
            if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint)) used += info.Length;
        }
        var metadata = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            capturedFrom = "visible-practice-minimap",
            userLabel = label,
            evidenceKind,
            verification = "manual-note-not-verified-object-detection-ground-truth",
            capturedTimeUtc = DateTimeOffset.UtcNow,
            imageIsApproximateBottomRightCrop = true,
            automaticEnemyPositionInference = false
        }, new JsonSerializerOptions { WriteIndented = true });
        if (used + jpeg.Length + System.Text.Encoding.UTF8.GetByteCount(metadata) >= MaxBytes)
            throw new IOException("Đã đạt giới hạn 200 MB; không tự xóa ảnh đã lưu.");
        var name = "sample-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") +
                   "-" + Guid.NewGuid().ToString("N")[..8];
        var imagePath = Path.Combine(_root, name + ".jpg");
        var jsonPath = Path.Combine(_root, name + ".json");
        try
        {
            File.WriteAllBytes(imagePath, jpeg);
            File.WriteAllText(jsonPath, metadata);
        }
        catch
        {
            if (File.Exists(imagePath)) File.Delete(imagePath);
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
            throw;
        }
        return (name, imageFiles.Length + 1);
    }

    public int DeleteAllSelected()
    {
        if (!Directory.Exists(_root)) return 0;
        if (new DirectoryInfo(_root).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Không xóa thư mục liên kết.");
        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(_root, "sample-*",
                     SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(path);
            var extension = Path.GetExtension(path);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                !(extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                  extension.Equals(".json", StringComparison.OrdinalIgnoreCase))) continue;
            info.Delete();
            deleted++;
        }
        return deleted;
    }
}
