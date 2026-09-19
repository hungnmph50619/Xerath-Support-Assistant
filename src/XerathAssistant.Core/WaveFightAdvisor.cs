namespace XerathAssistant.Core;

/// <summary>
/// Offline, user-entered replay/scenario analysis. This engine NEVER reads live game state.
/// Unknown inputs block positive recommendations; conclusions are conditional heuristics,
/// not proofs of advantage or automatic decisions.
/// </summary>
public enum WaveLocation { Unknown, NearAllyTower, AllyHalf, Middle, EnemyHalf, NearEnemyTower }
public enum WaveDirection { Unknown, TowardAlly, Stable, TowardEnemy }
public enum AdcPlan { Unknown, Hold, PushSlow, CrashThenRecall, NoPlan }
public enum Condition { Unknown, Low, Healthy }
public enum VisionState { Unknown, Dark, Controlled }
public enum JungleState { Unknown, RecentlySeenBot, NoRecentSighting, RecentlySeenTop }
public enum AllyState { Unknown, Together, Separated }
public enum NumbersState { Unknown, Outnumbered, Even, Advantage }
public enum EnemyEngageState { Unknown, Available, RecentlyUsed }
public enum MinionState { Unknown, EnemyLarge, Even, AllyLarge }
public enum ObjectiveState { Unknown, Soon, NotSoon }
public enum AdviceState { InsufficientInformation, Caution, ConditionalOpportunity, Evaluate }

public sealed record WaveFightScenario(
    WaveLocation WaveLocation,
    WaveDirection WaveDirection,
    AdcPlan AdcPlan,
    Condition Health,
    Condition Mana,
    VisionState Vision,
    JungleState EnemyJungle,
    AllyState AllyPosition,
    NumbersState NearbyNumbers,
    EnemyEngageState EnemyEngage,
    MinionState MinionPressure,
    ObjectiveState Objective);

public sealed record ScenarioAdvice(
    AdviceState State,
    string Heading,
    string Explanation,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Missing);

public sealed record WaveFightResult(
    ScenarioAdvice Wave,
    ScenarioAdvice Position,
    ScenarioAdvice Fight);

public static class WaveFightAdvisor
{
    private static ScenarioAdvice MissingInfo(string topic, params string[] fields) =>
        new(AdviceState.InsufficientInformation,
            topic + ": chưa đủ thông tin để nhận định",
            "Bổ sung các thông tin chưa rõ. Không suy đoán dựa trên vị trí rừng đã cũ hoặc điều kiện chưa quan sát được.",
            Array.Empty<string>(), fields);

    private static ScenarioAdvice Advice(AdviceState state, string heading, string explanation,
        params string[] evidence) =>
        new(state, heading, explanation, evidence, Array.Empty<string>());

    public static WaveFightResult Evaluate(WaveFightScenario s) =>
        new(EvaluateWave(s), EvaluatePosition(s), EvaluateFight(s));

