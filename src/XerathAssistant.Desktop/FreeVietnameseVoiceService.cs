using System.IO;
using System.Security.Cryptography;
using System.Text;
using Edge_tts_sharp;
using Edge_tts_sharp.Model;

namespace XerathAssistant.Desktop;

/// <summary>
/// Keyless online synthesis for a finite, fixed set of Vietnamese reminder sentences.
/// The Edge consumer speech service is not a guaranteed public API; errors are reported,
/// never replaced by an English voice. Cached MP3 playback requires no network.
/// </summary>
public sealed class FreeVietnameseVoiceService
{
    public const string VoiceShortName = "vi-VN-HoaiMyNeural"; // Vietnamese female; regional accent not guaranteed.
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "voice", "edge-hoaimy-v1");

    public string CachePath(string phrase)
    {
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(VoiceShortName + "\n" + phrase)));
        return Path.Combine(_folder, fingerprint + ".mp3");
    }

    public bool IsReady(string phrase)
    {
        var file = CachePath(phrase);
        return File.Exists(file) && new FileInfo(file).Length > 1024;
    }

    public async Task PrepareAsync(string phrase, CancellationToken cancellationToken)
    {
        if (IsReady(phrase)) return;
        if (string.IsNullOrWhiteSpace(phrase) || phrase.Length > 500)
            throw new ArgumentException("Câu nhắc không hợp lệ.", nameof(phrase));

        Directory.CreateDirectory(_folder);
        var voices = Edge_tts.GetVoice();
        var voice = voices.FirstOrDefault(v =>
            string.Equals(v.ShortName, VoiceShortName, StringComparison.OrdinalIgnoreCase));
        if (voice is null)
            throw new InvalidOperationException(
                "Không tìm thấy giọng nữ tiếng Việt Hoài My. Kiểm tra gói tạo giọng hoặc danh sách giọng.");

        var destination = CachePath(phrase);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".mp3";

        // Run the third-party synchronous WebSocket client off the WPF UI thread.
        // Its endpoint is unofficial: cap the UI wait and report a service outage.
        try
        {
            await Task.Run(() =>
            {
                Edge_tts.Await = true;
                Edge_tts.SaveAudio(new PlayOption
                {
                    Text = phrase,
                    Rate = 0,
                    Volume = 1.0f, // WPF MediaPlayer controls playback volume at 70%.
                    SavePath = temp
                }, voice);
            }, cancellationToken).WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);

            // Some library versions close their WebSocket just after the wait completes.
            for (var retry = 0; retry < 20 && (!File.Exists(temp) || new FileInfo(temp).Length < 1024); retry++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(200, cancellationToken);
            }

            if (!File.Exists(temp) || new FileInfo(temp).Length < 1024)
                throw new InvalidOperationException(
                    "Dịch vụ chưa trả về âm thanh. Hãy kiểm tra Internet rồi thử lại.");

            var buffer = new byte[3];
            await using (var stream = File.OpenRead(temp))
                _ = await stream.ReadAsync(buffer, cancellationToken);
            var mp3 = buffer[0] == (byte)'I' && buffer[1] == (byte)'D' && buffer[2] == (byte)'3'
                || buffer[0] == 0xFF && (buffer[1] & 0xE0) == 0xE0;
            if (!mp3 || new FileInfo(temp).Length > 2_000_000)
                throw new InvalidOperationException(
                    "Âm thanh trả về không phải MP3 hợp lệ. Hãy thử lại khi dịch vụ ổn định.");

            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            // On timeout the third-party library might still hold the temporary file.
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
