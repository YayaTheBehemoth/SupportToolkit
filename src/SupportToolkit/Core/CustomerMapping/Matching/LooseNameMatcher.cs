using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SupportToolkit.Core.CustomerMapping.Matching;

public enum LooseNameTier
{
    None,

    /// <summary>
    /// Weak: shared words, neither name contains the other.
    /// </summary>
    Overlap,

    /// <summary>
    /// Every distinctive word of the shorter name occurs in the
    /// longer one, such as "FGT Tables" in "Holding company (FGT Tables)".
    /// </summary>
    Containment,

    /// <summary>
    /// The same distinctive words once case, accents, punctuation, legal
    /// forms and generic words are ignored.
    /// </summary>
    Exact
}

/// <summary>
/// Deliberately loose, deterministic name comparison for a small data
/// set where a person confirms every link.
///
/// Names are casefolded, accents and Danish letters folded (æ to ae,
/// ø to oe, å to aa), legal forms and generic words dropped, and
/// punctuation treated as a separator. Parenthesized text is kept as
/// ordinary words. A name that reduces to no distinctive word never
/// matches anything.
/// </summary>
public static class LooseNameMatcher
{
    /// <summary>
    /// Generic words that say nothing about which organization a name
    /// refers to. Extend here when a real pair is missed for a generic
    /// word.
    /// </summary>
    public static IReadOnlySet<string> NoiseWords { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "aps", "ivs", "amba", "smba", "fmba",
            "holding", "company", "group", "gruppen",
            "danmark", "denmark", "dk",
            "the", "and", "og", "inc", "ltd", "gmbh"
        };

    private const int MinDistinctiveLength = 3;

    private const double OverlapThreshold = 0.6;

    private static readonly Regex LegalForms =
        new(
            @"(?<![\p{L}\p{N}])(a/s|i/s|p/s|k/s|a\.m\.b\.a\.?)(?![\p{L}\p{N}])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Distinctive words of a name in their original order.
    /// </summary>
    public static IReadOnlyList<string> Tokens(
        string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var folded =
            Fold(name.Trim().ToLowerInvariant());

        folded =
            LegalForms.Replace(folded, " ");

        var words = new List<string>();
        var current = new StringBuilder();

        foreach (var character in folded)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
                continue;
            }

            Flush(current, words);
        }

        Flush(current, words);

        return words;
    }

    public static LooseNameTier Compare(
        string? left,
        string? right)
    {
        var a = Tokens(left);
        var b = Tokens(right);

        if (a.Count == 0 || b.Count == 0)
        {
            return LooseNameTier.None;
        }

        var setA = a.ToHashSet(StringComparer.Ordinal);
        var setB = b.ToHashSet(StringComparer.Ordinal);

        if (setA.SetEquals(setB)
            || string.Equals(
                string.Concat(a),
                string.Concat(b),
                StringComparison.Ordinal))
        {
            return LooseNameTier.Exact;
        }

        var (smaller, larger) =
            setA.Count <= setB.Count
                ? (setA, setB)
                : (setB, setA);

        if (smaller.IsSubsetOf(larger))
        {
            return LooseNameTier.Containment;
        }

        var shared =
            setA.Intersect(setB).Count();

        var union =
            setA.Union(setB).Count();

        return shared >= 2
            && (double)shared / union >= OverlapThreshold
                ? LooseNameTier.Overlap
                : LooseNameTier.None;
    }

    private static void Flush(
        StringBuilder current,
        List<string> words)
    {
        if (current.Length == 0)
        {
            return;
        }

        var word = current.ToString();
        current.Clear();

        if (word.Length >= MinDistinctiveLength
            && !NoiseWords.Contains(word))
        {
            words.Add(word);
        }
    }

    private static string Fold(
        string lowered)
    {
        var replaced =
            lowered
                .Replace("æ", "ae", StringComparison.Ordinal)
                .Replace("ø", "oe", StringComparison.Ordinal)
                .Replace("å", "aa", StringComparison.Ordinal)
                .Replace("ä", "ae", StringComparison.Ordinal)
                .Replace("ö", "oe", StringComparison.Ordinal)
                .Replace("ß", "ss", StringComparison.Ordinal);

        var builder = new StringBuilder(replaced.Length);

        foreach (var character in replaced.Normalize(
            NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
