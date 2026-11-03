using SupportToolkit.Core.CustomerMapping.Matching;

namespace SupportToolkit.Tests;

public class PartyKeyNormalizerTests
{
    [Theory]
    [InlineData("12345678", "12345678")]
    [InlineData("DK12345678", "12345678")]
    [InlineData("dk 12 34 56 78", "12345678")]
    [InlineData(" 1234 5678 ", "12345678")]
    public void Cvr_ValidForms_Normalize(
        string value,
        string expected)
    {
        var key =
            PartyKeyNormalizer.Cvr(value);

        Assert.Equal(NormalizedKeyStatus.Ok, key.Status);
        Assert.Equal(expected, key.Value);
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("SE12345678")]
    [InlineData("1234-5678")]
    [InlineData("DK")]
    public void Cvr_OtherShapes_AreMalformed(
        string value)
    {
        Assert.Equal(
            NormalizedKeyStatus.Malformed,
            PartyKeyNormalizer.Cvr(value).Status
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingValues_AreEmpty(
        string? value)
    {
        Assert.Equal(NormalizedKeyStatus.Empty, PartyKeyNormalizer.Cvr(value).Status);
        Assert.Equal(NormalizedKeyStatus.Empty, PartyKeyNormalizer.EmailDomain(value).Status);
        Assert.Equal(NormalizedKeyStatus.Empty, PartyKeyNormalizer.Name(value).Status);
    }

    [Theory]
    [InlineData("Ops@Example.TEST", "example.test")]
    [InlineData("it@www.example.test", "example.test")]
    [InlineData("www.example.test", "example.test")]
    [InlineData("example.test.", "example.test")]
    public void EmailDomain_Normalizes(
        string value,
        string expected)
    {
        var key =
            PartyKeyNormalizer.EmailDomain(value);

        Assert.Equal(NormalizedKeyStatus.Ok, key.Status);
        Assert.Equal(expected, key.Value);
    }

    [Theory]
    [InlineData("someone@gmail.com")]
    [InlineData("someone@Hotmail.com")]
    [InlineData("someone@outlook.com")]
    [InlineData("someone@live.dk")]
    public void EmailDomain_PublicProviders_AreExcluded(
        string value)
    {
        Assert.Equal(
            NormalizedKeyStatus.Excluded,
            PartyKeyNormalizer.EmailDomain(value).Status
        );
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("someone@")]
    [InlineData("localhost")]
    public void EmailDomain_OtherShapes_AreMalformed(
        string value)
    {
        Assert.Equal(
            NormalizedKeyStatus.Malformed,
            PartyKeyNormalizer.EmailDomain(value).Status
        );
    }

    [Theory]
    [InlineData("Example Trading ApS", "example trading")]
    [InlineData("  EXAMPLE   Trading   A/S ", "example trading")]
    [InlineData("Example Trading, I/S", "example trading")]
    [InlineData("Example IVS", "example")]
    [InlineData("Example P/S", "example")]
    [InlineData("Example K/S", "example")]
    [InlineData("Apsley Trading", "apsley trading")]
    public void Name_CasefoldsCollapsesAndStripsLegalSuffix(
        string value,
        string expected)
    {
        var key =
            PartyKeyNormalizer.Name(value);

        Assert.Equal(NormalizedKeyStatus.Ok, key.Status);
        Assert.Equal(expected, key.Value);
    }

    [Fact]
    public void Name_OnlyLegalSuffix_IsMalformed()
    {
        Assert.Equal(
            NormalizedKeyStatus.Malformed,
            PartyKeyNormalizer.Name("ApS").Status
        );
    }
}