    private static ScenarioAdvice EvaluateWave(WaveFightScenario s)
    {
        var missing = new List<string>();
        if (s.WaveLocation == WaveLocation.Unknown) missing.Add("Vị trí đợt lính");
        if (s.WaveDirection == WaveDirection.Unknown) missing.Add("Hướng di chuyển của lính");
        if (s.AdcPlan == AdcPlan.Unknown) missing.Add("Ý định của ADC");
        if (missing.Count > 0) return MissingInfo("Thế lính", missing.ToArray());

        if (s.AdcPlan == AdcPlan.Hold)
            return Advice(AdviceState.Caution, "Thế lính: tôn trọng kế hoạch giữ lính của ADC",
                "Không tự ý dùng Q/W dọn lính. Xem xét thế lính và phối hợp với ADC trước khi thay đổi nhịp đẩy.",
                "ADC muốn giữ lính.", "Hướng lính: " + WaveDirectionLabel(s.WaveDirection));

        if (s.WaveDirection == WaveDirection.TowardAlly &&
            s.WaveLocation is WaveLocation.NearAllyTower or WaveLocation.AllyHalf)
            return Advice(AdviceState.Caution, "Thế lính: cân nhắc đón đợt lính về phía mình",
                "Đợt lính đang tiến về phía trụ nhà. Tránh vô tình phá thế lính bằng W/Q; đánh giá thêm mục tiêu của ADC.",
                "Lính đang đẩy về phía trụ nhà.", "Vị trí lính ở nửa sân nhà.");

        if (s.AdcPlan == AdcPlan.CrashThenRecall &&
            s.WaveDirection == WaveDirection.TowardEnemy)
        {
            var safetyMissing = new List<string>();
            if (s.Health == Condition.Unknown) safetyMissing.Add("Máu đồng minh");
            if (s.Mana == Condition.Unknown) safetyMissing.Add("Năng lượng Xerath");
            if (s.Vision == VisionState.Unknown) safetyMissing.Add("Tầm nhìn sông");
            if (s.EnemyJungle == JungleState.Unknown) safetyMissing.Add("Thông tin rừng địch");
            if (s.AllyPosition == AllyState.Unknown) safetyMissing.Add("Vị trí ADC");
            if (s.EnemyEngage == EnemyEngageState.Unknown) safetyMissing.Add("Kỹ năng mở giao tranh địch");
            if (safetyMissing.Count > 0) return MissingInfo("Đẩy lính rồi về", safetyMissing.ToArray());

            if (s.Health == Condition.Healthy && s.Mana == Condition.Healthy &&
                s.Vision == VisionState.Controlled &&
                s.EnemyJungle == JungleState.RecentlySeenTop &&
                s.AllyPosition == AllyState.Together &&
                s.EnemyEngage == EnemyEngageState.RecentlyUsed)
                return Advice(AdviceState.ConditionalOpportunity,
                    "Thế lính: có thể trao đổi với ADC về việc đẩy vào trụ rồi về",
                    "Đây chỉ là cơ hội có điều kiện: cần xác nhận đủ thời gian dọn hết lính, khả năng chống trả và thời điểm rừng địch hiện tại. Vị trí nhìn thấy trước đó không bảo đảm an toàn.",
                    "ADC dự định đẩy vào trụ rồi về.", "Lính đang tiến về phía đối phương.",
                    "Theo mô tả: còn tài nguyên, có tầm nhìn, đang đi cùng ADC.");

            return Advice(AdviceState.Caution,
                "Thế lính: chưa đủ điều kiện an toàn để đề xuất đẩy nhanh",
                "Kế hoạch về nhà cần cân đối với tầm nhìn, tài nguyên và nguy cơ bị mở giao tranh. Phối hợp với ADC; không mặc định cứ muốn về là nên dâng cao đẩy lính.",
                "ADC dự định đẩy vào trụ rồi về.", "Một hoặc nhiều điều kiện an toàn chưa thuận lợi.");
        }

        if (s.AdcPlan == AdcPlan.PushSlow && s.WaveDirection == WaveDirection.TowardEnemy)
            return Advice(AdviceState.Evaluate, "Thế lính: xem xét hỗ trợ đẩy chậm theo kế hoạch",
                "Cần tránh dùng W/Q làm lính chết quá nhanh nếu ADC muốn tích đợt lính. Kết hợp kiểm tra tầm nhìn trước khi đi theo đợt lính lên cao.",
                "ADC muốn đẩy chậm.", "Đợt lính đang đi về phía đối phương.");

        return Advice(AdviceState.Evaluate, "Thế lính: trao đổi mục tiêu với ADC trước khi can thiệp",
            "Thông tin hiện tại chưa tạo thành một phương án đẩy rõ ràng. Xem xét vị trí lính, mục tiêu về nhà và nhịp đi đường; hạn chế dùng Q/W làm thay đổi thế lính ngoài ý muốn.",
            "Ý định ADC: " + AdcPlanLabel(s.AdcPlan),
            "Hướng lính: " + WaveDirectionLabel(s.WaveDirection));
    }

