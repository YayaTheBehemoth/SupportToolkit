using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;

using static SupportToolkit.Tests.CustomerMappingTestSupport;

namespace SupportToolkit.Tests;

public class LooseNameMatcherTests
{
    [Theory]
    [InlineData("Northwind Traders", "NORTHWIND TRADERS")]
    [InlineData("Northwind Traders ApS", "northwind traders")]
    [InlineData("Northwind Traders A/S", "Northwind  Traders")]
    [InlineData("Contoso Værksted", "Contoso Vaerksted")]
    [InlineData("Åbent Hus", "Aabent Hus")]
    [InlineData("North-wind, Traders.", "Northwind Traders")]
    [InlineData("Fgt Tables", "FGTTables")]
    public void CaseAccentsPunctuationAndLegalForms_AreIgnored(
        string left,
        string right)
    {
        Assert.Equal(
            LooseNameTier.Exact,
            LooseNameMatcher.Compare(left, right));
    }

    [Theory]
    [InlineData("FGT Tables", "Holding company (FGT Tables)")]
    [InlineData("Holding company (FGT Tables)", "FGT Tables")]
    public void GenericWrapperWords_AreIgnoredSoTheCoreNameIsExact(
        string left,
        string right)
    {
        Assert.Equal(
            LooseNameTier.Exact,
            LooseNameMatcher.Compare(left, right));
    }

    [Theory]
    [InlineData("FGT Tables", "Nordic FGT Tables")]
    [InlineData("Nordic Group (FGT Tables)", "FGT Tables")]
    [InlineData("Fabrikam", "Fabrikam Logistics")]
    [InlineData("Contoso", "Contoso Group Danmark A/S - Aarhus")]
    public void ShorterNameInsideTheLonger_IsContainment(
        string left,
        string right)
    {
        Assert.Equal(
            LooseNameTier.Containment,
            LooseNameMatcher.Compare(left, right));
    }

    [Fact]
    public void SeveralSharedWordsWithoutContainment_IsOverlap()
    {
        Assert.Equal(
            LooseNameTier.Overlap,
            LooseNameMatcher.Compare(
                "Alpine Consulting Services Nord",
                "Alpine Consulting Services Syd"));
    }

    [Theory]
    [InlineData("Northwind Traders", "Contoso Supplies")]
    [InlineData("Northwind", "Northern Lights")]
    [InlineData("Holding ApS", "Holding Group")]
    [InlineData("A/S", "ApS")]
    [InlineData("", "Northwind")]
    [InlineData(null, null)]
    [InlineData("   ", "   ")]
    public void UnrelatedOrEmptyNames_DoNotMatch(
        string? left,
        string? right)
    {
        Assert.Equal(
            LooseNameTier.None,
            LooseNameMatcher.Compare(left, right));
    }

    [Fact]
    public void NameOfOnlyGenericWords_HasNoTokens()
    {
        Assert.Empty(LooseNameMatcher.Tokens("Holding Group Danmark ApS"));
    }

    [Fact]
    public void ShortFragments_AreNotDistinctive()
    {
        // Two-letter fragments are dropped, so "Ab Cd" has no tokens.
        Assert.Empty(LooseNameMatcher.Tokens("Ab Cd"));
    }
}
