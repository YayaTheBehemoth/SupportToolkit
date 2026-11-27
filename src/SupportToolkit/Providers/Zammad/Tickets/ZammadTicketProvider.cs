using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Providers.Zammad.Tickets.Dtos;
using SupportToolkit.Providers.Zammad.Transport;

namespace SupportToolkit.Providers.Zammad.Tickets;

/// <summary>
/// Zammad implementation of the SupportToolkit ticket provider.
///
/// Zammad has no idempotency: every accepted request is a new ticket, and
/// an Idempotency-Key header is ignored. So the key is not sent, nothing
/// is retried, and only an answer Zammad gives before creating anything (a
/// refused token, a missing permission, a malformed request, a validation
/// error such as an unknown group) counts as "not created". Any other
/// failure leaves the outcome unknown, and the message says to check
/// Zammad before trying again.
/// </summary>
public sealed partial class ZammadTicketProvider : ITicketProvider
{
    private const string ReconciliationAdvice =
        "Ticket creation was not confirmed. Check Zammad for an existing " +
        "ticket before retrying: Zammad has no duplicate protection, so a " +
        "retry creates another ticket.";

    private const string NotCreated =
        "No ticket was created.";

    private const int MaximumErrorLength = 300;

    private readonly ZammadApiClient _apiClient;
    private readonly string _group;
    private readonly string _customer;

    public ZammadTicketProvider(
        ZammadApiClient apiClient,
        string group,
        string customer)
    {
        _apiClient =
            apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        if (string.IsNullOrWhiteSpace(group))
        {
            throw new ArgumentException(
                "A Zammad group name is required.",
                nameof(group));
        }

        if (string.IsNullOrWhiteSpace(customer)
            || customer.Trim().IndexOf('@') <= 0
            || customer.Trim().EndsWith('@'))
        {
            throw new ArgumentException(
                "A Zammad customer email address is required.",
                nameof(customer));
        }

        _group = group.Trim();
        _customer = customer.Trim();
    }

    /// <param name="idempotencyKey">
    /// Ignored: Zammad has no idempotency mechanism to pass it to.
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
            new ZammadCreateTicketRequestDto
            {
                Title = draft.Subject,
                Group = _group,
                Customer = "guess:" + _customer,
                Article =
                    new ZammadArticleDto
                    {
                        Subject = draft.Subject,
                        Body = draft.Body
                    }
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
                "A Zammad HTTP request failed without a response.",
                exception);
        }
        catch (OperationCanceledException exception)
        {
            throw Unconfirmed(
                "A Zammad request timed out or was interrupted.",
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
                    $"Zammad returned HTTP {(int)response.StatusCode} " +
                    "but the response could not be read.",
                    exception);
            }

            return Interpret(response.StatusCode, body, draft);
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
                var ticket = Read<ZammadTicketDto>(body);

                if (ticket?.Id is not > 0
                    || string.IsNullOrWhiteSpace(ticket.Number))
                {
                    throw Unconfirmed(
                        "Zammad returned HTTP 201 without a ticket ID and " +
                        "number.");
                }

                return new CreatedTicket(ticket.Number.Trim(), draft.Subject, null)
                {
                    // Every accepted request is a new ticket.
                    WasIdempotentReplay = false,
                    ViewUrl = _apiClient.GetTicketViewUri(ticket.Id.Value).ToString()
                };

            case HttpStatusCode.Unauthorized:
                throw new InvalidOperationException(
                    "Zammad refused the API token (HTTP 401): it is wrong, " +
                    $"inactive or expired. {NotCreated}");

            case HttpStatusCode.Forbidden:
                throw new InvalidOperationException(
                    "Zammad refused the request (HTTP 403): the token's " +
                    "user may not create tickets in this group " +
                    $"({Detail(body)}). {NotCreated}");

            case HttpStatusCode.BadRequest:
                throw new InvalidOperationException(
                    "Zammad rejected the request as malformed " +
                    $"(HTTP 400): {Detail(body)} {NotCreated}");

            // Validation: an unknown group, a missing title, an invalid
            // customer address. Zammad checks these before saving.
            case HttpStatusCode.UnprocessableEntity:
                throw new InvalidOperationException(
                    "Zammad rejected the ticket: " +
                    $"{Detail(body)} {NotCreated}");

            default:
                throw Unconfirmed(
                    $"Zammad returned HTTP {(int)status} instead of the " +
                    "expected HTTP 201.");
        }
    }

    /// <summary>
    /// Zammad's own message (error_human, else error), on one line and at
    /// most MaximumErrorLength characters; the raw body when it is not
    /// Zammad's error JSON.
    /// </summary>
    private static string Detail(string body)
    {
        var error = Read<ZammadErrorDto>(body);

        var text =
            error?.ErrorHuman
            ?? error?.Error
            ?? body;

        var line = Whitespace().Replace(text, " ").Trim();

        if (line.Length == 0)
        {
            return "(no details)";
        }

        return line.Length <= MaximumErrorLength
            ? line
            : line[..MaximumErrorLength] + "…";
    }

    private static T? Read<T>(string body)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static InvalidOperationException Unconfirmed(
        string detail,
        Exception? innerException = null) =>
        new($"{detail} {ReconciliationAdvice}", innerException);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
