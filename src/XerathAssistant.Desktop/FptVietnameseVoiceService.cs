using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XerathAssistant.Desktop;

/// <summary>FPT.AI TTS v5; offline playback uses cached MP3 only. Never stores API keys.</summary>
public sealed class FptVietnameseVoiceService : IDisposable
{
    private const string Endpoint = "https://api.fpt.ai/hmi/tts/v5";
    private const string Voice = "banmai"; // Female Northern Vietnamese.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XerathSupportAssistant", "voice", "fpt-banmai-v1");

    public string CachePath(string phrase)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phrase)));
        return Path.Combine(_folder, hash + ".mp3");
    }

    public bool IsReady(string phrase) =>
        File.Exists(CachePath(phrase)) && new FileInfo(CachePath(phrase)).Length > 1024;

    public async Task PrepareAsync(string phrase, string apiKey, CancellationToken cancellationToken)
    {
        if (IsReady(phrase)) return;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Cần API key FPT.AI để tạo âm thanh lần đầu.");
        if (phrase.Length is < 3 or > 5000) throw new ArgumentOutOfRangeException(nameof(phrase));
        Directory.CreateDirectory(_folder);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.TryAddWithoutValidation("api_key", apiKey.Trim());
        request.Headers.TryAddWithoutValidation("voice", Voice);
        request.Headers.TryAddWithoutValidation("speed", "0");
        request.Headers.TryAddWithoutValidation("format", "mp3");
        request.Content = new StringContent(phrase, Encoding.UTF8, "text/plain");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        if (!root.TryGetProperty("error", out var error) || error.GetInt32() != 0 ||
            !root.TryGetProperty("async", out var urlElement))
            throw new InvalidOperationException("FPT.AI chưa tạo được lời nhắc. Kiểm tra API key và hạn mức.");

        var rawUrl = urlElement.GetString();
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("FPT.AI không trả về đường dẫn âm thanh HTTPS hợp lệ.");

        // Provider gives an async URL before the audio is ready.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            try
            {
                using var audioResponse = await _http.GetAsync(url, cancellationToken);
                if (!audioResponse.IsSuccessStatusCode) continue;
                var type = audioResponse.Content.Headers.ContentType?.MediaType ?? "";
                var bytes = await audioResponse.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length < 1024 || bytes.Length > 2_000_000 ||
                    (type is not ("audio/mpeg" or "audio/mp3" or "application/octet-stream") &&
                     !(bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3')))
                    continue;
                var tmp = CachePath(phrase) + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(tmp, bytes, cancellationToken);
                    File.Move(tmp, CachePath(phrase), true);
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
                return;
            }
            catch (HttpRequestException) when (attempt < 29) { }
        }
        throw new TimeoutException("FPT.AI chưa trả về âm thanh sau thời gian chờ. Hãy thử tạo lại.");
    }

    public void Dispose() => _http.Dispose();
}
