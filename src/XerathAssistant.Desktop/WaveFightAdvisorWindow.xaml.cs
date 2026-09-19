using Condition = XerathAssistant.Core.Condition;
using System.Windows;
using System.Windows.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

/// <summary>
/// Offline review only: manually entered observations, no League integration or live advice.
/// </summary>
public partial class WaveFightAdvisorWindow : Window
{
    private readonly ComboBox[] _inputs;

    public WaveFightAdvisorWindow()
    {
        InitializeComponent();
        _inputs = new[]
        {
            WaveLocationBox, WaveDirectionBox, AdcPlanBox, HealthBox, ManaBox,
            VisionBox, JungleBox, AllyBox, NumbersBox, EngageBox, MinionsBox, ObjectiveBox
        };

        Choices(WaveLocationBox,
            (WaveLocation.Unknown, "Chưa rõ"), (WaveLocation.NearAllyTower, "Gần trụ nhà"),
            (WaveLocation.AllyHalf, "Nửa sân nhà"), (WaveLocation.Middle, "Giữa đường"),
            (WaveLocation.EnemyHalf, "Nửa sân đối phương"),
            (WaveLocation.NearEnemyTower, "Gần trụ đối phương"));
        Choices(WaveDirectionBox,
            (WaveDirection.Unknown, "Chưa rõ"), (WaveDirection.TowardAlly, "Đang về phía trụ nhà"),
            (WaveDirection.Stable, "Tương đối cân bằng"),
            (WaveDirection.TowardEnemy, "Đang về phía trụ đối phương"));
        Choices(AdcPlanBox,
            (AdcPlan.Unknown, "Chưa rõ"), (AdcPlan.Hold, "ADC muốn giữ lính"),
            (AdcPlan.PushSlow, "ADC muốn đẩy chậm"),
            (AdcPlan.CrashThenRecall, "Đẩy vào trụ rồi về"),
            (AdcPlan.NoPlan, "Chưa thống nhất kế hoạch"));
        Choices(HealthBox,
            (Condition.Unknown, "Chưa rõ"), (Condition.Low, "Một trong hai đang thấp máu"),
            (Condition.Healthy, "Cả hai còn đủ máu"));
        Choices(ManaBox,
            (Condition.Unknown, "Chưa rõ"), (Condition.Low, "Thiếu năng lượng"),
            (Condition.Healthy, "Đủ năng lượng cho trao đổi chiêu"));
        Choices(VisionBox,
            (VisionState.Unknown, "Chưa rõ"), (VisionState.Dark, "Thiếu tầm nhìn sông"),
            (VisionState.Controlled, "Có kiểm soát tầm nhìn"));
        Choices(JungleBox,
            (JungleState.Unknown, "Chưa rõ"),
            (JungleState.RecentlySeenBot, "Mới thấy rừng địch gần Bot"),
            (JungleState.NoRecentSighting, "Không có vị trí gần đây"),
            (JungleState.RecentlySeenTop, "Mới thấy rừng địch ở Top"));
        Choices(AllyBox,
            (AllyState.Unknown, "Chưa rõ"), (AllyState.Together, "Đang ở gần ADC"),
            (AllyState.Separated, "Đứng tách xa ADC"));
        Choices(NumbersBox,
            (NumbersState.Unknown, "Chưa rõ"), (NumbersState.Outnumbered, "Mình thiếu người"),
            (NumbersState.Even, "Số người tương đương"), (NumbersState.Advantage, "Mình hơn người"));
        Choices(EngageBox,
            (EnemyEngageState.Unknown, "Chưa rõ"),
            (EnemyEngageState.Available, "Đối thủ còn kỹ năng mở giao tranh"),
            (EnemyEngageState.RecentlyUsed, "Đối thủ vừa dùng kỹ năng mở giao tranh"));
        Choices(MinionsBox,
            (MinionState.Unknown, "Chưa rõ"), (MinionState.EnemyLarge, "Lính địch đông hơn nhiều"),
            (MinionState.Even, "Tương đối cân bằng"), (MinionState.AllyLarge, "Lính mình đông hơn"));
        Choices(ObjectiveBox,
            (ObjectiveState.Unknown, "Chưa rõ"), (ObjectiveState.Soon, "Mục tiêu sắp xuất hiện"),
            (ObjectiveState.NotSoon, "Chưa có mục tiêu gần"));
    }

