using System.Text.RegularExpressions;

namespace SupportToolkit.Core.CustomerMapping.Matching;

public enum NormalizedKeyStatus
{
    Ok,
    Empty,
    Malformed,
    Excluded
}

/// <summary>
/// A normalized key. Value is set only when Status is Ok.
/// </summary>
public sealed record NormalizedKey(
    NormalizedKeyStatus Status,
    string? Value = null)
{
    public static NormalizedKey Empty { get; } =
        new(NormalizedKeyStatus.Empty);

    public static NormalizedKey Malformed { get; } =
        new(NormalizedKeyStatus.Malformed);

    public static NormalizedKey Excluded { get; } =
        new(NormalizedKeyStatus.Excluded);
}

/// <summary>
/// Deterministic normalization of match keys.
///
/// Normalization is strict: a value that does not fit the expected
/// shape is Malformed rather than repaired.
/// </summary>
public static class PartyKeyNormalizer
{
    private static readonly Regex Whitespace =
        new(
            @"\s+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EightDigits =
        new(
            "^[0-9]{8}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DomainShape =
        new(
            "^[a-z0-9-]+(\\.[a-z0-9-]+)+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Shared mailbox providers. A domain on this list says nothing
    /// about which organization owns the address.
    /// </summary>
    public static IReadOnlySet<string> PublicMailDomains { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "gmail.com",
            "googlemail.com",
            "hotmail.com",
            "hotmail.dk",
            "outlook.com",
            "outlook.dk",
            "live.com",
            "live.dk",
            "msn.com",
            "yahoo.com",
            "yahoo.dk",
            "icloud.com",
            "me.com",
            "mail.dk",
            "jubii.dk",
            "aol.com",
            "proton.me",
            "protonmail.com"
        };

    private static readonly string[] LegalSuffixes =
    [
        "aps",
        "a/s",
        "i/s",
        "ivs",
        "p/s",
        "k/s"
    ];

    public static NormalizedKey Normalize(
        CustomerMatchKey key,
        CustomerParty party)
    {
        ArgumentNullException.ThrowIfNull(party);

        return key switch
        {
            CustomerMatchKey.Cvr => Cvr(party.Cvr),
            CustomerMatchKey.EmailDomain => EmailDomain(party.Email),
            CustomerMatchKey.Name => Name(party.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
    }

    /// <summary>
    /// Removes whitespace and an optional DK prefix; the remainder
    /// must be exactly eight digits.
    /// </summary>
    public static NormalizedKey Cvr(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NormalizedKey.Empty;
        }

        var compact =
            Whitespace.Replace(
                value,
                string.Empty
            );

        if (compact.StartsWith(
            "DK",
            StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[2..];
        }

        return EightDigits.IsMatch(compact)
            ? new NormalizedKey(NormalizedKeyStatus.Ok, compact)
            : NormalizedKey.Malformed;
    }

    /// <summary>
    /// Accepts an e-mail address or a bare domain. Lowercases, strips
    /// "www." and excludes public mail domains.
    /// </summary>
    public static NormalizedKey EmailDomain(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NormalizedKey.Empty;
        }

        var text =
            value.Trim().ToLowerInvariant();

        var at =
            text.LastIndexOf('@');

        var domain =
            (at >= 0 ? text[(at + 1)..] : text)
                .TrimEnd('.');

        if (domain.StartsWith(
            "www.",
            StringComparison.Ordinal))
        {
            domain = domain[4..];
        }

        if (!DomainShape.IsMatch(domain))
        {
            return NormalizedKey.Malformed;
        }

        return PublicMailDomains.Contains(domain)
            ? NormalizedKey.Excluded
            : new NormalizedKey(NormalizedKeyStatus.Ok, domain);
    }

    /// <summary>
    /// Casefolds, trims, collapses whitespace and strips one trailing
    /// Danish legal-form suffix. A name that is only a suffix is
    /// Malformed.
    /// </summary>
    public static NormalizedKey Name(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NormalizedKey.Empty;
        }

        var name =
            Whitespace.Replace(
                value.Trim().ToLowerInvariant(),
                " "
            );

        foreach (var suffix in LegalSuffixes)
        {
            if (name == suffix)
            {
                return NormalizedKey.Malformed;
            }

            if (name.EndsWith(
                " " + suffix,
                StringComparison.Ordinal))
            {
                name =
                    name[..^(suffix.Length + 1)]
                        .TrimEnd(' ', ',');

                break;
            }
        }

        return name.Length == 0
            ? NormalizedKey.Malformed
            : new NormalizedKey(NormalizedKeyStatus.Ok, name);
    }
}
