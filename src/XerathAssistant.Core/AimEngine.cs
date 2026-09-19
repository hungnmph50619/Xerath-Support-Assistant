using System.Numerics;

namespace XerathAssistant.Core;

/// <summary>
/// Educational, screen-pixel simulation. Values below are deliberately NOT League patch stats.
/// The algorithm and collision primitives can be reused with separately calibrated data.
/// </summary>
public enum Spell { Q, W, E, R }

public sealed record TargetObservation(Vector2 Position, Vector2 Velocity);

public sealed record AimRequest(
    Vector2 Caster,
    TargetObservation Target,
    Spell Spell,
    float QChargeSeconds = 0f,
    Vector2? Blocker = null);

public sealed record AimResult(
    Vector2 AimPoint,
    float TimeToImpact,
    float Range,
    float HitRadius,
    bool InRange,
    bool Blocked,
    string Explanation);

public static class AimEngine
{
    public static AimResult Predict(AimRequest request)
    {
        var caster = request.Caster;
        var target = request.Target;
        var charge = Math.Clamp(request.QChargeSeconds, 0f, 1.5f);
        float range, radius, time;
        Vector2 aim;
        string explanation;

        switch (request.Spell)
        {
            case Spell.Q:
                range = 365f + 190f * (charge / 1.5f);
                radius = 22f; // Half of simulated line width.
                time = 0.30f; // Release -> damage; charge is already elapsed.
                aim = target.Position + target.Velocity * time;
                explanation = "Q: dự đoán vị trí sau độ trễ phóng; đường thẳng xuyên lính.";
                break;
            case Spell.W:
                range = 650f;
                radius = 49f; // Outer-zone practice radius, not true in-game size.
                time = 0.75f;
                aim = target.Position + target.Velocity * time;
                explanation = "W: đặt tâm vòng tròn tại vị trí dự đoán khi vùng nổ kích hoạt.";
                break;
            case Spell.E:
                range = 605f;
                radius = 19f; // Simulated projectile + target collision allowance.
                const float windup = 0.16f;
                const float projectileSpeed = 480f;
                var targetAtLaunch = target.Position + target.Velocity * windup;
                if (TryIntercept(caster, targetAtLaunch, target.Velocity, projectileSpeed, out var flight))
                {
                    time = windup + flight;
                    aim = target.Position + target.Velocity * time;
                    explanation = "E: tính giao điểm với viên đạn bay; vật cản trên đường có thể chặn E.";
                }
                else
                {
                    time = windup;
                    aim = targetAtLaunch;
                    explanation = "E: không tìm được giao điểm với vận tốc hiện tại của mục tiêu.";
                }
                break;
            case Spell.R:
                range = 950f;
                radius = 57f;
                time = 0.88f;
                aim = target.Position + target.Velocity * time;
                explanation = "R: dự đoán tâm vụ nổ cho một phát; cần tính lại ở phát tiếp theo.";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request.Spell));
        }

        var distance = Vector2.Distance(caster, aim);
        // A line/circle can clip a target at its edge, but this practice prototype uses
        // center-in-range for clarity. In-game geometry needs separately calibrated data.
        var inRange = distance <= range;
        var blocked = request.Spell == Spell.E && request.Blocker is { } blocker &&
                      DistanceToSegment(blocker, caster, aim) <= 21f &&
                      Vector2.Distance(caster, blocker) < distance;
        if (!inRange) explanation += " Ngoài tầm trong mô phỏng.";
        if (blocked) explanation += " Lính mô phỏng đang chắn E.";
        return new AimResult(aim, time, range, radius, inRange, blocked, explanation);
    }

    public static bool IsHit(Spell spell, Vector2 caster, Vector2 aim, Vector2 actualTarget,
        float range, float radius, Vector2? blocker = null)
    {
        if (Vector2.Distance(caster, aim) > range) return false;
        var hit = spell is Spell.Q or Spell.E
            ? DistanceToSegment(actualTarget, caster, aim) <= radius &&
              Vector2.Dot(actualTarget - caster, aim - caster) >= 0 &&
              Vector2.Distance(caster, actualTarget) <= range + radius
            : Vector2.Distance(aim, actualTarget) <= radius;
        if (spell == Spell.E && blocker is { } b &&
            DistanceToSegment(b, caster, aim) <= 21f &&
            Vector2.Distance(caster, b) < Vector2.Distance(caster, actualTarget)) return false;
        return hit;
    }

    public static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSq = segment.LengthSquared();
        if (lengthSq < 1e-7f) return Vector2.Distance(point, start);
        var t = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSq, 0f, 1f);
        return Vector2.Distance(point, start + segment * t);
    }

    /// <summary>Solve |relative + targetVelocity*t| = projectileSpeed*t, t >= 0.</summary>
    public static bool TryIntercept(Vector2 origin, Vector2 target, Vector2 velocity,
        float projectileSpeed, out float seconds)
    {
        seconds = 0f;
        if (projectileSpeed <= 0f) return false;
        var relative = target - origin;
        var a = Vector2.Dot(velocity, velocity) - projectileSpeed * projectileSpeed;
        var b = 2f * Vector2.Dot(relative, velocity);
        var c = Vector2.Dot(relative, relative);
        if (c < 1e-7f) return true;
        if (Math.Abs(a) < 1e-6f)
        {
            if (Math.Abs(b) < 1e-6f) return false;
            var linearT = -c / b;
            if (linearT < 0f) return false;
            seconds = linearT;
            return true;
        }
        var disc = b * b - 4f * a * c;
        if (disc < 0f) return false;
        var root = MathF.Sqrt(disc);
        var t1 = (-b - root) / (2f * a);
        var t2 = (-b + root) / (2f * a);
        var best = MathF.Min(t1 >= 0f ? t1 : float.PositiveInfinity,
                             t2 >= 0f ? t2 : float.PositiveInfinity);
        if (!float.IsFinite(best)) return false;
        seconds = best;
        return true;
    }
}
