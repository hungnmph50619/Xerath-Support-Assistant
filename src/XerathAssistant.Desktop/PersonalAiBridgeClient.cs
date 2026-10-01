using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

/// <summary>
/// Optional stage-one bridge to the user's own PersonalAI web server.
/// Fixed loopback URL, no screenshots, credentials, chat history, hidden data or
/// language-model calls. Each outgoing request is one factual, typed event.
/// </summary>
public sealed record PersonalAiCoachAdvice(
    string Id,
    string Text,
    int Priority,
    double Confidence,
    double GameTimeSeconds,
    double ValidUntilGameTimeSeconds,
    bool UsedLanguageModel);

public sealed class PersonalAiBridgeClient : IDisposable
{
    private readonly string _clientInstanceId = Guid.NewGuid().ToString("N");
    private long _snapshotSequence;

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

    /// <summary>
    /// Sends one factual Live Client snapshot to PersonalAI Bridge v2.
    /// This never sends screenshots, credentials, chat text or hidden enemy state.
    /// Failure is non-fatal: the local Xerath HUD continues independently.
    /// </summary>
    public async Task<bool> SendSnapshotAsync(
        SelfStatsSnapshot snapshot,
        bool isDead,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(snapshot.GameTimeSeconds) ||
            snapshot.GameTimeSeconds < 0 ||
            snapshot.GameTimeSeconds > 86400)
            return false;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "api/integrations/league/snapshot");
            request.Headers.TryAddWithoutValidation("X-Xerath-Bridge", "2");
            request.Content = JsonContent.Create(new
            {
                bridgeVersion = 2,
                clientInstanceId = _clientInstanceId,
                sequence = Interlocked.Increment(ref _snapshotSequence),
                observedAtUtc = DateTimeOffset.UtcNow,
                source = "riot-live-client-data",
                gameTimeSeconds = snapshot.GameTimeSeconds,
                level = snapshot.Level,
                health = snapshot.Health,
                maxHealth = snapshot.MaxHealth,
                resource = snapshot.Resource,
                maxResource = snapshot.MaxResource,
                resourceType = snapshot.ResourceType,
                gold = snapshot.Gold,
                abilityPower = snapshot.AbilityPower,
                isDead
            });

            using var response = await _client.SendAsync(
                request,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            return doc.RootElement.TryGetProperty("accepted", out var accepted) &&
                   accepted.ValueKind == JsonValueKind.True &&
                   doc.RootElement.TryGetProperty("bridgeVersion", out var version) &&
                   version.TryGetInt32(out var bridgeVersion) &&
                   bridgeVersion == 2;
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

    public async Task<PersonalAiCoachAdvice?> GetAiCoachAdviceAsync(
        PersonalAiCoachAdvice baseAdvice,
        CancellationToken cancellationToken)
    {
        if (baseAdvice.Priority < 60)
            return null;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "api/integrations/league/coach/ai");
            request.Headers.TryAddWithoutValidation("X-Xerath-Bridge", "2");
            request.Content = JsonContent.Create(new
            {
                confirmExternalAi = true,
                expectedAdviceId = baseAdvice.Id,
                expectedAdviceGameTimeSeconds = baseAdvice.GameTimeSeconds
            });

            using var response = await _client.SendAsync(
                request,
                cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (!root.TryGetProperty("adviceId", out var id) ||
                id.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("priority", out var priority) ||
                !priority.TryGetInt32(out var priorityValue) ||
                !root.TryGetProperty("confidence", out var confidence) ||
                !confidence.TryGetDouble(out var confidenceValue) ||
                !root.TryGetProperty("gameTimeSeconds", out var gameTime) ||
                !gameTime.TryGetDouble(out var gameTimeValue) ||
                !root.TryGetProperty("validUntilGameTimeSeconds", out var validUntil) ||
                !validUntil.TryGetDouble(out var validUntilValue) ||
                !root.TryGetProperty("usedLanguageModel", out var usedLanguageModel) ||
                usedLanguageModel.ValueKind != JsonValueKind.True)
                return null;

            var adviceId = id.GetString();
            var adviceText = text.GetString();
            if (!string.Equals(adviceId, baseAdvice.Id, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(adviceText) ||
                adviceText.Length > 180 ||
                priorityValue > baseAdvice.Priority ||
                priorityValue is < 0 or > 100 ||
                !double.IsFinite(confidenceValue) ||
                confidenceValue is < 0 or > 1 ||
                !double.IsFinite(gameTimeValue) ||
                Math.Abs(gameTimeValue - baseAdvice.GameTimeSeconds) > 0.001 ||
                !double.IsFinite(validUntilValue) ||
                validUntilValue > baseAdvice.ValidUntilGameTimeSeconds + 0.001)
                return null;

            return new(
                adviceId!,
                adviceText,
                priorityValue,
                confidenceValue,
                gameTimeValue,
                validUntilValue,
                true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
               or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<PersonalAiCoachAdvice?> GetCoachAdviceAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _client.GetAsync(
                "api/integrations/league/coach/current",
                cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("text", out var text) ||
                text.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("priority", out var priority) ||
                !priority.TryGetInt32(out var priorityValue) ||
                !root.TryGetProperty("confidence", out var confidence) ||
                !confidence.TryGetDouble(out var confidenceValue) ||
                !root.TryGetProperty("gameTimeSeconds", out var gameTime) ||
                !gameTime.TryGetDouble(out var gameTimeValue) ||
                !root.TryGetProperty("validUntilGameTimeSeconds", out var validUntil) ||
                !validUntil.TryGetDouble(out var validUntilValue) ||
                !root.TryGetProperty("usedLanguageModel", out var usedLanguageModel) ||
                usedLanguageModel.ValueKind is not (
                    JsonValueKind.True or JsonValueKind.False))
                return null;

            var adviceId = id.GetString();
            var adviceText = text.GetString();
            if (string.IsNullOrWhiteSpace(adviceId) ||
                string.IsNullOrWhiteSpace(adviceText) ||
                adviceText.Length > 300 ||
                priorityValue is < 0 or > 100 ||
                !double.IsFinite(confidenceValue) ||
                confidenceValue is < 0 or > 1 ||
                !double.IsFinite(gameTimeValue) ||
                !double.IsFinite(validUntilValue) ||
                validUntilValue < gameTimeValue)
                return null;

            return new(
                adviceId,
                adviceText,
                priorityValue,
                confidenceValue,
                gameTimeValue,
                validUntilValue,
                usedLanguageModel.ValueKind == JsonValueKind.True);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
               or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose() => _client.Dispose();
}
