using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;

namespace SupportToolkit.Providers.BusinessCentral.Subscriptions;

/// <summary>
/// Matching evidence for the Business Central side, plus counts that
/// say how trustworthy it is.
/// </summary>
public sealed record BusinessCentralCustomerParties(
    IReadOnlyList<CustomerParty> Parties,
    int CustomersWithoutName,
    int CustomersWithConflictingNames)
{
    public int CustomersWithName =>
        Parties.Count - CustomersWithoutName;
}

/// <summary>
/// Builds customer parties from the customer name carried on
/// subscription header rows.
///
/// The customer number is the party id and also its account number.
/// A customer whose rows report different names keeps no name: an
/// unresolved conflict is never turned into one guess. Names that
/// differ only in case or spacing count as the same name.
/// </summary>
public static class BusinessCentralCustomerPartyMapper
{
    public static BusinessCentralCustomerParties Map(
        IEnumerable<string> customerNumbers,
        IReadOnlyList<BusinessCentralHeaderCustomerNameDto?> headerRows)
    {
        ArgumentNullException.ThrowIfNull(customerNumbers);
        ArgumentNullException.ThrowIfNull(headerRows);

        var namesByCustomer =
            headerRows
                .Where(row => row is not null)
                .Select(row => row!)
                .Where(
                    row => !string.IsNullOrWhiteSpace(
                            row.SellToCustomerNumber)
                        && !string.IsNullOrWhiteSpace(
                            row.SellToCustomerName))
                .GroupBy(
                    row => row.SellToCustomerNumber!.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(
                            row => string.Join(
                                ' ',
                                row.SellToCustomerName!
                                    .Split(
                                        (char[]?)null,
                                        StringSplitOptions
                                            .RemoveEmptyEntries)))
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
            .OrderBy(number => number, StringComparer.OrdinalIgnoreCase))
        {
            string? name = null;

            if (namesByCustomer.TryGetValue(number, out var names))
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

            parties.Add(
                new CustomerParty(
                    number,
                    name));
        }

        return new BusinessCentralCustomerParties(
            parties,
            withoutName,
            conflicting);
    }
}
