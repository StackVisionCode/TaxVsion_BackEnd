namespace TaxVision.Customer.Application.Abstractions;

/// <summary>Busca imports en curso sin avance desde <c>idleSinceUtc</c>, de todas las oficinas.</summary>
public interface ICustomerImportRecoveryStore
{
    Task<IReadOnlyList<Guid>> ListStuckIdsAsync(DateTime idleSinceUtc, int max, CancellationToken ct);
}
