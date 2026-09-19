using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace XerathAssistant.Desktop;

/// <summary>
/// Local, non-ML detector of the visible LoL minimap rectangle.
/// It scans a bounded bottom-right region, looking for a square panel with
/// a distinct surrounding frame and varied interior. It NEVER proves an icon
/// or enemy location; the user must inspect and confirm the proposed crop.
/// </summary>
internal static class MinimapAutoCropDetector
{
    internal sealed record Result(MinimapCropProfile Crop, double Evidence, bool Confident);

    // A usable FALLBACK candidate when border recognition is inconclusive.
    // This only sets a suggested preview: never authorize cloud uploads.
    internal static MinimapCropProfile CornerSuggestion(int width, int height)
    {
        var side = Math.Clamp((int)Math.Round(height * .255), 120,
            Math.Min((int)(height * .34), (int)(width * .25)));
        var rightGap = Math.Max(2, (int)(width * .006));
        var bottomGap = Math.Max(2, (int)(height * .008));
        return Profile(width, height, width - rightGap - side,
            height - bottomGap - side, side, side);
    }

    internal static Result? Detect(Bitmap frame)
    {
        if (frame.Width < 640 || frame.Height < 400) return null;

        // Shrink *only in RAM* to keep the scan bounded and responsive.
        const int targetWidth = 420;
        var scale = targetWidth / (double)frame.Width;
        var w = targetWidth;
        var h = Math.Max(100, (int)Math.Round(frame.Height * scale));
        using var reduced = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(reduced))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(frame, new Rectangle(0, 0, w, h));
        }

        var shortest = Math.Min(w, h);
        var minSide = Math.Max(24, (int)(shortest * .18));
        var maxSide = Math.Min((int)(shortest * .33), (int)(w * .25));
        if (maxSide < minSide) return null;

        double bestScore = double.NegativeInfinity;
        Rectangle best = Rectangle.Empty;
        // The panel should end close to the bottom/right of the client area;
        // using a larger search area here leads to picking the ability HUD.
        for (var side = minSide; side <= maxSide; side += 2)
        for (var rightGap = 0; rightGap <= Math.Max(4, (int)(w * .025)); rightGap += 2)
        for (var bottomGap = 0; bottomGap <= Math.Max(4, (int)(h * .035)); bottomGap += 2)
        {
            var x = w - rightGap - side;
            var y = h - bottomGap - side;
            if (x < (int)(w * .69) || y < (int)(h * .61)) continue;
            var candidate = new Rectangle(x, y, side, side);
            var score = Score(reduced, candidate);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best == Rectangle.Empty) return null;
        var crop = Profile(w, h, best.X, best.Y, best.Width, best.Height);
        // This is a conservative visual-structure heuristic, not a calibrated
        // ML confidence. All results are untrusted until the user confirms.
        var confident = bestScore >= .47;
        return new Result(crop, Math.Clamp(bestScore, 0, 1), confident);
    }

    private static MinimapCropProfile Profile(int w, int h, int x, int y, int width, int height) =>
        new(Math.Clamp(x / (double)w, 0, 1),
            Math.Clamp(y / (double)h, 0, 1),
            Math.Clamp(width / (double)w, .08, 1),
            Math.Clamp(height / (double)h, .08, 1));

    private static double Score(Bitmap bitmap, Rectangle r)
    {
        // For an actual minimap, most of the upper and left sides form a
        // persistent visual boundary between the HUD panel and world scene.
        var borderGradient = 0d;
        var borderDarkness = 0d;
        var n = 0;
        var shift = Math.Max(2, r.Width / 20);
        for (var i = 1; i <= 12; i++)
        {
            var fraction = i / 13d;
            var xx = r.Left + (int)((r.Width - 1) * fraction);
            var yy = r.Top + (int)((r.Height - 1) * fraction);
            if (r.Top - shift < 0 || r.Left - shift < 0 ||
                r.Top + shift >= bitmap.Height || r.Left + shift >= bitmap.Width)
                continue;

            var top = Luma(bitmap.GetPixel(xx, r.Top));
            var insideTop = Luma(bitmap.GetPixel(xx, r.Top + shift));
            var outsideTop = Luma(bitmap.GetPixel(xx, r.Top - shift));
            var left = Luma(bitmap.GetPixel(r.Left, yy));
            var insideLeft = Luma(bitmap.GetPixel(r.Left + shift, yy));
            var outsideLeft = Luma(bitmap.GetPixel(r.Left - shift, yy));
            // Distinct changes across both sides are more informative than a
            // black region by itself (a blank loading screen can be black).
            borderGradient += Math.Abs(insideTop - outsideTop) +
                Math.Abs(insideLeft - outsideLeft);
            borderDarkness += (1 - top) + (1 - left);
            n += 2;
        }
        if (n < 14) return 0;

        // Bounded 7x7 samples measure whether the rectangle contains terrain
        // rather than a uniform black or solid-color HUD panel.
        double mean = 0, squared = 0, transitions = 0;
        var interior = new double[7, 7];
        for (var y = 0; y < 7; y++)
        for (var x = 0; x < 7; x++)
        {
            var px = r.Left + (int)(r.Width * (.10 + .80 * x / 6));
            var py = r.Top + (int)(r.Height * (.10 + .80 * y / 6));
            var value = Luma(bitmap.GetPixel(px, py));
            interior[x, y] = value;
            mean += value;
            squared += value * value;
            if (x > 0) transitions += Math.Abs(value - interior[x - 1, y]);
            if (y > 0) transitions += Math.Abs(value - interior[x, y - 1]);
        }

        mean /= 49;
        var deviation = Math.Sqrt(Math.Max(0, squared / 49 - mean * mean));
        var edge = Math.Clamp(borderGradient / n / .35, 0, 1);
        var frameDark = Math.Clamp(borderDarkness / n, 0, 1);
        var texture = Math.Clamp(deviation / .18, 0, 1);
        var detail = Math.Clamp(transitions / 84 / .18, 0, 1);
        // Downweight very dark / blank content and favor the panel's typical
        // high-contrast border + non-uniform map interior.
        if (mean < .06 || texture < .12) return 0;
        return edge * .40 + frameDark * .13 + texture * .24 + detail * .23;
    }

    private static double Luma(Color c) =>
        (.2126 * c.R + .7152 * c.G + .0722 * c.B) / 255d;
}
