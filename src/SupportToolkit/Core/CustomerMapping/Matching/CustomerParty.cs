namespace SupportToolkit.Core.CustomerMapping.Matching;

/// <summary>
/// Matching evidence for one side of a potential link.
///
/// Id is the Acronis tenant id or the Business Central customer
/// number. Name, CVR and e-mail are raw, unnormalized source values;
/// null means the source did not supply them.
///
/// The verified Acronis tenant contract supplies name only, so CVR and
/// e-mail are always null on the Acronis side. No Business Central
/// customer source exists yet.
/// </summary>
public sealed record CustomerParty(
    string Id,
    string? Name = null,
    string? Cvr = null,
    string? Email = null
);

public interface ICustomerPartySource
{
    Task<IReadOnlyList<CustomerParty>> GetPartiesAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The match keys, in order of strength.
/// </summary>
public enum CustomerMatchKey
{
    Cvr,
    EmailDomain,
    Name
}
