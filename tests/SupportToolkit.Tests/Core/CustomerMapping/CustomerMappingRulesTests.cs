using SupportToolkit.Core.CustomerMapping;

using static SupportToolkit.Tests.CustomerMappingTestSupport;

namespace SupportToolkit.Tests;

public class CustomerMappingRulesTests
{
    [Theory]
    [InlineData(CustomerMappingState.Suggested, CustomerMappingState.Confirmed)]
    [InlineData(CustomerMappingState.Suggested, CustomerMappingState.Rejected)]
    [InlineData(CustomerMappingState.Confirmed, CustomerMappingState.Retired)]
    public void AllowedTransitions_AreAllowed(
        CustomerMappingState from,
        CustomerMappingState to)
    {
        Assert.True(CustomerMappingRules.IsAllowed(from, to));
    }

    [Theory]
    [InlineData(CustomerMappingState.Rejected, CustomerMappingState.Suggested)]
    [InlineData(CustomerMappingState.Rejected, CustomerMappingState.Confirmed)]
    [InlineData(CustomerMappingState.Retired, CustomerMappingState.Confirmed)]
    [InlineData(CustomerMappingState.Confirmed, CustomerMappingState.Rejected)]
    [InlineData(CustomerMappingState.Suggested, CustomerMappingState.Retired)]
    [InlineData(CustomerMappingState.Confirmed, CustomerMappingState.Confirmed)]
    public void OtherTransitions_Throw(
        CustomerMappingState from,
        CustomerMappingState to)
    {
        Assert.Throws<InvalidOperationException>(
            () => CustomerMappingRules.EnsureAllowed(from, to)
        );
    }

    [Fact]
    public void Repository_HasNoDeleteOperation()
    {
        Assert.DoesNotContain(
            typeof(ICustomerMappingRepository).GetMethods(),
            method => method.Name.Contains(
                "Delete",
                StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains(
                    "Remove",
                    StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task InMemoryRepository_RejectsDuplicateAddAndUnknownUpdate()
    {
        var mapping =
            Mapping("tenant-1", "BC-1", CustomerMappingState.Suggested);

        var repository =
            await RepositoryWith(mapping);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AddAsync(mapping)
        );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.UpdateAsync(
                Mapping("tenant-2", "BC-2", CustomerMappingState.Suggested))
        );
    }
}
