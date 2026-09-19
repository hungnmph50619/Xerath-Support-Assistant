using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace XerathAssistant.Desktop;

/// <summary>
/// Free Vietnamese speech for fixed reminders. First attempts a keyless Vietnamese
/// web TTS service, then uses a locally installed eSpeak NG Vietnamese voice.
/// eSpeak NG is fully offline, has no usage quota, and sounds more robotic.
/// Both paths write a cached audio file; English is never used as a fallback.
/// </summary>
public sealed class FreeVietnameseVoiceService : IDisposable
{
    private static readonly Uri GoogleTtsHost = new("https://translate.google.com/");
    private readonly HttpClient _http = new()
    {
        BaseAddress = GoogleTtsHost,
        Timeout = TimeSpan.FromSeconds(15)
    };
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "voice", "free-vietnamese-v2");

    private string FileStem(string phrase) => Path.Combine(_folder,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phrase))));

    public string CachePath(string phrase)
    {
        var stem = FileStem(phrase);
        var wav = stem + ".wav";
        return File.Exists(wav) && new FileInfo(wav).Length > 1024 ? wav : stem + ".mp3";
    }

    public bool IsReady(string phrase)
    {
        var path = CachePath(phrase);
        return File.Exists(path) && new FileInfo(path).Length > 1024;
    }

    public async Task PrepareAsync(string phrase, CancellationToken cancellationToken)
    {
        if (IsReady(phrase)) return;
        if (string.IsNullOrWhiteSpace(phrase) || phrase.Length > 200)
            throw new ArgumentException("Câu nhắc không hợp lệ hoặc dài quá 200 ký tự.", nameof(phrase));

        Directory.CreateDirectory(_folder);
        cancellationToken.ThrowIfCancellationRequested();

        // Primary: short fixed Vietnamese sentences through Google Translate TTS.
        // This is an unofficial public consumer endpoint; it may change or block requests.
        try
        {
            var query = "translate_tts?ie=UTF-8&client=tw-ob&tl=vi&q=" + Uri.EscapeDataString(phrase);
            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!LooksLikeMp3(data))
                throw new InvalidDataException("Máy chủ không trả về âm thanh MP3 tiếng Việt.");

            var destination = FileStem(phrase) + ".mp3";
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temp, data, cancellationToken);
                File.Move(temp, destination, true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
               or InvalidDataException or IOException)
        {
            // Web voice failed; continue to a truly offline Vietnamese synthesizer.
        }

        var executable = FindEspeakNg();
        if (executable is null)
            throw new InvalidOperationException(
                "Giọng trực tuyến tạm không hoạt động và máy chưa có eSpeak NG. " +
                "Cài eSpeak NG miễn phí từ https://github.com/espeak-ng/espeak-ng/releases " +
                "(bản Windows x64), mở lại trợ lý rồi nhấn tạo giọng. " +
                "eSpeak NG tạo giọng tiếng Việt offline, không cần API key.");

        var output = FileStem(phrase) + ".wav";
        var input = FileStem(phrase) + "." + Guid.NewGuid().ToString("N") + ".txt";
        var tempWav = output + "." + Guid.NewGuid().ToString("N") + ".wav";
        try
        {
            await File.WriteAllTextAsync(input, phrase, new UTF8Encoding(false), cancellationToken);
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("-v");
            info.ArgumentList.Add("vi");
            info.ArgumentList.Add("-w");
            info.ArgumentList.Add(tempWav);
            info.ArgumentList.Add("-f");
            info.ArgumentList.Add(input);

            using var process = Process.Start(info)
                ?? throw new InvalidOperationException("Không khởi chạy được eSpeak NG.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(true);
                throw new TimeoutException("eSpeak NG xử lý quá lâu. Hãy thử lại.");
            }
            if (process.ExitCode != 0 || !File.Exists(tempWav) ||
                new FileInfo(tempWav).Length <= 1024)
                throw new InvalidOperationException(
                    "eSpeak NG chưa tạo được giọng tiếng Việt. Kiểm tra cài đặt eSpeak NG.");
            // Check the RIFF/WAVE header before enabling playback.
            var header = new byte[12];
            await using (var stream = File.OpenRead(tempWav))
                _ = await stream.ReadAsync(header, cancellationToken);
            if (Encoding.ASCII.GetString(header, 0, 4) != "RIFF" ||
                Encoding.ASCII.GetString(header, 8, 4) != "WAVE")
                throw new InvalidDataException("eSpeak NG trả về tệp âm thanh WAV không hợp lệ.");
            File.Move(tempWav, output, true);
        }
        finally
        {
            if (File.Exists(input)) File.Delete(input);
            if (File.Exists(tempWav)) File.Delete(tempWav);
        }
    }

    private static string? FindEspeakNg()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "eSpeak NG", "espeak-ng.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "eSpeak NG", "espeak-ng.exe")
        };
        return roots.FirstOrDefault(File.Exists);
    }

    private static bool LooksLikeMp3(byte[] data) =>
        data.Length > 1024 && data.Length < 2_000_000 &&
        ((data[0] == (byte)'I' && data[1] == (byte)'D' && data[2] == (byte)'3') ||
         (data[0] == 0xff && (data[1] & 0xe0) == 0xe0));

    public void Dispose() => _http.Dispose();
}
