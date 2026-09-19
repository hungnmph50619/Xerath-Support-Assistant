using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace XerathAssistant.Desktop;

/// <summary>
/// Chạy mô hình YOLOv8/YOLO11 một lớp ("minimap") trên máy. Không gọi mạng.
/// Không có mô hình ONNX thì KHÔNG coi heuristic là kết quả của AI.
/// Dữ liệu huấn luyện và suy luận đều là ROI góc dưới bên phải cửa sổ game.
/// </summary>
internal static class MinimapLocalAiDetector
{
    internal const int InputSize = 640;
    private const float MinimumScore = .60f;
    internal static readonly string ModelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "models", "minimap-detector.onnx");
    internal static readonly string TrainingFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "minimap-ai-training");

    internal static bool ModelAvailable => File.Exists(ModelPath);

    // Luôn giữ cùng một ROI khi thu ảnh huấn luyện và lúc suy luận.
    internal static Rectangle SearchRegion(int width, int height)
    {
        var left = (int)Math.Floor(width * .45);
        var top = (int)Math.Floor(height * .45);
        return new Rectangle(left, top, width - left, height - top);
    }

    internal static bool TryDetect(Bitmap frame, out MinimapCropProfile crop,
        out string explanation)
    {
        crop = MinimapCropProfile.Default;
        explanation = "";
        if (!ModelAvailable)
        {
            explanation = "Chưa cài mô hình AI. Hãy thu và gắn nhãn ảnh, " +
                "huấn luyện rồi đặt minimap-detector.onnx vào thư mục models trong LocalAppData. " +
                "Bạn vẫn có thể chỉnh khung thủ công; ứng dụng không tự dùng lại bộ dò cũ.";
            return false;
        }
        if (frame.Width < 640 || frame.Height < 400 ||
            (long)frame.Width * frame.Height > 12_000_000)
        {
            explanation = "Kích thước cửa sổ game không phù hợp để chạy mô hình.";
            return false;
        }

        try
        {
            var area = SearchRegion(frame.Width, frame.Height);
            using var roi = frame.Clone(area, PixelFormat.Format24bppRgb);
            using var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = 2
            };
            using var session = new InferenceSession(ModelPath, options);
            if (session.InputMetadata.Count != 1)
                throw new InvalidDataException("Mô hình phải có đúng một đầu vào hình ảnh.");

            var inputName = session.InputMetadata.Keys.Single();
            var dimensions = session.InputMetadata[inputName].Dimensions;
            if (dimensions.Length != 4 || dimensions[0] != 1 ||
                dimensions[1] != 3 || dimensions[2] != InputSize ||
                dimensions[3] != InputSize)
                throw new InvalidDataException("Mô hình phải dùng đầu vào NCHW [1,3,640,640].");

            var input = MakeInput(roi, out var scale, out var padX, out var padY);
            using var output = session.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor(inputName, input)
            });
            var predictions = output.First().AsTensor<float>();
            var shape = predictions.Dimensions;
            if (shape.Length != 3 || shape[0] != 1 || shape[1] != 5 ||
                shape[2] < 1 || shape[2] > 20000)
                throw new InvalidDataException(
                    "Mô hình phải xuất tensor YOLO một lớp [1,5,N], không dùng NMS.");
            var topScore = 0f;
            Rectangle chosen = Rectangle.Empty;
            for (var i = 0; i < shape[2]; i++)
            {
                var score = predictions[0, 4, i];
                if (!float.IsFinite(score) || score < MinimumScore || score <= topScore)
                    continue;
                var centerX = predictions[0, 0, i];
                var centerY = predictions[0, 1, i];
                var boxW = predictions[0, 2, i];
                var boxH = predictions[0, 3, i];
                if (!float.IsFinite(centerX) || !float.IsFinite(centerY) ||
                    !float.IsFinite(boxW) || !float.IsFinite(boxH) ||
                    boxW <= 0 || boxH <= 0)
                    continue;

                // YOLO xuất tọa độ pixel trong ảnh letterbox 640x640.
                var x = (centerX - boxW / 2f - padX) / scale;
                var y = (centerY - boxH / 2f - padY) / scale;
                var width = boxW / scale;
                var height = boxH / scale;
                if (x < 0 || y < 0 || x + width > roi.Width ||
                    y + height > roi.Height)
                    continue;
                var rectangle = new Rectangle(
                    area.Left + (int)Math.Round(x), area.Top + (int)Math.Round(y),
                    (int)Math.Round(width), (int)Math.Round(height));
                if (rectangle.Width < 90 || rectangle.Height < 90 ||
                    rectangle.Left < frame.Width * .55 ||
                    rectangle.Top < frame.Height * .50 ||
                    rectangle.Right > frame.Width || rectangle.Bottom > frame.Height)
                    continue;
                var ratio = rectangle.Width / (double)rectangle.Height;
                if (ratio is < .75 or > 1.25)
                    continue;
                topScore = score;
                chosen = rectangle;
            }

            if (chosen == Rectangle.Empty)
            {
                explanation = "AI không tìm được minimap đủ rõ (ngưỡng điểm 0,60). " +
                    "Không có khung nào được tự chấp nhận; hãy thu thêm ảnh huấn luyện hoặc chỉnh tay.";
                return false;
            }
            crop = new MinimapCropProfile(
                Math.Round(chosen.Left / (double)frame.Width, 3),
                Math.Round(chosen.Top / (double)frame.Height, 3),
                Math.Round(chosen.Width / (double)frame.Width, 3),
                Math.Round(chosen.Height / (double)frame.Height, 3));
            if (!crop.IsValid)
            {
                explanation = "Khung AI dự đoán không hợp lệ; không sử dụng.";
                return false;
            }
            explanation = $"AI cục bộ đề xuất khung (điểm mô hình {topScore:0.00}). " +
                "Điểm này không chứng minh khung đúng: hãy nhìn ảnh trước khi xác nhận.";
            return true;
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or IOException or
                                   UnauthorizedAccessException or InvalidDataException or
                                   ArgumentException or InvalidOperationException or
                                   DllNotFoundException or BadImageFormatException)
        {
            explanation = "Không chạy được mô hình AI cục bộ: " + ex.Message +
                ". Không chuyển âm thầm sang heuristic.";
            return false;
        }
    }

    private static DenseTensor<float> MakeInput(Bitmap source, out float scale,
        out int padX, out int padY)
    {
        scale = Math.Min(InputSize / (float)source.Width,
            InputSize / (float)source.Height);
        var resizedWidth = Math.Clamp((int)Math.Round(source.Width * scale), 1, InputSize);
        var resizedHeight = Math.Clamp((int)Math.Round(source.Height * scale), 1, InputSize);
        // Tọa độ ngược phải khớp chính xác phép co giãn thực tế.
        scale = resizedWidth / (float)source.Width;
        if (Math.Abs(resizedHeight / (float)source.Height - scale) > .002)
            throw new InvalidDataException("Tỉ lệ letterbox không nhất quán.");
        padX = (InputSize - resizedWidth) / 2;
        padY = (InputSize - resizedHeight) / 2;

        using var square = new Bitmap(InputSize, InputSize, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(square))
        {
            graphics.Clear(Color.FromArgb(114, 114, 114));
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(source, new Rectangle(padX, padY, resizedWidth, resizedHeight));
        }
        var rgb = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
        var bounds = new Rectangle(0, 0, InputSize, InputSize);
        var locked = square.LockBits(bounds, ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(locked.Stride);
            var bytes = new byte[stride * InputSize];
            Marshal.Copy(locked.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < InputSize; y++)
            for (var x = 0; x < InputSize; x++)
            {
                var offset = y * stride + x * 3;
                rgb[0, 0, y, x] = bytes[offset + 2] / 255f; // R
                rgb[0, 1, y, x] = bytes[offset + 1] / 255f; // G
                rgb[0, 2, y, x] = bytes[offset] / 255f;     // B
            }
        }
        finally { square.UnlockBits(locked); }
        return rgb;
    }
}
