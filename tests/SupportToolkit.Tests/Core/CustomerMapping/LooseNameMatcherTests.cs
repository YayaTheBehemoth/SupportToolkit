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

    [Theory]
    [InlineData("O'Brien Bygger", "OBrien Bygger")]
    [InlineData("Fru’ens Have", "Fruens Have")]
    [InlineData("Hans'Byg", "Hans Byg")]
    [InlineData("Ta bles Nordic", "Tables Nordic")]
    [InlineData("Nor dic Foods", "Nordic Foods")]
    [InlineData("Nord​ic Foods", "Nordic Foods")]
    [InlineData("Nordic Foods", "Nordic Foods")]
    public void StrayApostrophesSpacesAndInvisibleCharacters_AreIgnored(
        string left,
        string right)
    {
        Assert.Equal(
            LooseNameTier.Exact,
            LooseNameMatcher.Compare(left, right));
    }

    [Theory]
    [InlineData("Northwind Traders", "Northwnd Traders")]
    [InlineData("Alpine Consulting Services", "Alpine Consultng Services")]
    [InlineData("Alpine Consulting Services", "Alpine Consultng Servces")]
    public void OneTypoInALongName_IsNearExact(
        string left,
        string right)
    {
        Assert.Equal(
            LooseNameTier.NearExact,
            LooseNameMatcher.Compare(left, right));
    }

    [Theory]
    [InlineData("Contoso", "Contosa")]
    [InlineData("Nordic Foods", "Nordic Fools Ab")]
    [InlineData("Hansen Byg 1", "Hansen Byg 2")]
    [InlineData("Northwind Traders", "Northwind Traitors Ltd Co")]
    public void ShortNamesNumbersAndBiggerDifferences_AreNotTreatedAsTypos(
        string left,
        string right)
    {
        Assert.NotEqual(
            LooseNameTier.NearExact,
            LooseNameMatcher.Compare(left, right));

        Assert.NotEqual(
            LooseNameTier.Exact,
            LooseNameMatcher.Compare(left, right));
    }

    [Theory]
    [InlineData("Nor dic Foods", "Nordic Foods Service")]
    [InlineData("Nordic Foods", "Nor dic Foods Service")]
    [InlineData("Nor dic Foods", "Danish Nordic Foods Service")]
    [InlineData("Nor dic Foods", "Nordic Foods Service ApS")]
    public void StraySpaceInsideALongerName_IsContainment(
        string left,
        string right)
    {
        Assert.Equal(
            LooseNameTier.Containment,
            LooseNameMatcher.Compare(left, right));

        Assert.Equal(
            LooseNameTier.Containment,
            LooseNameMatcher.Compare(right, left));
    }

    [Theory]
    [InlineData("Ford Motors", "Stanford Motors Co")]
    [InlineData("Ford Mot", "Stanford Motors Co")]
    [InlineData("Nord ic", "Nordic Foods Service")]
    [InlineData("Nordicfood Service", "Nordic Foods")]
    public void ARunMustStartAndEndOnWholeWords_AndBeLongEnough(
        string left,
        string right)
    {
        Assert.NotEqual(
            LooseNameTier.Containment,
            LooseNameMatcher.Compare(left, right));
    }

    [Theory]
    [InlineData("Åbent Hus - Nordic", "Åbent Hus - Nordic")]
    [InlineData("Frå Byg & Co", "Frå Byg & Co")]
    [InlineData("Søren Åse - Træ", "Søren Åse - Træ")]
    public void ALetterStoredAsBasePlusAccent_MatchesTheSingleCharacterForm(
        string composed,
        string decomposed)
    {
        Assert.NotEqual(composed, decomposed);

        Assert.Equal(
            LooseNameTier.Exact,
            LooseNameMatcher.Compare(composed, decomposed));

        Assert.Equal(
            LooseNameMatcher.Compact(composed),
            LooseNameMatcher.Compact(decomposed));
    }

    [Fact]
    public void ExactNameKey_TreatsComposedAndDecomposedLettersAsEqual()
    {
        Assert.Equal(
            PartyKeyNormalizer.Name("Åbent Hus"),
            PartyKeyNormalizer.Name("Åbent Hus"));
    }

    [Fact]
    public void Forensics_FlagsADecomposedLetter()
    {
        var inspected = NameForensics.Inspect("Åbent Hus");

        Assert.Contains(
            inspected.Oddities,
            odd => odd.Contains("U+030A")
                && odd.Contains("decomposed"));

        Assert.Equal(
            NameForensics.Inspect("Åbent Hus").Compact,
            inspected.Compact);

        Assert.Empty(NameForensics.Inspect("Åbent Hus").Oddities);
    }

    [Theory]
    [InlineData(
        "Strand- og Havnemålingsteknikerne",
        "Strand- og Havnemålingsteknikernes Forening")]
    [InlineData("Hansens Maskinfabrik", "Hansen Maskinfabrik ApS")]
    [InlineData("Foreningen Nordlys", "Foreninger Nordlys Danmark")]
    public void WordsDifferingOnlyByAShortEnding_AreTheSameWord(
        string left,
        string right)
    {
        // Containment, or a stronger tier when the whole names are
        // also within a typo of each other.
        Assert.True(
            LooseNameMatcher.Compare(left, right)
                >= LooseNameTier.Containment);

        Assert.True(
            LooseNameMatcher.Compare(right, left)
                >= LooseNameTier.Containment);
    }

    [Fact]
    public void TheHyphenatedPossessiveCase_IsContainmentSoItIsAcceptedWhenUnique()
    {
        Assert.Equal(
            LooseNameTier.Containment,
            LooseNameMatcher.Compare(
                "Strand- og Havnemålingsteknikerne",
                "Strand- og Havnemålingsteknikernes Forening"));
    }

    [Theory]
    [InlineData("Nord Byg", "Norde Byg Syd")]
    [InlineData("Bygge Hus", "Byggeri Hus Nord")]
    [InlineData("Hansen Maskin", "Hansenberg Maskin Co")]
    public void ShortStemsAndLongerEndings_AreNotTheSameWord(
        string left,
        string right)
    {
        Assert.NotEqual(
            LooseNameTier.Containment,
            LooseNameMatcher.Compare(left, right));
    }

    [Fact]
    public void DifferentNumbers_NeverMatchTheSameCustomer()
    {
        Assert.Equal(
            LooseNameTier.None,
            LooseNameMatcher.Compare("Hansen Byg 1", "Hansen Byg 2"));

        Assert.Equal(
            LooseNameTier.Containment,
            LooseNameMatcher.Compare("Hansen Byg", "Hansen Byg 2"));
    }

    [Fact]
    public void Forensics_ExposesWhatTheMatcherSeesAndOddCharacters()
    {
        var inspected =
            NameForensics.Inspect(" Nordic Fo'ods  ApS​");

        Assert.Equal("nordicfoods", inspected.Compact);
        Assert.Contains("leading space", inspected.Oddities);
        Assert.Contains(
            inspected.Oddities,
            odd => odd.Contains("U+00A0") && odd.Contains("no-break space"));
        Assert.Contains(
            inspected.Oddities,
            odd => odd.Contains("apostrophe"));
        Assert.Contains(
            inspected.Oddities,
            odd => odd.StartsWith("double space"));
        Assert.Contains(
            inspected.Oddities,
            odd => odd.Contains("zero-width space"));
    }

    [Fact]
    public void Forensics_OfACleanName_ListsNothingOdd()
    {
        var inspected = NameForensics.Inspect("Northwind Traders");

        Assert.Empty(inspected.Oddities);
        Assert.Equal("northwindtraders", inspected.Compact);
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
