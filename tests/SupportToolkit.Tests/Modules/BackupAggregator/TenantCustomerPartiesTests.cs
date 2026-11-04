using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Core.Tenants;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests;

public class TenantCustomerPartiesTests
{
    [Fact]
    public void CustomerTenant_BecomesNameOnlyParty()
    {
        var party =
            Assert.Single(
                TenantCustomerParties.FromCustomerTenants(
                    [Tenant("t1", "Example Trading", "customer")]
                )
            );

        Assert.Equal(new CustomerParty("t1", "Example Trading"), party);
        Assert.Null(party.Cvr);
        Assert.Null(party.Email);
    }

    [Fact]
    public void NonCustomerAndDeletedTenants_AreExcluded()
    {
        var parties =
            TenantCustomerParties.FromCustomerTenants(
                [
                    Tenant("t1", "Partner", "partner"),
                    Tenant("t2", "Folder", "folder"),
                    Tenant("t3", "Gone", "customer", DateTimeOffset.UnixEpoch),
                    Tenant("t4", "Kept", "Customer")
                ]
            );

        Assert.Equal("t4", Assert.Single(parties).Id);
    }

    [Fact]
    public void Profile_OfTenantParties_OnlyNameIsUsable()
    {
        var acronis =
            TenantCustomerParties.FromCustomerTenants(
                [Tenant("t1", "Example Trading", "customer")]
            );

        var profiles =
            CandidateKeyProfiler.Profile(
                acronis,
                [new CustomerParty("BC-1", "Example Trading ApS", "10000001", "it@example.test")]
            );

        Assert.All(
            profiles.Where(profile => profile.Key != CustomerMatchKey.Name),
            profile =>
            {
                Assert.Equal(0, profile.Acronis.Ok);
                Assert.Equal(0, profile.OneToOneValues);
            }
        );

        Assert.Equal(
            1,
            profiles.Single(profile => profile.Key == CustomerMatchKey.Name)
                .OneToOneValues
        );
    }

    private static TenantRecord Tenant(
        string id,
        string name,
        string kind,
        DateTimeOffset? deletedAt = null)
    {
        return new TenantDto
        {
            Id = id,
            Name = name,
            Kind = kind,
            Enabled = true,
            DeletedAt = deletedAt
        }.ToTenantRecord();
    }
}
