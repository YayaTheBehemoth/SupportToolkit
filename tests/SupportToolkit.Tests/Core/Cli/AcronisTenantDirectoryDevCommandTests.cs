
using SupportToolkit.Cli.Commands;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests.Cli;

public class AcronisTenantDirectoryDevCommandTests
{
    [Fact]
    public void SummaryOnly_DoesNotRevealCustomerNames()
    {
        var tenants =
            Enumerable.Range(1, 120)
                .Select(
                    index => CreateTenant(
                        index,
                        $"Private Customer {index}",
                        "customer"))
                .ToList();

        tenants.Add(
            CreateTenant(
                121,
                "Private Partner",
                "partner"));

        using var output = new StringWriter();

        var exitCode =
            AcronisTenantDirectoryDevCommand.WriteReport(
                output,
                tenants,
                showNames: false);

        var text = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("Total tenant records:  121", text);
        Assert.Contains("Customer tenants:      120", text);
        Assert.Contains("Other tenant kinds:    1", text);

        Assert.DoesNotContain(
            "Private Customer",
            text);

        Assert.DoesNotContain(
            "Private Partner",
            text);
    }

    [Fact]
    public void ExplicitNameReview_SortsAndPreservesDuplicateNames()
    {
        var tenants = new List<TenantDto>
        {
            CreateTenant(1, "Zulu Customer", "customer"),
            CreateTenant(2, "Alpha Customer", "customer"),
            CreateTenant(3, "Alpha Customer", "customer"),
            CreateTenant(4, "Hidden Partner", "partner")
        };

        using var output = new StringWriter();

        var exitCode =
            AcronisTenantDirectoryDevCommand.WriteReport(
                output,
                tenants,
                showNames: true);

        var text = output.ToString();

        Assert.Equal(0, exitCode);

        Assert.Contains(
            "Duplicate customer names: 1",
            text);

        Assert.DoesNotContain(
            "Hidden Partner",
            text);

        Assert.Equal(
            2,
            text.Split(
                "\"Alpha Customer\"",
                StringSplitOptions.None).Length - 1);

        Assert.True(
            text.IndexOf(
                "\"Alpha Customer\"",
                StringComparison.Ordinal)
            < text.IndexOf(
                "\"Zulu Customer\"",
                StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidIdentity_ReturnsIncompleteExitCode()
    {
        var tenants = new List<TenantDto>
        {
            new()
            {
                Id = "invalid-uuid",
                Name = "Synthetic Customer",
                Kind = "customer"
            }
        };

        using var output = new StringWriter();

        var exitCode =
            AcronisTenantDirectoryDevCommand.WriteReport(
                output,
                tenants,
                showNames: false);

        Assert.Equal(2, exitCode);

        Assert.Contains(
            "Invalid UUIDs:         1",
            output.ToString());

        Assert.DoesNotContain(
            "Synthetic Customer",
            output.ToString());
    }

    private static TenantDto CreateTenant(
        int index,
        string name,
        string kind)
    {
        return new TenantDto
        {
            Id =
                $"00000000-0000-0000-0000-{index:D12}",
            Name = name,
            Kind = kind
        };
    }
}
