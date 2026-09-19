using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MessageBox = System.Windows.MessageBox;

namespace XerathAssistant.Desktop;

/// <summary>Only opens locally saved, manually selected images; never sends data to Gemini.</summary>
public partial class MinimapSampleLibraryWindow : Window
{
    private readonly MinimapSampleStore _store = new();

    public MinimapSampleLibraryWindow() => InitializeComponent();

    private void WindowLoaded(object sender, RoutedEventArgs e) => Reload();

    private void RefreshClick(object sender, RoutedEventArgs e) => Reload();

    private void Reload(string? selectName = null)
    {
        try
        {
            var samples = _store.ListSamples();
            SampleList.ItemsSource = samples;
            var used = samples.Sum(x => x.SizeBytes);
            LibraryStatus.Text = $"Đã lưu {samples.Count}/250 ảnh; dung lượng " +
                $"{used / 1024d / 1024d:0.0}/200 MB. " +
                $"Tập điều chỉnh: {samples.Count(x => x.DatasetSplit == "train")}; " +
                $"tập kiểm thử giữ riêng: {samples.Count(x => x.DatasetSplit == "test")}; " +
                $"mẫu cũ chưa phân tập: {samples.Count(x => x.DatasetSplit == "unassigned")}. " +
                "Chưa đo độ chính xác: chưa có kết quả nhận diện có cấu trúc để so sánh.";
            if (selectName is not null)
                SampleList.SelectedItem = samples.FirstOrDefault(x => x.Name == selectName);
            if (SampleList.SelectedItem is null)
            {
                LibraryPreview.Source = null;
                EditLabelBox.Clear();
                SampleDetail.Text = "Chưa chọn mẫu hoặc thư viện chưa có ảnh.";
                UpdateSampleButton.IsEnabled = DeleteSampleButton.IsEnabled = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LibraryStatus.Text = "Không đọc được thư viện mẫu: " + ex.Message;
        }
    }

    private void SampleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SampleList.SelectedItem is not MinimapSampleStore.SampleItem item) return;
        try
        {
            var bytes = _store.ReadSampleImage(item.Name);
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            LibraryPreview.Source = bitmap;
            EditLabelBox.Text = item.Label;
            var index = item.EvidenceKind switch
            {
                "visible-observation" => 0,
                "hypothesis" => 1,
                _ => 2
            };
            EditEvidenceBox.SelectedIndex = index;
            SampleDetail.Text = $"{item.Name} · {item.MarkCount} điểm tọa độ thủ công · " +
                $"Nhóm dữ liệu: {item.DatasetSplit}. " +
                "Giữ riêng ảnh kiểm thử, không dùng để điều chỉnh mô hình; " +
                "không tự xác nhận tên tướng hay vị trí đối thủ từ mô tả.";
            UpdateSampleButton.IsEnabled = DeleteSampleButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or InvalidOperationException or
                                   NotSupportedException)
        {
            LibraryPreview.Source = null;
            UpdateSampleButton.IsEnabled = DeleteSampleButton.IsEnabled = false;
            LibraryStatus.Text = "Không mở được mẫu: " + ex.Message;
        }
    }

    private void UpdateSampleClick(object sender, RoutedEventArgs e)
    {
        if (SampleList.SelectedItem is not MinimapSampleStore.SampleItem item) return;
        var kind = (EditEvidenceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            ?? "uncertain";
        try
        {
            _store.UpdateSampleNote(item.Name, EditLabelBox.Text, kind);
            Reload(item.Name);
            LibraryStatus.Text = "Đã sửa ghi chú và loại bằng chứng trên ổ đĩa. " +
                "Các điểm tọa độ cũ không tự thay đổi.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or InvalidDataException or
                                   System.Text.Json.JsonException)
        {
            LibraryStatus.Text = "Chưa thể sửa ghi chú: " + ex.Message;
        }
    }

    private void DeleteSampleClick(object sender, RoutedEventArgs e)
    {
        if (SampleList.SelectedItem is not MinimapSampleStore.SampleItem item) return;
        if (MessageBox.Show(this, $"Xóa vĩnh viễn ảnh {item.Name} cùng nhãn và các điểm " +
            "tọa độ của mẫu này? Các mẫu khác sẽ được giữ nguyên.",
            "Xác nhận xóa một mẫu", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            LibraryPreview.Source = null; // Release file-backed resources before deletion.
            _store.DeleteSample(item.Name);
            Reload();
            LibraryStatus.Text = $"Đã xóa mẫu {item.Name}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LibraryStatus.Text = "Chưa xóa được mẫu: " + ex.Message;
        }
    }
}
