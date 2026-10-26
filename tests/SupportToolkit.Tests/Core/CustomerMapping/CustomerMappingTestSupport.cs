using SupportToolkit.Core.CustomerMapping;

namespace SupportToolkit.Tests;

internal static class CustomerMappingTestSupport
{
    public static readonly DateTimeOffset Now =
        new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    public static CustomerMappingRecord Mapping(
        string tenant,
        string customer,
        CustomerMappingState state,
        CustomerMatchMethod method = CustomerMatchMethod.Manual)
    {
        return new CustomerMappingRecord(
            Guid.NewGuid(),
            tenant,
            customer,
            state,
            method
        );
    }

    public static async Task<InMemoryCustomerMappingRepository> RepositoryWith(
        params CustomerMappingRecord[] mappings)
    {
        var repository =
            new InMemoryCustomerMappingRepository();

        foreach (var mapping in mappings)
        {
            await repository.AddAsync(mapping);
        }

        return repository;
    }

    internal sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
