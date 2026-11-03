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
    /// The same name apart from a single typo in a long name, such as
    /// one wrong letter. Numbers must be identical.
    /// </summary>
    NearExact,

    /// <summary>
    /// The same distinctive words once case, accents, punctuation,
    /// spacing, legal forms and generic words are ignored.
    /// </summary>
    Exact
}

/// <summary>
/// Deliberately loose, deterministic name comparison for a small data
/// set where a person confirms every link.
///
/// Names are casefolded, accents and Danish letters folded (æ to ae,
/// ø to oe, å to aa), legal forms and generic words dropped, and
/// punctuation treated as a separator. Apostrophes and invisible
/// characters are deleted, not treated as separators, so a stray one
/// inside a word does not split it. Parenthesized text is kept as
/// ordinary words. Numbers are always kept. A name that reduces to no
/// distinctive word never matches anything.
///
/// Spacing and punctuation are ignored outright when two names are
/// compared whole, so a stray space inside a word does not matter, and
/// one typo in a long name is tolerated.
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

    // A word may differ from another only by a short ending (genitive
    // or plural), and only when the shared part is long enough to be
    // distinctive.
    private const int MinStemLength = 6;

    private const int MaxEndingLength = 2;

    // A name whose spacing differs is only searched for inside
    // another name when it is long enough to be distinctive.
    private const int MinRunLength = 8;

    // One typo is tolerated only in names long enough that a single
    // wrong letter cannot make a different name, and two in very long
    // ones.
    private const int MinNearLength = 10;

    private const int LongNameLength = 16;

    private const double OverlapThreshold = 0.6;

    private static readonly Regex LegalForms =
        new(
            @"(?<![\p{L}\p{N}])(a/s|i/s|p/s|k/s|a\.m\.b\.a\.?)(?![\p{L}\p{N}])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Distinctive words of a name in their original order.
    /// </summary>
    public static IReadOnlyList<string> Tokens(
        string? name) =>
        Split(name, keepShortWords: false);

    /// <summary>
    /// The whole name reduced to letters and digits: spacing,
    /// punctuation, legal forms and generic words removed, short words
    /// kept. Two names with the same compact form are the same name
    /// written differently.
    /// </summary>
    public static string Compact(
        string? name) =>
        string.Concat(Split(name, keepShortWords: true));

    private static List<string> Split(
        string? name,
        bool keepShortWords)
    {
        var words = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            return words;
        }

        var folded =
            Fold(name.Trim().ToLowerInvariant());

        folded =
            LegalForms.Replace(folded, " ");

        var current = new StringBuilder();

        foreach (var character in folded)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
                continue;
            }

            Flush(current, words, keepShortWords);
        }

        Flush(current, words, keepShortWords);

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

        var compactA = Compact(left);
        var compactB = Compact(right);

        if (setA.SetEquals(setB)
            || (compactA.Length >= MinDistinctiveLength
                && string.Equals(
                    compactA,
                    compactB,
                    StringComparison.Ordinal)))
        {
            return LooseNameTier.Exact;
        }

        if (IsOneTypoApart(compactA, compactB))
        {
            return LooseNameTier.NearExact;
        }

        var (smaller, larger) =
            setA.Count <= setB.Count
                ? (setA, setB)
                : (setB, setA);

        if (smaller.All(
                word => larger.Any(
                    other => SameWord(word, other)))
            || ContainsAsWordRun(left, right)
            || ContainsAsWordRun(right, left))
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
        List<string> words,
        bool keepShortWords)
    {
        if (current.Length == 0)
        {
            return;
        }

        var word = current.ToString();
        current.Clear();

        if (NoiseWords.Contains(word))
        {
            return;
        }

        // Numbers always count: "Byg 1" and "Byg 2" are different
        // customers. Short letter words only count in the compact form.
        var isNumber = word.All(char.IsDigit);

        if (isNumber
            || keepShortWords
            || word.Length >= MinDistinctiveLength)
        {
            words.Add(word);
        }
    }

    /// <summary>
    /// Two words that are the same word in different grammatical
    /// forms: identical, or one is the other plus an ending of at most
    /// two letters, such as the Danish possessive in "teknikerne" and
    /// "teknikernes". Short words are never stretched this way, so
    /// unrelated short words cannot match.
    /// </summary>
    private static bool SameWord(
        string a,
        string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        var (shorter, longer) =
            a.Length <= b.Length
                ? (a, b)
                : (b, a);

        return shorter.Length >= MinStemLength
            && longer.Length - shorter.Length <= MaxEndingLength
            && longer.StartsWith(shorter, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the compact form of <paramref name="inner"/> equals
    /// a run of whole, consecutive words of <paramref name="outer"/>.
    /// This finds a name with a stray space inside a longer name, such
    /// as "Nor dic Foods" in "Nordic Foods Service". The run must
    /// start and end on word boundaries, so a short name cannot match
    /// the middle of another word.
    /// </summary>
    private static bool ContainsAsWordRun(
        string? inner,
        string? outer)
    {
        var target = Compact(inner);

        if (target.Length < MinRunLength)
        {
            return false;
        }

        var words = Split(outer, keepShortWords: true);

        for (var start = 0; start < words.Count; start++)
        {
            var joined = new StringBuilder();

            for (var end = start; end < words.Count; end++)
            {
                joined.Append(words[end]);

                if (joined.Length > target.Length
                    || !target.StartsWith(
                        joined.ToString(),
                        StringComparison.Ordinal))
                {
                    break;
                }

                if (joined.Length == target.Length
                    && end - start + 1 < words.Count)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsOneTypoApart(
        string a,
        string b)
    {
        if (Math.Min(a.Length, b.Length) < MinNearLength)
        {
            return false;
        }

        // The numbers must agree exactly; only letters may differ.
        if (!string.Equals(
            new string(a.Where(char.IsDigit).ToArray()),
            new string(b.Where(char.IsDigit).ToArray()),
            StringComparison.Ordinal))
        {
            return false;
        }

        var allowed =
            Math.Min(a.Length, b.Length) >= LongNameLength
                ? 2
                : 1;

        return Math.Abs(a.Length - b.Length) <= allowed
            && EditDistance(a, b, allowed) <= allowed;
    }

    /// <summary>
    /// Levenshtein distance, giving up early once it must exceed the
    /// allowance.
    /// </summary>
    private static int EditDistance(
        string a,
        string b,
        int allowed)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMinimum = current[0];

            for (var j = 1; j <= b.Length; j++)
            {
                var cost =
                    a[i - 1] == b[j - 1]
                        ? 0
                        : 1;

                current[j] =
                    Math.Min(
                        Math.Min(
                            previous[j] + 1,
                            current[j - 1] + 1),
                        previous[j - 1] + cost);

                rowMinimum = Math.Min(rowMinimum, current[j]);
            }

            if (rowMinimum > allowed)
            {
                return allowed + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static string Fold(
        string lowered)
    {
        // An accented letter can be one character or a letter plus a
        // separate accent mark; the two look identical. Compose first,
        // so "å" is "å" whichever way it was stored and folds to "aa".
        var replaced =
            lowered
                .Normalize(NormalizationForm.FormC)
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
            var category =
                CharUnicodeInfo.GetUnicodeCategory(character);

            // Accent marks, invisible format characters (such as a
            // zero-width space) and apostrophes are deleted, so they
            // never split a word.
            if (category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.Format
                || Apostrophes.Contains(character))
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private const string Apostrophes =
        "'’‘ʼ´`";
}
