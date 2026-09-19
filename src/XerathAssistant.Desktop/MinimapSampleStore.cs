using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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

    public (string filename, int count) SaveSelected(byte[] jpeg, string label, string evidenceKind) =>
        SaveSelected(jpeg, label, evidenceKind, Array.Empty<MinimapChampionMark>());

    public (string filename, int count) SaveSelected(byte[] jpeg, string label,
        string evidenceKind, IReadOnlyList<MinimapChampionMark> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);
        if (marks.Count > 10)
            throw new ArgumentException("Mỗi ảnh tối đa 10 điểm biểu tượng tướng.");
        foreach (var mark in marks)
            _ = MinimapChampionMark.Checked(mark.X, mark.Y, mark.Champion, mark.Team, mark.Role);
        if (marks.Count > 0 && evidenceKind != "visible-observation")
            throw new ArgumentException("Điểm biểu tượng chỉ đi cùng nhãn quan sát trực tiếp.");
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
            marks,
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

    public sealed record SampleItem(string Name, string Label, string EvidenceKind,
        int MarkCount, long SizeBytes);

    private static readonly Regex SampleName = new(
        @"^sample-[0-9]{8}-[0-9]{9}-[0-9a-f]{8}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private string SamplePath(string name, string extension)
    {
        if (!SampleName.IsMatch(name) || extension is not (".jpg" or ".json"))
            throw new ArgumentException("Tên mẫu không hợp lệ.");
        return Path.Combine(_root, name + extension);
    }

    public IReadOnlyList<SampleItem> ListSamples()
    {
        if (!Directory.Exists(_root)) return Array.Empty<SampleItem>();
        if (new DirectoryInfo(_root).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Thư mục mẫu là liên kết, không thể kiểm tra an toàn.");
        var result = new List<SampleItem>();
        foreach (var path in Directory.EnumerateFiles(_root, "sample-*.jpg",
                     SearchOption.TopDirectoryOnly).OrderByDescending(p => p).Take(250))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!SampleName.IsMatch(name)) continue;
            var image = new FileInfo(path);
            if (image.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var metadata = SamplePath(name, ".json");
            if (!File.Exists(metadata) ||
                new FileInfo(metadata).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(metadata));
                var root = doc.RootElement;
                var label = root.TryGetProperty("userLabel", out var title)
                    ? title.GetString() ?? "" : "";
                var kind = root.TryGetProperty("evidenceKind", out var evidence)
                    ? evidence.GetString() ?? "uncertain" : "uncertain";
                var marks = root.TryGetProperty("marks", out var array) &&
                    array.ValueKind == JsonValueKind.Array ? array.GetArrayLength() : 0;
                result.Add(new SampleItem(name, label, kind, marks,
                    image.Length + new FileInfo(metadata).Length));
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
                // A malformed individual sample must not hide the rest of the library.
            }
        }
        return result;
    }

    public byte[] ReadSampleImage(string name)
    {
        var path = SamplePath(name, ".jpg");
        var info = new FileInfo(path);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            info.Length is < 24 or > 2 * 1024 * 1024)
            throw new IOException("Ảnh mẫu không hợp lệ.");
        return File.ReadAllBytes(path);
    }

    public void UpdateSampleNote(string name, string label, string evidenceKind)
    {
        label = label.Trim();
        if (label.Length is < 3 or > 300 ||
            evidenceKind is not ("visible-observation" or "hypothesis" or "uncertain"))
            throw new ArgumentException("Nhãn hoặc phân loại không hợp lệ.");
        var path = SamplePath(name, ".json");
        if (new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Không sửa tệp liên kết.");
        var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidDataException("Nhãn JSON không hợp lệ.");
        var marks = node["marks"] as JsonArray;
        if (marks is { Count: > 0 } && evidenceKind != "visible-observation")
            throw new ArgumentException("Ảnh đã gắn tọa độ biểu tượng; chỉ dùng nhãn quan sát trực tiếp.");
        node["userLabel"] = label;
        node["evidenceKind"] = evidenceKind;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void DeleteSample(string name)
    {
        foreach (var extension in new[] { ".jpg", ".json" })
        {
            var path = SamplePath(name, extension);
            if (!File.Exists(path)) continue;
            var file = new FileInfo(path);
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Không xóa tệp liên kết.");
            file.Delete();
        }
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
