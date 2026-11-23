using System.Net;
using System.Text.RegularExpressions;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Providers.OsTicket.Tickets.Dtos;
using SupportToolkit.Providers.OsTicket.Transport;

namespace SupportToolkit.Providers.OsTicket.Tickets;

/// <summary>
/// osTicket implementation of the SupportToolkit ticket provider.
///
/// osTicket has no idempotency: every accepted request is a new ticket.
/// So the idempotency key is not sent, nothing is retried, and only an
/// answer osTicket gives before creating anything (a refused key, a
/// malformed request, a validation error) counts as "not created". Any
/// other failure leaves the outcome unknown, and the message says to
/// check osTicket before trying again.
/// </summary>
public sealed partial class OsTicketTicketProvider : ITicketProvider
{
    private const string ReconciliationAdvice =
        "Ticket creation was not confirmed. Check the osTicket helpdesk " +
        "for an existing ticket before retrying: osTicket has no " +
        "duplicate protection, so a retry creates another ticket.";

    private const string NotCreated =
        "No ticket was created.";

    private const int MaximumErrorLength = 300;

    private readonly OsTicketApiClient _apiClient;
    private readonly int _topicId;
    private readonly OsTicketRequester _requester;

    public OsTicketTicketProvider(
        OsTicketApiClient apiClient,
        int topicId,
        OsTicketRequester requester)
    {
        _apiClient =
            apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        _requester =
            requester ?? throw new ArgumentNullException(nameof(requester));

        if (topicId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(topicId),
                topicId,
                "A positive osTicket help topic ID is required.");
        }

        _topicId = topicId;
    }

    /// <param name="idempotencyKey">
    /// Ignored: osTicket has no idempotency mechanism to pass it to.
    /// </param>
    public async Task<CreatedTicket> CreateTicketAsync(
        TicketDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (string.IsNullOrWhiteSpace(draft.Subject))
        {
            throw new ArgumentException(
                "A ticket requires a subject.",
                nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.Body))
        {
            throw new ArgumentException(
                "A ticket requires a body.",
                nameof(draft));
        }

        var request =
            new OsTicketCreateTicketRequestDto
            {
                Name = _requester.Name,
                Email = _requester.Email,
                Subject = draft.Subject,
                Message = "data:text/plain;charset=utf-8," + draft.Body,
                TopicId = _topicId,
                Alert = false,
                AutoRespond = false
            };

        HttpResponseMessage response;

        try
        {
            response =
                await _apiClient.PostTicketAsync(
                    request,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw Unconfirmed(
                "An osTicket HTTP request failed without a response.",
                exception);
        }
        catch (OperationCanceledException exception)
        {
            throw Unconfirmed(
                "An osTicket request timed out or was interrupted.",
                exception);
        }

        using (response)
        {
            string body;

            try
            {
                body =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is HttpRequestException
                          or OperationCanceledException)
            {
                throw Unconfirmed(
                    $"osTicket returned HTTP {(int)response.StatusCode} " +
                    "but the response could not be read.",
                    exception);
            }

            return Interpret(response.StatusCode, body.Trim(), draft);
        }
    }

    private CreatedTicket Interpret(
        HttpStatusCode status,
        string body,
        TicketDraft draft)
    {
        switch (status)
        {
            case HttpStatusCode.Created:
                if (!TicketNumber().IsMatch(body))
                {
                    throw Unconfirmed(
                        "osTicket returned HTTP 201 without a ticket " +
                        "number.");
                }

                return new CreatedTicket(body, draft.Subject, null)
                {
                    // Every accepted request is a new ticket.
                    WasIdempotentReplay = false,
                    ViewUrl = _apiClient.GetTicketViewUri(body).ToString()
                };

            // osTicket checks the key, and the address it is bound to,
            // before anything else.
            case HttpStatusCode.Unauthorized:
                throw new InvalidOperationException(
                    "osTicket refused the API key (HTTP 401). The key is " +
                    "wrong or inactive, or it is bound to a different " +
                    $"source IP address than this computer's. {NotCreated}");

            case HttpStatusCode.BadRequest:
                throw new InvalidOperationException(
                    "osTicket rejected the request as malformed " +
                    $"(HTTP 400): {Excerpt(body)} {NotCreated}");

            // osTicket reports validation failures as HTTP 500 with this
            // prefix. Any other 500 may come after a ticket was saved.
            case HttpStatusCode.InternalServerError
                when body.StartsWith(
                    "Unable to create new ticket",
                    StringComparison.Ordinal):
                throw new InvalidOperationException(
                    "osTicket rejected the ticket: " +
                    $"{Excerpt(body)} {NotCreated}");

            default:
                throw Unconfirmed(
                    $"osTicket returned HTTP {(int)status} instead of the " +
                    "expected HTTP 201.");
        }
    }

    /// <summary>
    /// One line, at most MaximumErrorLength characters: enough to name
    /// the rejected field without echoing a whole response.
    /// </summary>
    private static string Excerpt(string body)
    {
        var line =
            Whitespace().Replace(body, " ").Trim();

        if (line.Length == 0)
        {
            return "(no details)";
        }

        return line.Length <= MaximumErrorLength
            ? line
            : line[..MaximumErrorLength] + "…";
    }

    private static InvalidOperationException Unconfirmed(
        string detail,
        Exception? innerException = null) =>
        new($"{detail} {ReconciliationAdvice}", innerException);

    /// <summary>
    /// osTicket's ticket number, which an administrator can format with
    /// letters and dashes as well as digits.
    /// </summary>
    [GeneratedRegex("^[A-Za-z0-9-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex TicketNumber();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
