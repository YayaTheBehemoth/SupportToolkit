using SupportToolkit.Core.Tenants;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests;

/// <summary>
/// Acronis's own conventions are applied once, in the mapping: its kind
/// names become roles, and only a UUID is a canonical tenant ID.
/// </summary>
public class TenantRecordMappingTests
{
    [Theory]
    [InlineData("customer", TenantRole.Customer)]
    [InlineData("CUSTOMER", TenantRole.Customer)]
    [InlineData("partner", TenantRole.Partner)]
    [InlineData("Partner", TenantRole.Partner)]
    [InlineData("unit", TenantRole.Other)]
    [InlineData("folder", TenantRole.Other)]
    [InlineData(null, TenantRole.Other)]
    public void Kind_BecomesARole(string? kind, TenantRole expected)
    {
        var record =
            new TenantDto { Id = "11111111-1111-4111-8111-111111111111", Name = "Example", Kind = kind }
                .ToTenantRecord();

        Assert.Equal(expected, record.Role);
        Assert.Equal(kind, record.Kind);
    }

    [Theory]
    [InlineData("11111111-1111-4111-8111-111111111111", "11111111-1111-4111-8111-111111111111")]
    [InlineData("AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")]
    [InlineData("not-a-uuid", null)]
    [InlineData("", null)]
    public void OnlyAUuid_IsACanonicalId(string id, string? expected)
    {
        var record =
            new TenantDto { Id = id, Name = "Example", Kind = "customer" }
                .ToTenantRecord();

        Assert.Equal(expected, record.CanonicalId);
        Assert.Equal(id, record.Id);
    }
}
