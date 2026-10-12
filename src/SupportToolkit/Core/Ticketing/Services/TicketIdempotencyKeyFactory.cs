
using System.Security.Cryptography;
using System.Text;
using SupportToolkit.Core.Ticketing.Models;

namespace SupportToolkit.Core.Ticketing.Services;

/// <summary>
/// Creates stable idempotency keys for ticket submission.
///
/// Identical source, destination, and ticket contents produce identical keys.
///
/// Different destinations produce different keys so that submissions to
/// separate operational queues do not share retry identities.
///
/// Idempotency is not permanent ticket deduplication. Actual protection
/// depends on the external provider's retention window.
/// </summary>
public static class TicketIdempotencyKeyFactory
{
    public static string Create(
        string sourceCommand,
        TicketDraft draft,
        string destinationIdentity)
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

        if (string.IsNullOrWhiteSpace(
                destinationIdentity))
        {
            throw new ArgumentException(
                "Ticket destination identity cannot be empty.",
                nameof(destinationIdentity)
            );
        }

        var normalizedSource =
            NormalizeSourceCommand(
                sourceCommand
            );

        var normalizedDestination =
            destinationIdentity
                .Trim()
                .ToLowerInvariant();

        var material =
            string.Join(
                '\n',
                normalizedSource,
                normalizedDestination,
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
