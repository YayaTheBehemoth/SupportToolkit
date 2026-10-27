using System.Globalization;

namespace SupportToolkit.Core.CustomerMapping.Matching;

/// <summary>
/// What the matcher sees in a name, and the characters that make a
/// name differ from how it looks: invisible characters, odd spaces,
/// stray apostrophes. For diagnosing names that look identical but do
/// not match.
/// </summary>
public sealed record NameForensics(
    string Compact,
    IReadOnlyList<string> Oddities)
{
    public static NameForensics Inspect(
        string? name)
    {
        var oddities = new List<string>();

        if (string.IsNullOrEmpty(name))
        {
            return new NameForensics(string.Empty, oddities);
        }

        if (name.Length > 0 && char.IsWhiteSpace(name[0]))
        {
            oddities.Add("leading space");
        }

        if (name.Length > 0 && char.IsWhiteSpace(name[^1]))
        {
            oddities.Add("trailing space");
        }

        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            var category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (character == ' ')
            {
                if (index + 1 < name.Length && name[index + 1] == ' ')
                {
                    oddities.Add($"double space at {index}");
                }

                continue;
            }

            // A letter stored as a base letter plus a separate accent
            // looks the same as the single-character form but compares
            // differently in a plain comparison.
            if (category == UnicodeCategory.NonSpacingMark)
            {
                oddities.Add(
                    $"U+{(int)character:X4} (separate accent mark, " +
                    $"decomposed letter) at {index}");

                continue;
            }

            var odd =
                char.IsControl(character)
                || category == UnicodeCategory.Format
                || category == UnicodeCategory.SpaceSeparator
                || category == UnicodeCategory.LineSeparator
                || category == UnicodeCategory.ParagraphSeparator
                || IsApostropheLike(character);

            if (odd)
            {
                oddities.Add(
                    $"U+{(int)character:X4} ({Describe(character, category)}) " +
                    $"at {index}");
            }
        }

        return new NameForensics(
            LooseNameMatcher.Compact(name),
            oddities);
    }

    private static bool IsApostropheLike(
        char character) =>
        "'’‘ʼ´`".Contains(character);

    private static string Describe(
        char character,
        UnicodeCategory category)
    {
        if (IsApostropheLike(character))
        {
            return "apostrophe";
        }

        return character switch
        {
            ' ' => "no-break space",
            '​' => "zero-width space",
            '‌' => "zero-width non-joiner",
            '‍' => "zero-width joiner",
            '﻿' => "byte-order mark",
            '­' => "soft hyphen",
            '\t' => "tab",
            _ => category.ToString()
        };
    }
}
