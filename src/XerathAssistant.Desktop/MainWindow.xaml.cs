using System;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Random _random = new(42);
    private readonly Vector2 _caster = new(125, 337);
    private readonly Vector2 _blocker = new(324, 287);
    private Vector2 _target = new(625, 180);
    private Vector2 _velocity = new(-85, 37);
    private PendingCast? _pending;
    private DateTime _lastFrame = DateTime.UtcNow;
    private float _zigzagRemaining = 1.6f;
    private bool _paused;
    private bool _dragging;
    private int _hit, _miss;

    private sealed record PendingCast(Spell Spell, Vector2 Aim, float Range,
        float Radius, Vector2? Blocker, float Remaining);

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += OnTick;
        _timer.Start();
        Draw();
    }

    private Spell SelectedSpell =>
        Enum.TryParse<Spell>((SpellPicker.SelectedItem as ComboBoxItem)?.Content?.ToString(), out var spell)
            ? spell : Spell.Q;

    private float Speed => (float)SpeedSlider.Value;
    private Vector2? CurrentBlocker => BlockerCheck.IsChecked == true ? _blocker : null;
    private AimResult CurrentPrediction => AimEngine.Predict(new AimRequest(
        _caster, new TargetObservation(_target, _velocity), SelectedSpell,
        (float)ChargeSlider.Value, CurrentBlocker));

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        // WPF may raise control events while InitializeComponent is still constructing the tree.
        if (Arena is null || SpellPicker is null || ChargeSlider is null || SpeedSlider is null ||
            ChargeLabel is null || SpeedLabel is null || PredictionLabel is null ||
            ZigzagCheck is null || BlockerCheck is null) return;
        ChargeLabel.Text = $"{ChargeSlider.Value:0.0} s";
        SpeedLabel.Text = $"{Speed:0} px/s";
        if (_velocity.LengthSquared() > .001f) _velocity = Vector2.Normalize(_velocity) * Speed;
        Draw();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Clamp((float)(now - _lastFrame).TotalSeconds, 0f, 0.075f);
        _lastFrame = now;
        if (_paused || _dragging) return;
        _target += _velocity * dt;
        const float left = 235, right = 965, top = 50, bottom = 415;
        if (_target.X < left || _target.X > right)
        {
            _target.X = Math.Clamp(_target.X, left, right);
            _velocity.X = -_velocity.X;
        }
        if (_target.Y < top || _target.Y > bottom)
        {
            _target.Y = Math.Clamp(_target.Y, top, bottom);
            _velocity.Y = -_velocity.Y;
        }
        if (ZigzagCheck.IsChecked == true)
        {
            _zigzagRemaining -= dt;
            if (_zigzagRemaining <= 0f)
            {
                var angle = (_random.NextDouble() * 2 - 1) * 1.25;
                var radians = (float)angle;
                var c = MathF.Cos(radians);
                var s = MathF.Sin(radians);
                var next = new Vector2(_velocity.X * c - _velocity.Y * s,
                                       _velocity.X * s + _velocity.Y * c);
                _velocity = Vector2.Normalize(next) * Speed;
                _zigzagRemaining = 0.65f + (float)_random.NextDouble() * 1.4f;
            }
        }
        if (_pending is { } pending)
        {
            var remaining = pending.Remaining - dt;
            if (remaining <= 0)
            {
                var hit = AimEngine.IsHit(pending.Spell, _caster, pending.Aim, _target,
                    pending.Range, pending.Radius, pending.Blocker);
                if (hit) _hit++; else _miss++;
                FeedbackLabel.Foreground = Brush(hit ? "#84E1B7" : "#FFAD9E");
                FeedbackLabel.Text = hit
                    ? $"TRÚNG {pending.Spell}! Vị trí thực tế: ({_target.X:0}, {_target.Y:0})."
                    : $"TRƯỢT {pending.Spell}. Mục tiêu đổi hướng/vượt tầm, hoặc E bị chắn. Vị trí thực tế: ({_target.X:0}, {_target.Y:0}).";
                ScoreLabel.Text = $"Trúng: {_hit} | Trượt: {_miss}";
                _pending = null;
            }
            else _pending = pending with { Remaining = remaining };
        }
        Draw();
    }

    private void CastClick(object sender, RoutedEventArgs e)
    {
        if (_pending is not null)
        {
            FeedbackLabel.Text = "Đang đợi chiêu trước tác dụng. Hãy thử lại sau.";
            return;
        }
        var result = CurrentPrediction;
        if (!result.InRange || result.Blocked)
        {
            FeedbackLabel.Foreground = Brush("#FFAD9E");
            FeedbackLabel.Text = result.Blocked ? "E đang bị lính mô phỏng chắn. Thử tắt vật cản hoặc đổi vị trí."
                                                : "Mục tiêu dự đoán ngoài tầm mô phỏng. Thử tăng thời gian vận Q hoặc đổi vị trí.";
            return;
        }
        _pending = new PendingCast(SelectedSpell, result.AimPoint, result.Range,
            result.HitRadius, CurrentBlocker, result.TimeToImpact);
        FeedbackLabel.Foreground = Brush("#8FE1C3");
        FeedbackLabel.Text = $"Đã thả {SelectedSpell}. Chờ {result.TimeToImpact:0.00} giây để so sánh với vị trí thực tế…";
    }

    private void PauseClick(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? "Tiếp tục" : "Tạm dừng";
        _lastFrame = DateTime.UtcNow;
    }

    private void ResetClick(object sender, RoutedEventArgs e)
    {
        _target = new Vector2(625, 180);
        _velocity = Vector2.Normalize(new Vector2(-85, 37)) * Speed;
        _zigzagRemaining = 1.6f;
        _pending = null;
        _hit = _miss = 0;
        ScoreLabel.Text = "Trúng: 0 | Trượt: 0";
        FeedbackLabel.Text = "Đã đặt lại bài tập.";
        Draw();
    }

    private void ArenaMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        Arena.CaptureMouse();
        MoveTargetToMouse(e);
    }
    private void ArenaMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        Arena.ReleaseMouseCapture();
        _lastFrame = DateTime.UtcNow;
    }
    private void ArenaMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging) MoveTargetToMouse(e);
    }
    private void MoveTargetToMouse(MouseEventArgs e)
    {
        var p = e.GetPosition(Arena);
        _target = new Vector2((float)Math.Clamp(p.X, 235, 965),
                              (float)Math.Clamp(p.Y, 50, 415));
        Draw();
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex)!);

    private void AddLine(Vector2 a, Vector2 b, string color, double thickness = 1.5,
        DoubleCollection? dashes = null)
    {
        Arena.Children.Add(new Line
        {
            X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = Brush(color),
            StrokeThickness = thickness, StrokeDashArray = dashes
        });
    }
    private void AddCircle(Vector2 p, double radius, string fill, string stroke, double strokeWidth = 1)
    {
        var circle = new Ellipse
        {
            Width = 2 * radius, Height = 2 * radius, Fill = Brush(fill),
            Stroke = Brush(stroke), StrokeThickness = strokeWidth
        };
        Canvas.SetLeft(circle, p.X - radius);
        Canvas.SetTop(circle, p.Y - radius);
        Arena.Children.Add(circle);
    }
    private void AddText(string text, Vector2 p, string color, int size = 12)
    {
        var label = new TextBlock { Text = text, Foreground = Brush(color), FontSize = size };
        Canvas.SetLeft(label, p.X);
        Canvas.SetTop(label, p.Y);
        Arena.Children.Add(label);
    }

    private void Draw()
    {
        if (Arena is null || PredictionLabel is null || SpellPicker is null ||
            ChargeSlider is null || SpeedSlider is null || BlockerCheck is null) return;
        Arena.Children.Clear();
        for (var x = 30; x <= 1000; x += 50)
            AddLine(new Vector2(x, 0), new Vector2(x, 465), "#1B3044", 0.7);
        for (var y = 30; y <= 455; y += 50)
            AddLine(new Vector2(0, y), new Vector2(1010, y), "#1B3044", 0.7);
        var selected = CurrentPrediction;
        var aim = selected.AimPoint;
        var spell = SelectedSpell;
        var aimVector = aim - _caster;
        var unit = aimVector.LengthSquared() > .0001f ? Vector2.Normalize(aimVector) : Vector2.UnitX;
        if (spell is Spell.Q or Spell.E)
        {
            var endpoint = _caster + unit * selected.Range;
            AddLine(_caster, endpoint, "#49B8E7", spell == Spell.Q ? 5 : 3,
                new DoubleCollection { 7, 4 });
        }
        else AddLine(_caster, aim, "#3B87B4", 1.5, new DoubleCollection { 3, 3 });
        AddCircle(aim, selected.HitRadius, "#284963", "#5BCEFA", 1.5);
        if (spell == Spell.W) AddCircle(aim, selected.HitRadius * .53, "#294C58", "#92E5FF", 1.5);
        AddCircle(aim, 4, "#72DAFA", "#E6FBFF");
        if (CurrentBlocker is { } blocker)
        {
            AddCircle(blocker, 15, "#B18E52", "#F5D58A");
            AddText("Lính chắn E", blocker + new Vector2(-30, 19), "#F5D58A");
        }
        AddLine(_target, _target + _velocity * .62f, "#E8A29B", 2,
            new DoubleCollection { 5, 3 });
        AddCircle(_target, 15, "#B94A59", "#FFC4BF", 2);
        AddText("Mục tiêu", _target + new Vector2(-23, -34), "#FFC4BF");
        AddCircle(_caster, 17, "#236797", "#B9E5FF", 2);
        AddText("Xerath", _caster + new Vector2(-21, 23), "#C8E8FF");
        var status = selected.Blocked ? "E BỊ CHẮN" : selected.InRange ? "TRONG TẦM" : "NGOÀI TẦM";
        PredictionLabel.Text = $"{spell}: Điểm ngắm ({aim.X:0}, {aim.Y:0})  •  " +
            $"Tác dụng sau {selected.TimeToImpact:0.00} s  •  {status}\n{selected.Explanation}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