    private static void Choices<T>(ComboBox combo, params (T Value, string Label)[] items) where T : struct, Enum
    {
        foreach (var (value, label) in items)
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        combo.SelectedIndex = 0;
    }

    private static T Chosen<T>(ComboBox combo) where T : struct, Enum =>
        combo.SelectedItem is ComboBoxItem { Tag: T value } ? value : default;

    private void AnalyzeClick(object sender, RoutedEventArgs e)
    {
        var scenario = new WaveFightScenario(
            Chosen<WaveLocation>(WaveLocationBox),
            Chosen<WaveDirection>(WaveDirectionBox),
            Chosen<AdcPlan>(AdcPlanBox),
            Chosen<Condition>(HealthBox),
            Chosen<Condition>(ManaBox),
            Chosen<VisionState>(VisionBox),
            Chosen<JungleState>(JungleBox),
            Chosen<AllyState>(AllyBox),
            Chosen<NumbersState>(NumbersBox),
            Chosen<EnemyEngageState>(EngageBox),
            Chosen<MinionState>(MinionsBox),
            Chosen<ObjectiveState>(ObjectiveBox));

        var result = WaveFightAdvisor.Evaluate(scenario);
        WaveAdviceText.Text = Format(result.Wave);
        PositionAdviceText.Text = Format(result.Position);
        FightAdviceText.Text = Format(result.Fight);
        var missing = new[] { result.Wave, result.Position, result.Fight }
            .Count(x => x.State == AdviceState.InsufficientInformation);
        ResultStatus.Text = missing > 0
            ? $"Đã phân tích dữ kiện bạn nhập. {missing}/3 mục chưa đủ thông tin; hãy xem danh sách còn thiếu."
            : "Đã phân tích dữ kiện tự nhập. Đây là nhận định có điều kiện để xem lại tình huống, không phải chỉ dẫn trận đấu trực tiếp.";
    }

    private static string Format(ScenarioAdvice advice)
    {
        var lines = new List<string> { advice.Heading, "", advice.Explanation };
        if (advice.Evidence.Count > 0)
        {
            lines.Add("");
            lines.Add("Căn cứ bạn cung cấp:");
            lines.AddRange(advice.Evidence.Select(x => "• " + x));
        }
        if (advice.Missing.Count > 0)
        {
            lines.Add("");
            lines.Add("Thông tin cần bổ sung:");
            lines.AddRange(advice.Missing.Select(x => "• " + x));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private void ResetClick(object sender, RoutedEventArgs e)
    {
        foreach (var box in _inputs) box.SelectedIndex = 0;
        ResultStatus.Text = "Đã xóa thông tin. Chọn tình huống mới rồi bấm Phân tích tình huống.";
        WaveAdviceText.Text = PositionAdviceText.Text = FightAdviceText.Text = "Chưa có nhận định.";
    }

    private void ExampleClick(object sender, RoutedEventArgs e)
    {
        WaveLocationBox.SelectedIndex = 4; // enemy half
        WaveDirectionBox.SelectedIndex = 3; // toward enemy
        AdcPlanBox.SelectedIndex = 3;       // crash then recall
        HealthBox.SelectedIndex = 2;
        ManaBox.SelectedIndex = 2;
        VisionBox.SelectedIndex = 1;        // dark: should NOT suggest an aggressive push
        JungleBox.SelectedIndex = 3;        // recently top: not proof Bot is safe
        AllyBox.SelectedIndex = 1;
        NumbersBox.SelectedIndex = 2;
        EngageBox.SelectedIndex = 1;
        MinionsBox.SelectedIndex = 2;
        ObjectiveBox.SelectedIndex = 2;
        ResultStatus.Text = "Đã điền ví dụ giả định. Bấm Phân tích tình huống để xem ba nhận định.";
        WaveAdviceText.Text = PositionAdviceText.Text = FightAdviceText.Text = "Chờ phân tích ví dụ.";
    }
}