    private static ScenarioAdvice EvaluatePosition(WaveFightScenario s)
    {
        var missing = new List<string>();
        if (s.WaveLocation == WaveLocation.Unknown) missing.Add("Vị trí đợt lính");
        if (s.Health == Condition.Unknown) missing.Add("Máu đồng minh");
        if (s.Mana == Condition.Unknown) missing.Add("Năng lượng Xerath");
        if (s.Vision == VisionState.Unknown) missing.Add("Tầm nhìn sông");
        if (s.EnemyJungle == JungleState.Unknown) missing.Add("Thông tin rừng địch");
        if (s.AllyPosition == AllyState.Unknown) missing.Add("Vị trí ADC");
        if (s.EnemyEngage == EnemyEngageState.Unknown) missing.Add("Kỹ năng mở giao tranh đối phương");
        if (missing.Count > 0) return MissingInfo("Vị trí đứng", missing.ToArray());

        if (s.Health == Condition.Low || s.Mana == Condition.Low ||
            s.AllyPosition == AllyState.Separated ||
            s.Vision == VisionState.Dark ||
            s.EnemyJungle is JungleState.RecentlySeenBot or JungleState.NoRecentSighting ||
            s.EnemyEngage == EnemyEngageState.Available)
            return Advice(AdviceState.Caution, "Vị trí đứng: hạn chế dâng cao chỉ để cấu rỉa",
                "Một hoặc nhiều rủi ro được người dùng ghi nhận. Cân nhắc đứng trong khoảng hỗ trợ ADC, giữ E khi cần và ưu tiên bổ sung tầm nhìn; không suy ra rừng địch đang ở đâu từ thông tin thiếu hoặc đã cũ.",
                "Tình trạng tầm nhìn: " + VisionLabel(s.Vision),
                "Thông tin rừng địch: " + JungleLabel(s.EnemyJungle),
                "Vị trí ADC: " + AllyLabel(s.AllyPosition));

        if (s.WaveLocation is WaveLocation.EnemyHalf or WaveLocation.NearEnemyTower)
            return Advice(AdviceState.Evaluate, "Vị trí đứng: cân nhắc cấu rỉa từ xa, không mặc định tiến thêm",
                "Dù các điều kiện đã nhập tương đối thuận lợi, đợt lính đang ở phía đối phương. Kiểm tra lại các ngả tiếp cận, vị trí ADC và độ mới của thông tin rừng trước mỗi lần tiến lên.",
                "Đợt lính ở phía đối phương.", "Có tầm nhìn theo dữ liệu người dùng nhập.");

        return Advice(AdviceState.ConditionalOpportunity,
            "Vị trí đứng: có thể xem xét tiến lên một khoảng để cấu rỉa",
            "Chỉ cân nhắc khi bạn vẫn ở trong tầm hỗ trợ ADC, có đường lùi và tự quan sát thấy đối phương chưa có cơ hội áp sát. Không coi thông tin rừng ở Top trước đó là chứng cứ Bot an toàn.",
            "Đồng minh đang đứng cùng nhau.", "Theo mô tả: còn tài nguyên và có tầm nhìn.",
            "Kỹ năng mở giao tranh đối phương vừa được dùng.");
    }

