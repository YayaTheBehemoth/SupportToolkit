using System.Globalization;
using SupportToolkit.Domain.CustomerMapping.Matching;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Providers.Dolibarr.Transport;
using SupportToolkit.Providers.Dolibarr.Transport.Dtos;

namespace SupportToolkit.Providers.Dolibarr.Subscriptions;

/// <summary>
/// Dolibarr's contracts as the Domain's subscription evidence.
///
/// Dolibarr activates and closes each service line of a contract on its
/// own, with its own dates, so every line becomes one subscription
/// (numbered "contract ref/line id") carrying that line's status and dates,
/// and one line naming its service. Decided November 20, 2026.
///
/// - Running line (4) of a validated contract (1): Active.
/// - Never activated (0) or closed (5) line, or a draft (0) or closed (2)
///   contract: Suspended.
/// - Any other status: passed on as unrecognized text, so it is reviewed.
/// - Start: the activation date, else the planned start. End: the closing
///   date, else the planned end; a running line past its planned end is
///   what Dolibarr shows as expired, and the evaluator treats it as ended.
/// - Dolibarr has no "blocked" concept: every subscription is not blocked.
/// - A line without a service (free text) entitles nothing and is left out.
/// - Dates are Unix times, read as UTC dates.
///
/// The evidence is complete or the read fails: an unreadable date or a
/// contract without a reference fails it, never yields a partial list.
/// </summary>
public static class DolibarrSubscriptionMapper
{
    private const string NotBlocked = " ";

    internal static SmartSubscriptionEvidence Map(
        IReadOnlyList<DolibarrContractDto> contracts,
        IReadOnlyList<DolibarrCustomerDto> customers)
    {
        var customerCodes =
            customers
                .Where(customer => !string.IsNullOrWhiteSpace(customer.Id))
                .GroupBy(customer => customer.Id!, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().CustomerCode?.Trim(),
                    StringComparer.Ordinal);

        var headers = new List<SmartSubscriptionHeader>();
        var lines = new List<SmartSubscriptionLine>();

        foreach (var contract in contracts)
        {
            if (string.IsNullOrWhiteSpace(contract.Ref))
            {
                throw new DolibarrApiException(
                    "Dolibarr returned a contract without a reference.");
            }

            var customerNumber =
                contract.CustomerId is { } id
                && customerCodes.TryGetValue(id, out var code)
                && !string.IsNullOrWhiteSpace(code)
                    ? code
                    : null;

            foreach (var line in contract.Lines ?? [])
            {
                if (string.IsNullOrWhiteSpace(line.ProductRef))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line.Id))
                {
                    throw new DolibarrApiException(
                        "Dolibarr returned a contract line without an ID.");
                }

                var number = $"{contract.Ref.Trim()}/{line.Id.Trim()}";

                headers.Add(
                    new SmartSubscriptionHeader(
                        number,
                        customerNumber,
                        Status(contract.Status, line.Status),
                        Date(line.ActualStart, line.PlannedStart),
                        Date(line.ActualEnd, line.PlannedEnd),
                        BillToCustomerNumber: null,
                        BlockedValue: NotBlocked));

                lines.Add(
                    new SmartSubscriptionLine(
                        number,
                        line.ProductRef.Trim(),
                        "Item",
                        OmitLine: false,
                        Quantity: decimal.TryParse(
                            line.Quantity,
                            NumberStyles.Number,
                            CultureInfo.InvariantCulture,
                            out var quantity)
                            ? quantity
                            : null));
            }
        }

        return new SmartSubscriptionEvidence(headers, lines);
    }

    /// <summary>
    /// Customer parties for the requested customer numbers. A code held by
    /// customers of different names keeps no name: a conflict is never
    /// turned into a guess.
    /// </summary>
    internal static ScopedCustomerParties MapCustomers(
        IReadOnlyCollection<string> customerNumbers,
        IReadOnlyList<DolibarrCustomerDto> customers)
    {
        var namesByCode =
            customers
                .Where(customer =>
                    !string.IsNullOrWhiteSpace(customer.CustomerCode)
                    && !string.IsNullOrWhiteSpace(customer.Name))
                .GroupBy(customer => customer.CustomerCode!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(customer => string.Join(' ', customer.Name!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        var parties = new List<CustomerParty>();
        var withoutName = 0;
        var conflicting = 0;

        foreach (var number in customerNumbers
                     .Where(number => !string.IsNullOrWhiteSpace(number))
                     .Select(number => number.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            string? name = null;

            if (namesByCode.TryGetValue(number, out var names))
            {
                if (names.Length == 1)
                {
                    name = names[0];
                }
                else
                {
                    conflicting++;
                }
            }

            if (name is null)
            {
                withoutName++;
            }

            parties.Add(new CustomerParty(number, name));
        }

        return new ScopedCustomerParties(parties, withoutName, conflicting);
    }

    private static string Status(string? contractStatus, string? lineStatus)
    {
        var vocabulary = SmartSubscriptionStatusVocabulary.English;
        var contract = contractStatus?.Trim();
        var line = lineStatus?.Trim();

        if (contract == "1" && line == "4")
        {
            return vocabulary.Active;
        }

        return contract is "0" or "1" or "2" && line is "0" or "4" or "5"
            ? vocabulary.Suspended
            : $"Dolibarr contract status {contract ?? "missing"}, " +
              $"line status {line ?? "missing"}";
    }

    private static DateOnly? Date(LooseTime actual, LooseTime planned)
    {
        if (actual.IsUnreadable || planned.IsUnreadable)
        {
            throw new DolibarrApiException(
                "Dolibarr returned a contract line date that could not be read.");
        }

        return (actual.Value ?? planned.Value) is { } time
            ? DateOnly.FromDateTime(time.UtcDateTime)
            : null;
    }
}
