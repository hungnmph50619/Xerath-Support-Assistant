using System.Drawing;
using System.IO;
using System.Text.Json;

namespace XerathAssistant.Desktop;

/// <summary>
/// User-calibrated minimap crop in normalized game-client coordinates.
/// Only stores 4 numbers and the last acknowledged client size; no image pixels.
/// </summary>
public sealed record MinimapCropProfile(
    double Left = .78, double Top = .70, double Width = .22, double Height = .30,
    int ConfirmedClientWidth = 0, int ConfirmedClientHeight = 0)
{
    public static MinimapCropProfile Default => new();

    public bool IsValid =>
        double.IsFinite(Left) && double.IsFinite(Top) &&
        double.IsFinite(Width) && double.IsFinite(Height) &&
        Left >= 0 && Top >= 0 && Width >= .08 && Height >= .08 &&
        Left + Width <= 1.000001 && Top + Height <= 1.000001;

    public Rectangle Crop(Rectangle client)
    {
        if (!IsValid || client.Width < 640 || client.Height < 400)
            throw new ArgumentException("Khung minimap hoặc kích thước cửa sổ game không hợp lệ.");
        return new Rectangle(
            client.Left + (int)Math.Round(client.Width * Left),
            client.Top + (int)Math.Round(client.Height * Top),
            Math.Max(1, (int)Math.Round(client.Width * Width)),
            Math.Max(1, (int)Math.Round(client.Height * Height)));
    }

    public bool MatchesConfirmedResolution(Rectangle client) =>
        ConfirmedClientWidth == client.Width && ConfirmedClientHeight == client.Height;
}

public sealed class MinimapCropProfileStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "minimap-crop-v1.8.json");

    public MinimapCropProfile Load()
    {
        try
        {
            if (!File.Exists(_path)) return MinimapCropProfile.Default;
            var profile = JsonSerializer.Deserialize<MinimapCropProfile>(File.ReadAllText(_path));
            return profile is { IsValid: true } ? profile : MinimapCropProfile.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return MinimapCropProfile.Default;
        }
    }

    public void Save(MinimapCropProfile profile)
    {
        if (!profile.IsValid || profile.ConfirmedClientWidth < 640 ||
            profile.ConfirmedClientHeight < 400)
            throw new ArgumentException("Hãy xem trước và xác nhận vùng cắt đúng minimap trước khi lưu.");
        var folder = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(folder);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(profile));
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
