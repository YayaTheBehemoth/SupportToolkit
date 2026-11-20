namespace SupportToolkit.Providers.OsTicket.Tickets;

/// <summary>
/// The osTicket user tickets are opened for. osTicket identifies users by
/// email and creates the user on first use.
/// </summary>
public sealed record OsTicketRequester
{
    public string Name { get; }

    public string Email { get; }

    public OsTicketRequester(string name, string email)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "An osTicket requester name is required.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(email)
            || email.Trim().IndexOf('@') <= 0
            || email.Trim().EndsWith('@'))
        {
            throw new ArgumentException(
                "An osTicket requester email address is required.",
                nameof(email));
        }

        Name = name.Trim();
        Email = email.Trim();
    }
}
