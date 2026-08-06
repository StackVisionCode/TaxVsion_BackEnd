namespace TaxVision.Notes.Application.Backfill.Abstractions;

/// <summary>
/// Backfill de <c>CustomerDirectoryEntry</c> (Fase 4B) para un tenant recién descubierto — la
/// única forma en que Notes "descubre" un tenant es verlo llegar en un evento de Customer (no hay
/// endpoint de enumeración de tenants M2M en el monorepo). Nunca lanza: una falla de red/HTTP se
/// loguea y el tenant queda pendiente para el próximo evento.
/// </summary>
public interface ITenantCustomerBackfillService
{
    Task EnsureBackfilledAsync(Guid tenantId, CancellationToken ct = default);
}
