using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

/// <summary>
/// Manual annotations on an existing screenshot only. No screen capture, live tracking,
/// in-game overlay, memory access, keyboard injection or inferred hidden location.
/// </summary>
public partial class LastSeenReviewWindow : Window
{
    private readonly LastSeenReview _review = new();
    private bool _imageLoaded;
    private readonly FreeVietnameseVoiceService _reviewVoice = new();
    private readonly MediaPlayer _reviewPlayer = new() { Volume = 0.7 };
    private CancellationTokenSource? _voicePreparation;

    public LastSeenReviewWindow()
    {
        InitializeComponent();
        _reviewPlayer.MediaEnded += (_, _) => _reviewPlayer.Close();
        _reviewPlayer.MediaFailed += (_, _) =>
        {
            ReplayVoiceStatus.Text = "Không phát được tệp tiếng Việt. Hãy kiểm tra âm lượng Windows.";
            _reviewPlayer.Close();
        };
    }

    /// <summary>
    /// Narrate ONLY a manually annotated screenshot during replay. No live detection,
    /// no automated opponent tracking and no narration of an unseen position.
    /// </summary>
    private async void ReplayVoiceClick(object sender, RoutedEventArgs e)
    {
        if (!_imageLoaded || _review.Current is null)
        {
            ReplayVoiceStatus.Text = "Hãy mở ảnh đã lưu rồi bấm đánh dấu đúng biểu tượng tướng trong ảnh trước khi nghe.";
            return;
        }

        // Even a marked coordinate is not evidence of identity/role, so the user
        // must manually select the role and the observed region.
        var role = ChampionRoleBox.SelectedIndex switch
        {
            0 => "Rừng địch",
            1 => "Mid đối phương",
            _ => null
        };
        var area = LastSeenAreaBox.SelectedIndex switch
        {
            0 => "đường trên",
            1 => "đường giữa",
            2 => "đường dưới",
            3 => "khu vực sông",
            4 => "khu vực rừng",
            _ => null
        };
        if (role is null || area is null)
        {
            ReplayVoiceStatus.Text = "Chưa xác định được vai trò hoặc vị trí trong ảnh. Không phát lời suy đoán.";
            return;
        }

        var phrase = $"Trong ảnh đã lưu, bạn đánh dấu {role.ToLowerInvariant()} ở {area}.";
        if (_voicePreparation is not null)
        {
            ReplayVoiceStatus.Text = "Đang chuẩn bị giọng cho ảnh trước, hãy đợi hoàn tất.";
            return;
        }

        using var preparation = new CancellationTokenSource();
        _voicePreparation = preparation;
        ReplayVoiceButton.IsEnabled = false;
        try
        {
            ReplayVoiceStatus.Text = "Đang tạo câu tiếng Việt miễn phí cho ảnh xem lại (chỉ tạo nếu chưa lưu).";
            await _reviewVoice.PrepareAsync(phrase, preparation.Token);
            if (preparation.IsCancellationRequested) return;
            _reviewPlayer.Stop();
            _reviewPlayer.Close();
            _reviewPlayer.Open(new Uri(Path.GetFullPath(_reviewVoice.CachePath(phrase))));
            _reviewPlayer.Volume = 0.7;
            _reviewPlayer.Play();
            ReplayVoiceStatus.Text = "Đang đọc vị trí bạn đánh dấu TRONG ẢNH ĐÃ LƯU; không phải vị trí hiện tại trong game.";
        }
        catch (OperationCanceledException)
        {
            ReplayVoiceStatus.Text = "Đã dừng chuẩn bị giọng cho ảnh.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or
                                   ArgumentException or TimeoutException)
        {
            ReplayVoiceStatus.Text = "Chưa tạo/phát được tiếng Việt: " + ex.Message;
        }
        finally
        {
            _voicePreparation = null;
            ReplayVoiceButton.IsEnabled = true;
        }
    }

    private void StopReplayVoiceClick(object sender, RoutedEventArgs e)
    {
        _voicePreparation?.Cancel();
        _reviewPlayer.Stop();
        _reviewPlayer.Close();
        ReplayVoiceStatus.Text = "Đã dừng phát giọng xem lại.";
    }

    protected override void OnClosed(EventArgs e)
    {
        _voicePreparation?.Cancel();
        _reviewPlayer.Stop();
        _reviewPlayer.Close();
        _reviewVoice.Dispose();
        base.OnClosed(e);
    }

    private void OpenImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Mở ảnh trận đã lưu để xem lại",
            Filter = "Ảnh PNG/JPEG|*.png;*.jpg;*.jpeg|Tất cả tệp|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(dialog.FileName));
            image.EndInit();
            image.Freeze();

            if (image.PixelWidth == 0 || image.PixelHeight == 0)
                throw new InvalidDataException("Ảnh không có kích thước hợp lệ.");

