using System.Net;
using System.Net.Http.Json;
using SupportToolkit.Modules.Ticketing;
using SupportToolkit.Modules.Ticketing.Models;
using SupportToolkit.Providers.Zendesk.Tickets.Dtos;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Providers.Zendesk.Tickets;

/// <summary>
/// Zendesk implementation of the SupportToolkit ticket provider.
///
/// Zendesk-specific request and response models remain inside the provider
/// boundary and are translated to and from SupportToolkit ticket models.
/// </summary>
public sealed class ZendeskTicketProvider
    : ITicketProvider
{
    private readonly ZendeskApiClient _apiClient;

    public ZendeskTicketProvider(
        ZendeskApiClient apiClient)
    {
        _apiClient =
            apiClient;
    }

    public async Task<CreatedTicket>
        CreateTicketAsync(
            TicketDraft draft,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            draft
        );

        if (string.IsNullOrWhiteSpace(
                draft.Subject))
        {
            throw new ArgumentException(
                "A ticket requires a subject.",
                nameof(draft)
            );
        }

        if (string.IsNullOrWhiteSpace(
                draft.Body))
        {
            throw new ArgumentException(
                "A ticket requires a body.",
                nameof(draft)
            );
        }

        var request =
            new ZendeskCreateTicketRequestDto
            {
                Ticket =
                    new ZendeskCreateTicketDto
                    {
                        Subject =
                            draft.Subject,

                        Comment =
                            new ZendeskTicketCommentDto
                            {
                                Body =
                                    draft.Body,

                                /*
                                 * SupportToolkit currently creates internal
                                 * operational tickets only. Zendesk comments
                                 * therefore default to private at the provider
                                 * boundary.
                                 */
                                Public =
                                    false
                            }
                    }
            };

        using var response =
            await _apiClient.PostTicketAsync(
                request,
                idempotencyKey,
                cancellationToken
            );

        if (response.StatusCode
            != HttpStatusCode.Created)
        {
            response.EnsureSuccessStatusCode();

            throw new InvalidOperationException(
                $"Zendesk returned unexpected status " +
                $"{(int)response.StatusCode} " +
                $"while creating a ticket."
            );
        }

        var payload =
            await response.Content
                .ReadFromJsonAsync<ZendeskCreateTicketResponseDto>(
                    cancellationToken:
                        cancellationToken
                )
            ?? throw new InvalidOperationException(
                "Zendesk returned an empty ticket creation response."
            );

        var ticket =
            payload.Ticket
            ?? throw new InvalidOperationException(
                "Zendesk returned a ticket response without a ticket."
            );

        if (ticket.Id <= 0)
        {
            throw new InvalidOperationException(
                "Zendesk returned an invalid created ticket ID."
            );
        }

        return new CreatedTicket(
            ticket.Id.ToString(),
            ticket.Subject,
            ticket.Url
        );
    }
}