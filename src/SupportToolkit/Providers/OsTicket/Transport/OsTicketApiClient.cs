using System.Diagnostics;
using System.Net.Http.Json;
using SupportToolkit.Core.Logging;

namespace SupportToolkit.Providers.OsTicket.Transport;

/// <summary>
/// The narrow HTTP transport for an osTicket helpdesk. Its only write,
/// and its only request, is ticket creation. osTicket's API cannot read
/// tickets, so nothing else is offered.
/// </summary>
public sealed class OsTicketApiClient
{
    private const string CreateTicketPath = "api/tickets.json";

    private readonly HttpClient _httpClient;
    private readonly OsTicketOptions _options;
    private readonly OperationalLogger? _logger;

    public OsTicketApiClient(
        HttpClient httpClient,
        OsTicketOptions options,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _options =
            options ?? throw new ArgumentNullException(nameof(options));

        _logger = logger;
    }

    /// <summary>
    /// The staff view of a ticket, built from the configured helpdesk
    /// rather than from anything osTicket returned.
    /// </summary>
    public Uri GetTicketViewUri(string ticketNumber)
    {
        if (string.IsNullOrWhiteSpace(ticketNumber))
        {
            throw new ArgumentException(
                "An osTicket ticket number is required.",
                nameof(ticketNumber));
        }

        return new Uri(
            _options.BaseUri,
            "scp/tickets.php?number=" + Uri.EscapeDataString(ticketNumber));
    }

    /// <summary>
    /// Sends one ticket creation request. Never retried: osTicket has no
    /// idempotency, so a repeat would create another ticket.
    /// </summary>
    public async Task<HttpResponseMessage> PostTicketAsync<TPayload>(
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_options.BaseUri, CreateTicketPath));

        request.Headers.Add("X-API-Key", _options.ApiKey);

        request.Content = JsonContent.Create(payload);

        _logger?.Info($"POST /{CreateTicketPath}");

        var stopwatch = Stopwatch.StartNew();

        var response =
            await _httpClient.SendAsync(request, cancellationToken);

        stopwatch.Stop();

        _logger?.Info(
            $"POST /{CreateTicketPath} -> " +
            $"{(int)response.StatusCode} " +
            $"({stopwatch.ElapsedMilliseconds} ms)");

        return response;
    }
}
