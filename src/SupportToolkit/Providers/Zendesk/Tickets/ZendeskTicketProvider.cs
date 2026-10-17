
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Providers.Zendesk.Tickets.Dtos;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Providers.Zendesk.Tickets;

/// <summary>
/// Zendesk implementation of the SupportToolkit ticket provider.
///
/// Ticket creation is a write operation. A missing, invalid, or failed
/// response must not be interpreted as proof that no ticket was created.
/// </summary>
public sealed class ZendeskTicketProvider : ITicketProvider
{
    private const string ReconciliationAdvice =
        "Ticket creation was not confirmed. " +
        "Check the selected Zendesk destination for an existing ticket " +
        "before retrying.";

    private readonly ZendeskApiClient _apiClient;
    private readonly long _groupId;

    public ZendeskTicketProvider(
        ZendeskApiClient apiClient,
        long groupId)
    {
        _apiClient =
            apiClient
            ?? throw new ArgumentNullException(nameof(apiClient));

        if (groupId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(groupId),
                groupId,
                "A positive Zendesk destination group ID is required.");
        }

        _groupId = groupId;
    }

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
            new ZendeskCreateTicketRequestDto
            {
                Ticket =
                    new ZendeskCreateTicketDto
                    {
                        Subject = draft.Subject,
                        GroupId = _groupId,

                        Comment =
                            new ZendeskTicketCommentDto
                            {
                                Body = draft.Body,
                                Public = false
                            }
                    }
            };

        HttpResponseMessage response;

        try
        {
            response =
                await _apiClient.PostTicketAsync(
                    request,
                    idempotencyKey,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            var detail =
                exception.StatusCode is HttpStatusCode status
                    ? $"Zendesk returned HTTP {(int)status}."
                    : "A Zendesk HTTP request failed without a response.";

            throw Unconfirmed(detail, exception);
        }
        catch (OperationCanceledException exception)
        {
            throw Unconfirmed(
                "A Zendesk request timed out or was interrupted.",
                exception);
        }
        catch (JsonException exception)
        {
            throw Unconfirmed(
                "Zendesk returned invalid authentication data.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw Unconfirmed(
                "Zendesk authentication or submission failed.",
                exception);
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.Created)
            {
                throw Unconfirmed(
                    $"Zendesk returned HTTP {(int)response.StatusCode} " +
                    "instead of the expected HTTP 201.");
            }

            ZendeskCreateTicketResponseDto? payload;

            try
            {
                payload =
                    await response.Content
                        .ReadFromJsonAsync<ZendeskCreateTicketResponseDto>(
                            cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                throw Unconfirmed(
                    "Could not read the Zendesk ticket creation response.",
                    exception);
            }
            catch (OperationCanceledException exception)
            {
                throw Unconfirmed(
                    "Reading the Zendesk ticket response timed out.",
                    exception);
            }
            catch (JsonException exception)
            {
                throw Unconfirmed(
                    "Zendesk returned HTTP 201 but the ticket response " +
                    "could not be decoded.",
                    exception);
            }
            catch (NotSupportedException exception)
            {
                throw Unconfirmed(
                    "Zendesk returned HTTP 201 with an unsupported " +
                    "response format.",
                    exception);
            }

            if (payload?.Ticket is null
                || payload.Ticket.Id <= 0)
            {
                throw Unconfirmed(
                    "Zendesk returned HTTP 201 without a valid ticket ID.");
            }

            var ticket = payload.Ticket;

            return new CreatedTicket(
                ticket.Id.ToString(),
                ticket.Subject,
                ticket.Url)
            {
                WasIdempotentReplay =
                    ReadIdempotencyReplay(response),

                ViewUrl =
                    _apiClient.GetTicketViewUri(ticket.Id).ToString()
            };
        }
    }

    private static bool? ReadIdempotencyReplay(
        HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(
                "x-idempotency-lookup",
                out var values))
        {
            return null;
        }

        var results = values.ToArray();

        if (results.Length != 1)
        {
            return null;
        }

        var value = results[0].Trim();

        if (string.Equals(
                value,
                "hit",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(
                value,
                "miss",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static InvalidOperationException Unconfirmed(
        string detail,
        Exception? innerException = null)
    {
        return new InvalidOperationException(
            $"{detail} {ReconciliationAdvice}",
            innerException);
    }
}
