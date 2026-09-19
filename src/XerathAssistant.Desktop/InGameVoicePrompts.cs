namespace XerathAssistant.Desktop;

/// <summary>
/// Fixed Vietnamese speech prompts generated before play and cached on the user's PC.
/// Does not synthesize sound from screen pixels, names, private game data or live LLM text.
/// </summary>
public static class InGameVoicePrompts
{
    public const string Died = "Bạn đã bị hạ gục.";
    public const string Respawned = "Xerath đã hồi sinh.";
    public const string HealthLoss = "Máu của bạn vừa giảm nhanh.";
    public const string HealthLow = "Máu của bạn đã xuống thấp.";
    public const string ManaLow = "Năng lượng của bạn đã xuống thấp.";
    public const string GoldHigh = "Vàng hiện có đã đạt ngưỡng nhắc.";
    public const string CompletedKill = "Vừa có một điểm hạ gục được ghi nhận.";
    public const string DangerElevated = "Máu bạn vừa giảm mạnh. Hãy chú ý.";
    public const string DangerHigh = "Cảnh báo nguy hiểm. Máu của bạn đang xuống nhanh.";
    public const string DangerCritical = "Nguy hiểm cao. Máu của bạn đang rất thấp.";

    public static readonly string[] All =
    {
        Died, Respawned, HealthLoss, HealthLow, ManaLow, GoldHigh, CompletedKill,
        DangerElevated, DangerHigh, DangerCritical
    };
}