            ScreenshotCanvas.Width = ScreenshotImage.Width = image.PixelWidth;
            ScreenshotCanvas.Height = ScreenshotImage.Height = image.PixelHeight;
            ScreenshotImage.Source = image;
            _imageLoaded = true;
            _review.Clear();
            RenderMark();
            ReviewStatus.Text = "Đã mở ảnh: " + Path.GetFileName(dialog.FileName) +
                ". Nhập thời điểm trong ảnh, sau đó bấm biểu tượng rừng địch trên minimap để đánh dấu.";
        }
        catch (Exception ex)
        {
            ReviewStatus.Text = "Không mở được ảnh: " + ex.Message;
        }
    }

    private void ImageClick(object sender, MouseButtonEventArgs e)
    {
        if (!_imageLoaded)
        {
            ReviewStatus.Text = "Hãy mở ảnh đã lưu trước khi đánh dấu.";
            return;
        }
        if (!LastSeenReview.TryParseGameTime(GameTimeBox.Text, out var matchTime))
        {
            ReviewStatus.Text = "Thời điểm trong ảnh không hợp lệ. Dùng dạng mm:ss, ví dụ 09:20.";
            return;
        }

        var p = e.GetPosition(ScreenshotCanvas);
        if (p.X < 0 || p.Y < 0 || p.X > ScreenshotCanvas.Width || p.Y > ScreenshotCanvas.Height)
            return;

        _review.Mark(p.X / ScreenshotCanvas.Width, p.Y / ScreenshotCanvas.Height,
            matchTime, NoteBox.Text);
        RenderMark();
        e.Handled = true;
    }

    private void UpdateTimeClick(object sender, RoutedEventArgs e)
    {
        if (_review.Current is not { } previous)
        {
            ReviewStatus.Text = "Chưa có dấu nào để cập nhật. Bấm vào minimap trên ảnh để đánh dấu.";
            return;
        }
        if (!LastSeenReview.TryParseGameTime(GameTimeBox.Text, out var time))
        {
            ReviewStatus.Text = "Thời điểm không hợp lệ. Dùng dạng mm:ss, ví dụ 09:20.";
            return;
        }

        _review.Mark(previous.X, previous.Y, time, NoteBox.Text);
        RenderMark();
    }

    private void ClearMarkClick(object sender, RoutedEventArgs e)
    {
        _review.Clear();
        RenderMark();
    }

    private void ReviewTimeChanged(object sender, TextChangedEventArgs e)
    {
        if (MarkHalo is null || ReviewStatus is null) return; // InitializeComponent may not be finished.
        RenderMark();
    }

    private void RenderMark()
    {
        if (MarkHalo is null || MarkTag is null || ReviewStatus is null) return;
        if (_review.Current is not { } mark)
        {
            MarkHalo.Visibility = Visibility.Collapsed;
            MarkTag.Visibility = Visibility.Collapsed;
            ReviewStatus.Text = _imageLoaded
                ? "Chưa có vị trí cuối cùng. Hãy bấm vào minimap trong ảnh để đánh dấu thủ công."
                : "Chưa có ảnh. Hãy mở ảnh trận đã lưu.";
            return;
        }

        var x = mark.X * ScreenshotCanvas.Width;
        var y = mark.Y * ScreenshotCanvas.Height;
        Canvas.SetLeft(MarkHalo, x - MarkHalo.Width / 2);
        Canvas.SetTop(MarkHalo, y - MarkHalo.Height / 2);
        MarkHalo.Visibility = Visibility.Visible;

        MarkTag.Text = "LAST SEEN  " + LastSeenReview.FormatTime(mark.MatchTime);
        var labelX = x + 18 + 200 > ScreenshotCanvas.Width ? x - 220 : x + 18;
        var labelY = y + 32 > ScreenshotCanvas.Height ? y - 30 : y + 16;
        Canvas.SetLeft(MarkTag, Math.Max(0, labelX));
        Canvas.SetTop(MarkTag, Math.Max(0, labelY));
        MarkTag.Visibility = Visibility.Visible;

        var details = "Vị trí được đánh dấu trên ẢNH tại " +
                      LastSeenReview.FormatTime(mark.MatchTime) + ".";
        if (!string.IsNullOrWhiteSpace(mark.Note)) details += " " + mark.Note + ".";
        if (!string.IsNullOrWhiteSpace(ReviewTimeBox.Text))
        {
            if (LastSeenReview.TryParseGameTime(ReviewTimeBox.Text, out var reviewTime))
            {
                var age = _review.AgeAt(reviewTime);
                details += age is { } known
                    ? " Thời gian giữa hai mốc bạn nhập: " + LastSeenReview.FormatTime(known) + "."
                    : " Mốc xem lại phải bằng hoặc muộn hơn thời điểm trong ảnh.";
            }
            else details += " Mốc xem lại không hợp lệ (mm:ss).";
        }
        ReviewStatus.Text = details + " Đây KHÔNG PHẢI vị trí hiện tại của rừng địch.";
    }
}
