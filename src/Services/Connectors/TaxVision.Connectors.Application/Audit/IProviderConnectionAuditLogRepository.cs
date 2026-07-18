using TaxVision.Connectors.Domain.Audit;

namespace TaxVision.Connectors.Application.Audit;

public interface IProviderConnectionAuditLogRepository
{
    Task AddAsync(ProviderConnectionAuditLog entry, CancellationToken ct = default);

    /// <summary>Borra en bloque las entradas con <c>Timestamp</c> anterior a <paramref name="cutoffUtc"/> — Fase 11 (retention). Devuelve la cantidad borrada.</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default);
}
