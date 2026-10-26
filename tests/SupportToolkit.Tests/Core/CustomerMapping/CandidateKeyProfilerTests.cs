using SupportToolkit.Core.CustomerMapping.Matching;

namespace SupportToolkit.Tests;

public class CandidateKeyProfilerTests
{
    [Fact]
    public void Profile_CountsCardinalityAndQuality()
    {
        IReadOnlyList<CustomerParty> acronis =
        [
            new("a1", Cvr: "10000001"),          // 1:1
            new("a2", Cvr: "10000002"),          // 1:many
            new("a3", Cvr: "10000003"),          // many:1
            new("a4", Cvr: "DK10000003"),        // many:1
            new("a5", Cvr: "10000009"),          // no match
            new("a6", Cvr: null),                // empty
            new("a7", Cvr: "123")                // malformed
        ];

        IReadOnlyList<CustomerParty> businessCentral =
        [
            new("b1", Cvr: "10000001"),
            new("b2", Cvr: "10000002"),
            new("b3", Cvr: "10000002"),
            new("b4", Cvr: "10000003"),
            new("b5", Cvr: "10000008"),
            new("b6", Cvr: "10000007")
        ];

        var profile =
            CandidateKeyProfiler.Profile(
                CustomerMatchKey.Cvr,
                acronis,
                businessCentral
            );

        Assert.Equal(1, profile.OneToOneValues);
        Assert.Equal(1, profile.OneToManyValues);
        Assert.Equal(1, profile.ManyToOneValues);
        Assert.Equal(0, profile.ManyToManyValues);
        Assert.Equal(1, profile.AcronisRecordsWithoutMatch);
        Assert.Equal(2, profile.BusinessCentralRecordsWithoutMatch);

        Assert.Equal(
            new KeySideProfile(7, 5, 1, 1, 0),
            profile.Acronis
        );

        Assert.Equal(
            new KeySideProfile(6, 6, 0, 0, 0),
            profile.BusinessCentral
        );
    }

    [Fact]
    public void Profile_ManyToMany_IsCounted()
    {
        IReadOnlyList<CustomerParty> acronis =
        [
            new("a1", Name: "Example ApS"),
            new("a2", Name: "example")
        ];

        IReadOnlyList<CustomerParty> businessCentral =
        [
            new("b1", Name: "Example A/S"),
            new("b2", Name: "EXAMPLE")
        ];

        var profile =
            CandidateKeyProfiler.Profile(
                CustomerMatchKey.Name,
                acronis,
                businessCentral
            );

        Assert.Equal(1, profile.ManyToManyValues);
        Assert.Equal(0, profile.OneToOneValues);
    }

    [Fact]
    public void Profile_CoversEveryKey_AndCarriesNoStringValues()
    {
        var profiles =
            CandidateKeyProfiler.Profile([], []);

        Assert.Equal(
            Enum.GetValues<CustomerMatchKey>(),
            profiles.Select(profile => profile.Key)
        );

        Assert.DoesNotContain(
            typeof(CandidateKeyProfile)
                .GetProperties()
                .Concat(typeof(KeySideProfile).GetProperties()),
            property => property.PropertyType == typeof(string)
        );
    }
}
