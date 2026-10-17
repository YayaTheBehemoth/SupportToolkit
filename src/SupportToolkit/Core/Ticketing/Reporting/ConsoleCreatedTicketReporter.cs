
using SupportToolkit.Core.Ticketing.Models;

namespace SupportToolkit.Core.Ticketing.Reporting;

/// <summary>
/// Human-readable output for a confirmed ticket creation response.
///
/// Distinguishes new creation, idempotent replay, and unknown replay status.
/// </summary>
public static class ConsoleCreatedTicketReporter
{
    public static void Write(
        TextWriter output,
        CreatedTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(ticket);

        output.WriteLine();

        if (ticket.WasIdempotentReplay == true)
        {
            output.WriteLine("Ticket already submitted.");
            output.WriteLine(
                "Zendesk recognized this as a repeat submission. " +
                "No new ticket was created.");
        }
        else if (ticket.WasIdempotentReplay == false)
        {
            output.WriteLine("New ticket created successfully.");
        }
        else
        {
            output.WriteLine("Ticket confirmed by Zendesk.");
            output.WriteLine(
                "Zendesk did not report whether this was " +
                "a new creation or an idempotent replay.");
        }

        output.WriteLine();

        output.WriteLine(
            $"Ticket ID: {ticket.Id}");

        if (!string.IsNullOrWhiteSpace(ticket.Subject))
        {
            output.WriteLine(
                $"Subject: {Inline(ticket.Subject)}");
        }

        if (!string.IsNullOrWhiteSpace(ticket.ViewUrl))
        {
            output.WriteLine(
                $"Open ticket: {ticket.ViewUrl}");
        }
        else if (!string.IsNullOrWhiteSpace(ticket.Url))
        {
            output.WriteLine(
                $"API URL: {ticket.Url}");
        }

        output.WriteLine();
    }

    private static string Inline(string value)
    {
        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}