    private static ScenarioAdvice EvaluateFight(WaveFightScenario s)
    {
        var missing = new List<string>();
        if (s.Health == Condition.Unknown) missing.Add("Máu đồng minh");
        if (s.Mana == Condition.Unknown) missing.Add("Năng lượng Xerath");
        if (s.Vision == VisionState.Unknown) missing.Add("Tầm nhìn sông");
        if (s.EnemyJungle == JungleState.Unknown) missing.Add("Thông tin rừng địch");
        if (s.AllyPosition == AllyState.Unknown) missing.Add("Vị trí ADC");
        if (s.NearbyNumbers == NumbersState.Unknown) missing.Add("Tương quan số người đã thấy");
        if (s.EnemyEngage == EnemyEngageState.Unknown) missing.Add("Kỹ năng mở giao tranh đối phương");
        if (s.MinionPressure == MinionState.Unknown) missing.Add("Lượng lính hai bên");
        if (missing.Count > 0) return MissingInfo("Giao tranh", missing.ToArray());

        if (s.Health == Condition.Low || s.Mana == Condition.Low ||
            s.AllyPosition == AllyState.Separated ||
            s.NearbyNumbers == NumbersState.Outnumbered ||
            s.MinionPressure == MinionState.EnemyLarge ||
            s.Vision == VisionState.Dark ||
            s.EnemyJungle is JungleState.RecentlySeenBot or JungleState.NoRecentSighting)
            return Advice(AdviceState.Caution, "Giao tranh: cân nhắc giữ vị trí và tránh mở đánh đổi kéo dài",
                "Có một hoặc nhiều điều kiện bất lợi/không chắc chắn. Ưu tiên tự quan sát tình huống, giữ khả năng thoát và cân nhắc cấu rỉa tầm xa thay vì mặc định giao tranh có lợi.",
                "Tương quan người: " + NumbersLabel(s.NearbyNumbers),
                "Tình trạng lính: " + MinionLabel(s.MinionPressure),
                "Thông tin rừng: " + JungleLabel(s.EnemyJungle));

        if (s.NearbyNumbers == NumbersState.Advantage &&
            s.EnemyEngage == EnemyEngageState.RecentlyUsed &&
            s.Vision == VisionState.Controlled &&
            s.MinionPressure != MinionState.EnemyLarge &&
            s.EnemyJungle == JungleState.RecentlySeenTop)
            return Advice(AdviceState.ConditionalOpportunity,
                "Giao tranh: có thể cân nhắc trao đổi chiêu khi đồng đội sẵn sàng",
                "Theo mô tả, một số điều kiện có lợi đang đồng thời xuất hiện. Không thể kết luận giao tranh chắc thắng: cần tự kiểm tra máu đối phương, chiêu cuối, phép bổ trợ, vị trí thực tế và độ mới của thông tin rừng.",
                "Theo thông tin nhập: đang hơn người trong khu vực.",
                "Kỹ năng mở giao tranh địch vừa sử dụng.", "Có tầm nhìn và không đối mặt đợt lính địch lớn.");

        return Advice(AdviceState.Evaluate, "Giao tranh: chưa đủ cơ sở để khẳng định lợi thế",
            "Bạn có thể cân nhắc cấu rỉa và giữ E, sau đó tự đánh giá phản ứng đối phương. Tránh ép giao tranh chỉ vì hai đội đang bằng người hoặc vì rừng địch từng xuất hiện phía trên.",
            "Tương quan người: " + NumbersLabel(s.NearbyNumbers),
            "Kỹ năng mở giao tranh: " + (s.EnemyEngage == EnemyEngageState.Available ? "còn khả năng sử dụng" : "vừa sử dụng"));
    }

    private static string WaveDirectionLabel(WaveDirection v) => v switch
    {
        WaveDirection.TowardAlly => "về phía trụ nhà", WaveDirection.Stable => "cân bằng",
        WaveDirection.TowardEnemy => "về phía trụ đối phương", _ => "chưa rõ"
    };
    private static string AdcPlanLabel(AdcPlan v) => v switch
    {
        AdcPlan.Hold => "giữ lính", AdcPlan.PushSlow => "đẩy chậm",
        AdcPlan.CrashThenRecall => "đẩy vào trụ rồi về", _ => "chưa xác định"
    };
    private static string VisionLabel(VisionState v) => v switch
    {
        VisionState.Controlled => "có kiểm soát", VisionState.Dark => "thiếu tầm nhìn", _ => "chưa rõ"
    };
    private static string JungleLabel(JungleState v) => v switch
    {
        JungleState.RecentlySeenBot => "mới nhìn thấy gần Bot",
        JungleState.RecentlySeenTop => "mới nhìn thấy ở Top (không bảo đảm Bot an toàn)",
        JungleState.NoRecentSighting => "không có thông tin gần đây", _ => "chưa rõ"
    };
    private static string AllyLabel(AllyState v) => v switch
    {
        AllyState.Together => "đang ở gần", AllyState.Separated => "đứng tách nhau", _ => "chưa rõ"
    };
    private static string NumbersLabel(NumbersState v) => v switch
    {
        NumbersState.Advantage => "hơn người", NumbersState.Even => "bằng người",
        NumbersState.Outnumbered => "thiếu người", _ => "chưa rõ"
    };
    private static string MinionLabel(MinionState v) => v switch
    {
        MinionState.EnemyLarge => "lính địch đông", MinionState.AllyLarge => "lính mình đông",
        MinionState.Even => "tương đối cân bằng", _ => "chưa rõ"
    };
}
