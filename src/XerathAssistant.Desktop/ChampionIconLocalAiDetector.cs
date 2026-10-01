using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace XerathAssistant.Desktop;

public sealed record ChampionIconDetection(
    string Champion,
    string Team,
    double X,
    double Y,
    double Confidence);

public sealed record ChampionIconDetectionResult(
    bool ModelAvailable,
    string Status,
    IReadOnlyList<ChampionIconDetection> Detections);

/// <summary>
/// Local ONNX classifier used only on the already-cropped minimap frame.
/// No network calls and no hidden-state inference. A missing/invalid model
/// produces zero detections instead of falling back to heuristics.
/// </summary>
public sealed class ChampionIconLocalAiDetector : IDisposable
{
    internal const int InputSize = 64;
    private const float MinimumConfidence = 0.82f;
    private static readonly string ModelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "models", "champion-icon-classifier.onnx");
    private static readonly string LabelsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "models", "champion-icon-labels.json");

    private InferenceSession? _session;
    private ChampionClass[]? _classes;
    private string? _loadError;

    public bool ModelAvailable =>
        File.Exists(ModelPath) && File.Exists(LabelsPath);

    public ChampionIconDetectionResult Detect(MinimapRuntimeFrame frame)
    {
        if (!ModelAvailable)
        {
            return new(
                false,
                "Chưa có mô hình nhận diện biểu tượng tướng cục bộ.",
                Array.Empty<ChampionIconDetection>());
        }

        if (!EnsureLoaded())
        {
            return new(
                false,
                _loadError ?? "Không nạp được mô hình biểu tượng tướng.",
                Array.Empty<ChampionIconDetection>());
        }

        try
        {
            using var stream = new MemoryStream(frame.Jpeg, writable: false);
            using var bitmap = new Bitmap(stream);
            if (bitmap.Width < 90 || bitmap.Height < 90)
                return new(true, "Ảnh minimap quá nhỏ.", Array.Empty<ChampionIconDetection>());

            var patchSize = Math.Clamp(
                (int)Math.Round(Math.Min(bitmap.Width, bitmap.Height) * 0.11),
                18,
                48);
            var stride = Math.Max(8, patchSize / 2);
            var candidates = BuildCandidates(bitmap.Width, bitmap.Height, patchSize, stride);
            if (candidates.Count == 0)
                return new(true, "Không có vùng quét hợp lệ.", Array.Empty<ChampionIconDetection>());

            var tensor = new DenseTensor<float>(
                new[] { candidates.Count, 3, InputSize, InputSize });

            for (var i = 0; i < candidates.Count; i++)
            {
                using var patch = bitmap.Clone(
                    candidates[i].Bounds,
                    PixelFormat.Format24bppRgb);
                FillInput(patch, tensor, i);
            }

            var inputName = _session!.InputMetadata.Keys.Single();
            using var output = _session.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor(inputName, tensor)
            });

            var scores = output.First().AsTensor<float>();
            if (scores.Dimensions.Length != 2 ||
                scores.Dimensions[0] != candidates.Count ||
                scores.Dimensions[1] != _classes!.Length)
                throw new InvalidDataException(
                    "Đầu ra classifier phải có dạng [N,C] khớp file nhãn.");

            var raw = new List<ChampionIconDetection>();
            for (var i = 0; i < candidates.Count; i++)
            {
                var (classIndex, confidence) = BestSoftmax(scores, i);
                if (confidence < MinimumConfidence)
                    continue;

                var label = _classes[classIndex];
                if (label.Key.Equals("background", StringComparison.OrdinalIgnoreCase))
                    continue;

                var point = candidates[i];
                raw.Add(new(
                    label.Champion,
                    label.Team,
                    point.CenterX / (double)bitmap.Width,
                    point.CenterY / (double)bitmap.Height,
                    confidence));
            }

            var detections = SuppressNearbyDuplicates(raw, patchSize, bitmap.Width, bitmap.Height);
            return new(
                true,
                detections.Count == 0
                    ? "Mô hình chưa tìm thấy biểu tượng đủ tin cậy."
                    : $"Đã nhận diện {detections.Count} biểu tượng nhìn thấy trên minimap.",
                detections);
        }
        catch (Exception ex) when (
            ex is OnnxRuntimeException or
            IOException or
            InvalidDataException or
            ArgumentException or
            InvalidOperationException or
            ExternalException)
        {
            return new(
                true,
                "Không chạy được model biểu tượng: " + ex.Message,
                Array.Empty<ChampionIconDetection>());
        }
    }

    private bool EnsureLoaded()
    {
        if (_session is not null && _classes is not null)
            return true;
        if (_loadError is not null)
            return false;

        try
        {
            var labels = JsonSerializer.Deserialize<ChampionLabelsFile>(
                File.ReadAllText(LabelsPath))
                ?? throw new InvalidDataException("File nhãn rỗng.");
            if (labels.SchemaVersion != 1 ||
                labels.InputSize != InputSize ||
                labels.Classes is null ||
                labels.Classes.Length < 2 ||
                labels.Classes.Select(x => x.Index).Distinct().Count() != labels.Classes.Length ||
                labels.Classes.Any(x => x.Index < 0 ||
                    string.IsNullOrWhiteSpace(x.Key) ||
                    x.Champion.Length > 50 ||
                    x.Team is not ("ally" or "enemy" or "unknown")))
                throw new InvalidDataException("File nhãn biểu tượng không hợp lệ.");

            var ordered = labels.Classes.OrderBy(x => x.Index).ToArray();
            if (ordered.Select((x, i) => x.Index == i).Any(ok => !ok) ||
                !ordered.Any(x => x.Key.Equals("background", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Chỉ số lớp phải liên tục và có lớp background.");

            using var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = 2
            };
            var session = new InferenceSession(ModelPath, options);
            if (session.InputMetadata.Count != 1)
            {
                session.Dispose();
                throw new InvalidDataException("Classifier phải có đúng một đầu vào.");
            }

            var dims = session.InputMetadata.Values.Single().Dimensions;
            if (dims.Length != 4 ||
                dims[1] != 3 ||
                dims[2] != InputSize ||
                dims[3] != InputSize)
            {
                session.Dispose();
                throw new InvalidDataException(
                    "Classifier phải nhận ảnh NCHW [N,3,64,64].");
            }

            _classes = ordered;
            _session = session;
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            OnnxRuntimeException or
            InvalidDataException or
            ArgumentException)
        {
            _loadError = ex.Message;
            return false;
        }
    }

    private static List<PatchCandidate> BuildCandidates(
        int width,
        int height,
        int patchSize,
        int stride)
    {
        var result = new List<PatchCandidate>();
        var half = patchSize / 2;
        for (var y = half; y < height - half; y += stride)
        for (var x = half; x < width - half; x += stride)
        {
            result.Add(new(
                new Rectangle(
                    x - half,
                    y - half,
                    patchSize,
                    patchSize),
                x,
                y));
        }
        return result;
    }

    private static void FillInput(
        Bitmap source,
        DenseTensor<float> tensor,
        int batch)
    {
        using var resized = new Bitmap(
            InputSize,
            InputSize,
            PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, InputSize, InputSize));
        }

        var bounds = new Rectangle(0, 0, InputSize, InputSize);
        var data = resized.LockBits(
            bounds,
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bytes = new byte[stride * InputSize];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            for (var y = 0; y < InputSize; y++)
            for (var x = 0; x < InputSize; x++)
            {
                var offset = y * stride + x * 3;
                tensor[batch, 0, y, x] = bytes[offset + 2] / 255f;
                tensor[batch, 1, y, x] = bytes[offset + 1] / 255f;
                tensor[batch, 2, y, x] = bytes[offset] / 255f;
            }
        }
        finally
        {
            resized.UnlockBits(data);
        }
    }

    private static (int Index, float Confidence) BestSoftmax(
        Tensor<float> scores,
        int row)
    {
        var classes = scores.Dimensions[1];
        var max = float.NegativeInfinity;
        for (var i = 0; i < classes; i++)
            max = Math.Max(max, scores[row, i]);

        double sum = 0;
        var bestIndex = 0;
        var best = double.NegativeInfinity;
        for (var i = 0; i < classes; i++)
        {
            var value = Math.Exp(scores[row, i] - max);
            sum += value;
            if (value > best)
            {
                best = value;
                bestIndex = i;
            }
        }

        return (
            bestIndex,
            sum > 0 ? (float)(best / sum) : 0f);
    }

    private static IReadOnlyList<ChampionIconDetection> SuppressNearbyDuplicates(
        IReadOnlyList<ChampionIconDetection> raw,
        int patchSize,
        int width,
        int height)
    {
        var chosen = new List<ChampionIconDetection>();
        var minDistance = Math.Max(
            patchSize / (double)Math.Max(width, height) * 0.75,
            0.025);

        foreach (var item in raw.OrderByDescending(x => x.Confidence))
        {
            if (chosen.Any(existing =>
                existing.Champion.Equals(item.Champion, StringComparison.OrdinalIgnoreCase) &&
                existing.Team == item.Team &&
                Math.Sqrt(
                    Math.Pow(existing.X - item.X, 2) +
                    Math.Pow(existing.Y - item.Y, 2)) < minDistance))
                continue;

            chosen.Add(item);
            if (chosen.Count >= 10)
                break;
        }

        return chosen;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }

    private sealed record PatchCandidate(
        Rectangle Bounds,
        int CenterX,
        int CenterY);

    private sealed record ChampionLabelsFile(
        int SchemaVersion,
        int InputSize,
        ChampionClass[] Classes);

    private sealed record ChampionClass(
        int Index,
        string Key,
        string Champion,
        string Team);
}
