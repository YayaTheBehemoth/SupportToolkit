using System.Text.RegularExpressions;

namespace SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;

/// <summary>
/// Business Central serializes enum members as XML-encoded AL names,
/// for example "_x0020_" for the blank member and
/// "G_x002F_L_x0020_Account" for "G/L Account". These names are
/// language independent. Decoding restores the AL member name; no
/// meaning is attached to it here.
/// </summary>
public static class BusinessCentralEnumNames
{
    private static readonly Regex EncodedCharacter =
        new(
            "_x([0-9A-Fa-f]{4})_",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? Decode(
        string? value)
    {
        return value is null
            ? null
            : EncodedCharacter.Replace(
                value,
                match => ((char)Convert.ToInt32(
                    match.Groups[1].Value,
                    16)).ToString());
    }
}
