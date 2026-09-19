using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace XerathAssistant.Desktop;

/// <summary>
/// Optional stage-one bridge to the user's own PersonalAI web server.
/// Fixed loopback URL, no screenshots, credentials, chat history, hidden data or
/// language-model calls. Each outgoing request is one factual, typed event.
/// </summary>
public sealed class PersonalAiBridgeClient : IDisposable
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:5188/"),
        Timeout = TimeSpan.FromMilliseconds(750)
    };

    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetAsync(
                "api/integrations/xerath/status", cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            return doc.RootElement.TryGetProperty("online", out var online) &&
                   online.ValueKind == JsonValueKind.True &&
                   doc.RootElement.TryGetProperty("bridgeVersion", out var version) &&
                   version.TryGetInt32(out var n) && n == 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
               or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <returns>A short, server-whitelisted factual phrase, or null on failure.</returns>
    public async Task<string?> GetNoticeAsync(
        string kind, double gameTimeSeconds, double? healthPercent,
        CancellationToken cancellationToken)
    {
        if (kind is not ("own-health-loss" or "completed-kill") ||
            !double.IsFinite(gameTimeSeconds) || gameTimeSeconds < 0)
            return null;
        if (healthPercent.HasValue && (!double.IsFinite(healthPercent.Value) ||
                                       healthPercent.Value is < 0 or > 100))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "api/integrations/xerath/notice");
            request.Headers.TryAddWithoutValidation("X-Xerath-Bridge", "1");
            request.Content = JsonContent.Create(new
            {
                kind,
                gameTimeSeconds,
                healthPercent
            });
            using var response = await _client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (!root.TryGetProperty("verified", out var verified) ||
                verified.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("usedLanguageModel", out var usedLanguageModel) ||
                usedLanguageModel.ValueKind != JsonValueKind.False ||
                !root.TryGetProperty("validForMilliseconds", out var validFor) ||
                !validFor.TryGetInt32(out var ttl) || ttl is < 500 or > 10000 ||
                !root.TryGetProperty("source", out var source) ||
                source.ValueKind != JsonValueKind.String ||
                source.GetString() != (kind == "own-health-loss"
                    ? "riot-local-own-stats" : "riot-local-public-event") ||
                !root.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String)
                return null;
            var message = text.GetString();
            return string.IsNullOrWhiteSpace(message) || message.Length > 200
                ? null : message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
               or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose() => _client.Dispose();
}
