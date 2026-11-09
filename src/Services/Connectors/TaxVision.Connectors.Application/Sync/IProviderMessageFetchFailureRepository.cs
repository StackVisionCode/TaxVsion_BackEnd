using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Application.Sync;

public interface IProviderMessageFetchFailureRepository
{
    /// <summary>Los fallos que todavía frenan el cursor de la cuenta (casi siempre ninguno).</summary>
    Task<IReadOnlyList<ProviderMessageFetchFailure>> ListPendingAsync(Guid accountId, CancellationToken ct = default);

    Task<ProviderMessageFetchFailure?> GetAsync(
        Guid accountId,
        string providerMessageId,
        CancellationToken ct = default
    );

    Task AddAsync(ProviderMessageFetchFailure failure, CancellationToken ct = default);
}
