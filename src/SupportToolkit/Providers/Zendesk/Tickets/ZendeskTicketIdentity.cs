namespace SupportToolkit.Providers.Zendesk.Tickets;

/// <summary>
/// The Zendesk users a created ticket is attributed to.
///
/// Both are required. Without them Zendesk attributes the ticket to
/// the user the OAuth token acts as, which for client_credentials is
/// the user who created the OAuth client. There is no fallback to that
/// identity.
///
/// They are kept separate because they may differ later, for example
/// a service account submitting on behalf of another requester.
/// </summary>
public sealed record ZendeskTicketIdentity
{
    public long SubmitterId { get; }

    public long RequesterId { get; }

    public ZendeskTicketIdentity(
        long submitterId,
        long requesterId)
    {
        if (submitterId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(submitterId),
                submitterId,
                "A positive Zendesk submitter user ID is required.");
        }

        if (requesterId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requesterId),
                requesterId,
                "A positive Zendesk requester user ID is required.");
        }

        SubmitterId = submitterId;
        RequesterId = requesterId;
    }
}
