using SupportToolkit.Modules.Ticketing.Models;

namespace SupportToolkit.Modules.Ticketing.Reporting;

/// <summary>
/// Writes a ticket draft to the console for operator review without contacting
/// or submitting to any external ticketing provider.
/// </summary>
public sealed class ConsoleTicketDraftPreviewer
{
    public void Write(
        TicketDraft draft)
    {
        ArgumentNullException.ThrowIfNull(
            draft
        );

        Console.WriteLine();

        Console.WriteLine(
            "SUPPORTTOOLKIT // TICKET PREVIEW"
        );

        Console.WriteLine(
            "================================"
        );

        Console.WriteLine();

        Console.WriteLine(
            "SUBJECT"
        );

        Console.WriteLine(
            "-------"
        );

        Console.WriteLine(
            draft.Subject
        );

        Console.WriteLine();

        Console.WriteLine(
            "BODY"
        );

        Console.WriteLine(
            "----"
        );

        Console.WriteLine(
            draft.Body
        );

        Console.WriteLine();

        Console.WriteLine(
            "PREVIEW ONLY - no ticket was submitted."
        );

        Console.WriteLine();
    }
}