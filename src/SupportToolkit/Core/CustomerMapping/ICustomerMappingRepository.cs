namespace SupportToolkit.Core.CustomerMapping;

/// <summary>
/// Storage for owned customer mappings.
///
/// There is deliberately no delete operation: the mapping table is an
/// audit trail. The production storage backend is not yet decided.
/// </summary>
public interface ICustomerMappingRepository
{
    Task<IReadOnlyList<CustomerMappingRecord>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<CustomerMappingRecord?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new mapping. Fails if the id already exists.
    /// </summary>
    Task AddAsync(
        CustomerMappingRecord mapping,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an existing mapping. Fails if the id does not exist.
    /// </summary>
    Task UpdateAsync(
        CustomerMappingRecord mapping,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Process-local storage for tests and fixture execution.
/// Nothing is persisted.
/// </summary>
public sealed class InMemoryCustomerMappingRepository
    : ICustomerMappingRepository
{
    private readonly object _gate = new();

    private readonly List<CustomerMappingRecord> _mappings = [];

    public Task<IReadOnlyList<CustomerMappingRecord>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<CustomerMappingRecord>>(
                _mappings.ToArray()
            );
        }
    }

    public Task<CustomerMappingRecord?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _mappings.FirstOrDefault(
                    mapping => mapping.Id == id
                )
            );
        }
    }

    public Task AddAsync(
        CustomerMappingRecord mapping,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        lock (_gate)
        {
            if (_mappings.Any(
                existing => existing.Id == mapping.Id))
            {
                throw new InvalidOperationException(
                    $"Customer mapping {mapping.Id} already exists."
                );
            }

            _mappings.Add(mapping);
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        CustomerMappingRecord mapping,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        lock (_gate)
        {
            var index =
                _mappings.FindIndex(
                    existing => existing.Id == mapping.Id
                );

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"Customer mapping {mapping.Id} does not exist."
                );
            }

            _mappings[index] = mapping;
        }

        return Task.CompletedTask;
    }
}
