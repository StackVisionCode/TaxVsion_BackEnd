using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Tests.Projections;

/// <summary>No-op double: los 6 consumers de eventos de Customer la llaman como primera línea
/// (ver TenantCustomerBackfillService) para descubrir tenants nuevos — irrelevante para las
/// aserciones de proyección de cada consumer, pero registra las llamadas para los tests que sí
/// quieren verificar que se disparó.</summary>
internal sealed class FakeTenantCustomerBackfillService : ITenantCustomerBackfillService
{
    private readonly List<Guid> _calls = [];

    public IReadOnlyList<Guid> Calls => _calls;

    public Task EnsureBackfilledAsync(Guid tenantId, CancellationToken ct = default)
    {
        _calls.Add(tenantId);
        return Task.CompletedTask;
    }
}
