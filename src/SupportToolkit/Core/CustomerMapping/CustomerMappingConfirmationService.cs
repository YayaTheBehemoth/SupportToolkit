namespace SupportToolkit.Core.CustomerMapping;

/// <summary>
/// Human decisions on customer mappings.
///
/// Every decision records who made it and when. A tenant or customer
/// can be in at most one Confirmed mapping; an existing link must be
/// retired before a different one is confirmed.
/// </summary>
public sealed class CustomerMappingConfirmationService
{
    private readonly ICustomerMappingRepository _repository;

    private readonly TimeProvider _timeProvider;

    public CustomerMappingConfirmationService(
        ICustomerMappingRepository repository,
        TimeProvider? timeProvider = null)
    {
        _repository =
            repository
            ?? throw new ArgumentNullException(nameof(repository));

        _timeProvider =
            timeProvider
            ?? TimeProvider.System;
    }

    public async Task<CustomerMappingRecord> ConfirmAsync(
        Guid id,
        string actor,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actor);

        var mapping =
            await RequireAsync(id, cancellationToken);

        CustomerMappingRules.EnsureAllowed(
            mapping.State,
            CustomerMappingState.Confirmed
        );

        await EnsureNoConfirmedConflictAsync(
            mapping.AcronisTenantId,
            mapping.BcCustomerNo,
            cancellationToken
        );

        var confirmed =
            mapping with
            {
                State = CustomerMappingState.Confirmed,
                ConfirmedAt = _timeProvider.GetUtcNow(),
                ConfirmedBy = actor.Trim(),
                Notes = notes ?? mapping.Notes
            };

        await _repository.UpdateAsync(
            confirmed,
            cancellationToken
        );

        return confirmed;
    }

    public async Task<CustomerMappingRecord> RejectAsync(
        Guid id,
        string actor,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actor);

        var mapping =
            await RequireAsync(id, cancellationToken);

        CustomerMappingRules.EnsureAllowed(
            mapping.State,
            CustomerMappingState.Rejected
        );

        var rejected =
            mapping with
            {
                State = CustomerMappingState.Rejected,
                RejectedAt = _timeProvider.GetUtcNow(),
                RejectedBy = actor.Trim(),
                Notes = notes ?? mapping.Notes
            };

        await _repository.UpdateAsync(
            rejected,
            cancellationToken
        );

        return rejected;
    }

    public async Task<CustomerMappingRecord> RetireAsync(
        Guid id,
        string actor,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actor);

        var mapping =
            await RequireAsync(id, cancellationToken);

        CustomerMappingRules.EnsureAllowed(
            mapping.State,
            CustomerMappingState.Retired
        );

        var retired =
            mapping with
            {
                State = CustomerMappingState.Retired,
                RetiredAt = _timeProvider.GetUtcNow(),
                RetiredBy = actor.Trim(),
                Notes = notes ?? mapping.Notes
            };

        await _repository.UpdateAsync(
            retired,
            cancellationToken
        );

        return retired;
    }

    /// <summary>
    /// Records a link decided by a person, confirmed immediately.
    ///
    /// Refused when the same pair is awaiting a decision as a
    /// suggestion; that suggestion should be confirmed instead.
    /// </summary>
    public async Task<CustomerMappingRecord> AddManualAsync(
        string acronisTenantId,
        string bcCustomerNo,
        string actor,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actor);

        if (string.IsNullOrWhiteSpace(acronisTenantId)
            || string.IsNullOrWhiteSpace(bcCustomerNo))
        {
            throw new ArgumentException(
                "A manual mapping needs both an Acronis tenant id " +
                "and a Business Central customer number."
            );
        }

        var all =
            await _repository.GetAllAsync(cancellationToken);

        if (all.Any(
            mapping =>
                mapping.State == CustomerMappingState.Suggested
                && CustomerMappingRules.SameId(
                    mapping.AcronisTenantId,
                    acronisTenantId)
                && CustomerMappingRules.SameId(
                    mapping.BcCustomerNo,
                    bcCustomerNo)))
        {
            throw new InvalidOperationException(
                "This pair is already suggested; confirm the " +
                "existing suggestion instead."
            );
        }

        await EnsureNoConfirmedConflictAsync(
            acronisTenantId,
            bcCustomerNo,
            cancellationToken
        );

        var now =
            _timeProvider.GetUtcNow();

        var mapping =
            new CustomerMappingRecord(
                Guid.NewGuid(),
                acronisTenantId.Trim(),
                bcCustomerNo.Trim(),
                CustomerMappingState.Confirmed,
                CustomerMatchMethod.Manual,
                ConfirmedAt: now,
                ConfirmedBy: actor.Trim(),
                Notes: notes
            );

        await _repository.AddAsync(
            mapping,
            cancellationToken
        );

        return mapping;
    }

    private async Task<CustomerMappingRecord> RequireAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await _repository.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Customer mapping {id} does not exist."
            );
    }

    private async Task EnsureNoConfirmedConflictAsync(
        string acronisTenantId,
        string bcCustomerNo,
        CancellationToken cancellationToken)
    {
        var all =
            await _repository.GetAllAsync(cancellationToken);

        var conflict =
            all.FirstOrDefault(
                mapping =>
                    mapping.State == CustomerMappingState.Confirmed
                    && (CustomerMappingRules.SameId(
                            mapping.AcronisTenantId,
                            acronisTenantId)
                        || CustomerMappingRules.SameId(
                            mapping.BcCustomerNo,
                            bcCustomerNo))
            );

        if (conflict is not null)
        {
            throw new InvalidOperationException(
                $"Customer mapping {conflict.Id} is already confirmed " +
                "for this tenant or customer; retire it first."
            );
        }
    }

    private static void RequireActor(
        string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException(
                "A mapping decision must record who made it.",
                nameof(actor)
            );
        }
    }
}
