using System.Globalization;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Providers.BusinessCentral.Subscriptions;

/// <summary>
/// The environment and company the configured connection is
/// expected to read from.
///
/// The URL already pins the environment and company ID. Row-level
/// values, when a row reports them, must agree with this context.
/// </summary>
public sealed record BusinessCentralSourceContext(
    string Environment,
    string? CompanyName)
{
    public static BusinessCentralSourceContext From(
        BusinessCentralOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new BusinessCentralSourceContext(
            options.Environment,
            options.CompanyName
        );
    }
}

/// <summary>
/// Maps Business Central subscription DTOs to normalized evidence.
///
/// Raw evidence (blocked value, line type, omit flag, both customer
/// numbers) is preserved for the evaluator. No business rule is
/// applied here.
///
/// Source-context violations and uninterpretable data are collection
/// errors that fail the whole collection. Rows are never filtered
/// out because they come from another company or environment.
/// </summary>
public static class BusinessCentralSubscriptionMapper
{
    /*
     * Business Central may return a sentinel for an empty date.
     * These two representations are treated as "no date". This is
     * platform behavior, not a business rule, but it is unverified
     * against the production pages.
     */
    private static readonly string[] EmptyDateSentinels =
    [
        "0001-01-01",
        "0000-12-31"
    ];

    public static IReadOnlyList<SmartSubscriptionHeader> MapHeaders(
        IReadOnlyList<BusinessCentralSubscriptionHeaderDto?> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return headers
            .Select(
                header =>
                {
                    RequireRow(header);

                    return new SmartSubscriptionHeader(
                        header!.Number?.Trim() ?? string.Empty,
                        NullIfBlank(header.SellToCustomerNumber),
                        header.Status ?? string.Empty,
                        ParseDate(header.StartDate),
                        ParseDate(header.EndDate),
                        NullIfBlank(header.BillToCustomerNumber),
                        BusinessCentralEnumNames.Decode(header.Blocked)
                    );
                }
            )
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Maps lines after validating their company and environment.
    /// Header rows carry no such columns and cannot be validated.
    /// </summary>
    public static IReadOnlyList<SmartSubscriptionLine> MapLines(
        IReadOnlyList<BusinessCentralSubscriptionLineDto?> lines,
        BusinessCentralSourceContext context)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(context);

        return lines
            .Select(
                line =>
                {
                    RequireRow(line);

                    RequireContext(line!, context);

                    return new SmartSubscriptionLine(
                        line!.SubscriptionNumber?.Trim()
                            ?? string.Empty,
                        line.Number?.Trim() ?? string.Empty,
                        BusinessCentralEnumNames.Decode(line.Type),
                        line.OmitLine,
                        line.Quantity
                    );
                }
            )
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Row-level company and environment columns are observed to be
    /// empty on the production pages, so a blank value is treated as
    /// not reported. A populated value that differs from the
    /// configured context, or that cannot be checked because no
    /// expected value is configured, fails the collection.
    /// </summary>
    private static void RequireContext(
        BusinessCentralSubscriptionLineDto line,
        BusinessCentralSourceContext context)
    {
        if (Conflicts(line.Environment, context.Environment)
            || Conflicts(line.Company, context.CompanyName))
        {
            throw new BusinessCentralContractException(
                "A subscription line reports a company or " +
                "environment that does not match the " +
                "configured context."
            );
        }
    }

    private static bool Conflicts(
        string? reported,
        string? expected)
    {
        if (string.IsNullOrWhiteSpace(reported))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(expected)
            || !string.Equals(
                reported.Trim(),
                expected.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static void RequireRow(
        object? row)
    {
        if (row is null)
        {
            throw new BusinessCentralContractException(
                "Business Central returned an empty record."
            );
        }
    }

    private static string? NullIfBlank(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static DateOnly? ParseDate(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text =
            raw.Trim();

        if (EmptyDateSentinels.Contains(
                text,
                StringComparer.Ordinal))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(
                text,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            throw new BusinessCentralContractException(
                "Business Central returned a date in an " +
                "unexpected format."
            );
        }

        return date;
    }
}
