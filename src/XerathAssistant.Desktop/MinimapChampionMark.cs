namespace XerathAssistant.Desktop;

/// <summary>
/// A pixel-accurate manual point on the displayed JPEG, normalized into [0,1].
/// The name/team/role are user assertions; they are NOT automatically verified.
/// </summary>
public sealed record MinimapChampionMark(
    double X, double Y, string Champion, string Team, string Role)
{
    public static MinimapChampionMark Checked(double x, double y,
        string champion, string team, string role)
    {
        champion = champion.Trim();
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            x < 0 || x > 1 || y < 0 || y > 1 ||
            champion.Length is < 2 or > 50 ||
            team is not ("ally" or "enemy" or "unknown") ||
            role is not ("top" or "jungle" or "mid" or "bottom" or "support" or "unknown"))
            throw new ArgumentException("Điểm đánh dấu, tên tướng, đội hoặc vai trò không hợp lệ.");
        return new MinimapChampionMark(x, y, champion, team, role);
    }
}
