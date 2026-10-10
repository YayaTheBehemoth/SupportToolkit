using System.Security.Cryptography;
using System.Text;
using SupportToolkit.Modules.Ticketing.Models;

namespace SupportToolkit.Modules.Ticketing.Services;

/// <summary>
/// Creates stable idempotency keys for ticket submission.
///
/// The same ticket source producing the same ticket contents will produce the
/// same key, allowing the external provider to protect against ambiguous
/// retries where supported.
/// </summary>
public static class TicketIdempotencyKeyFactory
{
    public static string Create(
        string sourceCommand,
        TicketDraft draft)
    {
        ArgumentNullException.ThrowIfNull(
            draft
        );

        if (string.IsNullOrWhiteSpace(
                sourceCommand))
        {
            throw new ArgumentException(
                "Ticket source command cannot be empty.",
                nameof(sourceCommand)
            );
        }

        if (string.IsNullOrWhiteSpace(
                draft.Subject))
        {
            throw new ArgumentException(
                "Ticket draft subject cannot be empty.",
                nameof(draft)
            );
        }

        if (string.IsNullOrWhiteSpace(
                draft.Body))
        {
            throw new ArgumentException(
                "Ticket draft body cannot be empty.",
                nameof(draft)
            );
        }

        var normalizedSource =
            NormalizeSourceCommand(
                sourceCommand
            );

        var material =
            string.Join(
                '\n',
                normalizedSource,
                draft.Subject,
                draft.Body
            );

        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    material
                )
            );

        var hashText =
            Convert.ToHexString(
                    hash
                )
                .ToLowerInvariant();

        /*
         * A shortened SHA-256 fingerprint is sufficient for the provider's
         * short-lived retry window while keeping the header compact.
         */
        return
            $"supporttoolkit-{normalizedSource}-{hashText[..32]}";
    }

    private static string NormalizeSourceCommand(
        string sourceCommand)
    {
        var builder =
            new StringBuilder();

        foreach (var character in sourceCommand
                     .Trim()
                     .ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(
                    character))
            {
                builder.Append(
                    character
                );

                continue;
            }

            builder.Append(
                '-'
            );
        }

        var normalized =
            builder
                .ToString()
                .Trim('-');

        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "Ticket source command does not contain a usable identifier.",
                nameof(sourceCommand)
            );
        }

        return normalized;
    }
}