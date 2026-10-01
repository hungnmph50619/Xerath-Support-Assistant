using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace XerathAssistant.Desktop;

public sealed record MinimapRuntimeFrame(
    byte[] Jpeg,
    DateTimeOffset CapturedAtUtc,
    int Width,
    int Height)
{
    public void Clear()
    {
        if (Jpeg.Length > 0)
            Array.Clear(Jpeg, 0, Jpeg.Length);
    }
}

public sealed record MinimapRuntimeCaptureResult(
    bool Captured,
    string Status,
    MinimapRuntimeFrame? Frame);

/// <summary>
/// Local-only minimap runtime capture. Requires the user-confirmed crop profile,
/// only reads pixels while the League game client is foreground, keeps no files
/// and never performs network calls.
/// </summary>
public sealed class MinimapRuntimeCaptureService
{
    private const int MaximumJpegBytes = 2 * 1024 * 1024;
    private readonly MinimapCropProfileStore _cropStore = new();

    public MinimapRuntimeCaptureResult Capture()
    {
        var profile = _cropStore.Load();
        if (!profile.IsValid ||
            profile.ConfirmedClientWidth < 640 ||
            profile.ConfirmedClientHeight < 400)
        {
            return new(
                false,
                "Chưa có khung minimap đã xác nhận.",
                null);
        }

        if (!TryGetForegroundGameClient(out var client))
        {
            return new(
                false,
                "Cửa sổ trận Liên Minh chưa được chọn.",
                null);
        }

        if (!profile.MatchesConfirmedResolution(client))
        {
            return new(
                false,
                "Kích thước cửa sổ game đã thay đổi; cần xác nhận lại khung minimap.",
                null);
        }

        var region = profile.Crop(client);
        region.Intersect(client);
        region.Intersect(
            System.Windows.Forms.SystemInformation.VirtualScreen);
        if (region.Width < 90 || region.Height < 90)
        {
            return new(
                false,
                "Vùng minimap không hợp lệ hoặc nằm ngoài màn hình.",
                null);
        }

        try
        {
            using var bitmap = new Bitmap(
                region.Width,
                region.Height,
                PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    region.Location,
                    Point.Empty,
                    region.Size,
                    CopyPixelOperation.SourceCopy);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Jpeg);
            if (stream.Length is < 24 or > MaximumJpegBytes)
            {
                return new(
                    false,
                    "Ảnh minimap trong RAM có kích thước không hợp lệ.",
                    null);
            }

            return new(
                true,
                "Đã chụp minimap vào RAM.",
                new(
                    stream.ToArray(),
                    DateTimeOffset.UtcNow,
                    region.Width,
                    region.Height));
        }
        catch (Exception ex) when (
            ex is ExternalException or
            ArgumentException or
            InvalidOperationException)
        {
            return new(
                false,
                "Không chụp được minimap: " + ex.Message,
                null);
        }
    }

    internal static bool TryGetForegroundGameClient(
        out Rectangle client)
    {
        client = Rectangle.Empty;
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return false;

        _ = GetWindowThreadProcessId(window, out var pid);
        if (pid == 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(
                unchecked((int)pid));
            if (!string.Equals(
                    process.ProcessName,
                    "League of Legends",
                    StringComparison.OrdinalIgnoreCase))
                return false;
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return false;
        }

        if (!GetClientRect(window, out var rect))
            return false;

        var topLeft = new NativePoint { X = 0, Y = 0 };
        if (!ClientToScreen(window, ref topLeft))
            return false;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 640 || height < 400)
            return false;

        client = new Rectangle(
            topLeft.X,
            topLeft.Y,
            width,
            height);
        return true;
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
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(
        IntPtr window,
        out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(
        IntPtr window,
        ref NativePoint point);
}
