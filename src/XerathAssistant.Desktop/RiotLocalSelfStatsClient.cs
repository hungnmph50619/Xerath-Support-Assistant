using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using XerathAssistant.Core;

namespace XerathAssistant.Desktop;

/// <summary>Only local, documented Riot Live Client Data API endpoints; no game-memory access.</summary>
public sealed class RiotLocalSelfStatsClient : IDisposable
{
    private static readonly Uri BaseUri = new("https://127.0.0.1:2999/");
    private readonly HttpClient _client;

    public RiotLocalSelfStatsClient()
    {
        // Game client uses a local self-signed TLS certificate. The exception is limited
        // to this fixed loopback host/port; never disable validation for other services.
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ServerCertificateCustomValidationCallback =
                (request, certificate, _, errors) =>
                    request?.RequestUri?.Scheme == Uri.UriSchemeHttps &&
                    request.RequestUri.Host == "127.0.0.1" &&
                    request.RequestUri.Port == 2999 &&
                    certificate is not null
        };
        _client = new HttpClient(handler) { BaseAddress = BaseUri, Timeout = TimeSpan.FromSeconds(3) };
    }

    public async Task<SelfStatsSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        using var activeResponse = await _client.GetAsync("liveclientdata/activeplayer", cancellationToken);
        activeResponse.EnsureSuccessStatusCode();
        var activeJson = await activeResponse.Content.ReadAsStringAsync(cancellationToken);
        using var statsResponse = await _client.GetAsync("liveclientdata/gamestats", cancellationToken);
        statsResponse.EnsureSuccessStatusCode();
        var gameJson = await statsResponse.Content.ReadAsStringAsync(cancellationToken);
        return SelfStatsParser.Parse(activeJson, gameJson);
    }

    public async Task<string> ReadEventsJsonAsync(CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync("liveclientdata/eventdata", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}
