using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Analytics;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio del snapshot analytics. <see cref="GetOrCreateForDayAsync"/> es el punto
/// único de acceso desde los consumers: si no existe la fila para el par (día, categoría)
/// se crea y se traquea; de lo contrario se devuelve la existente para acumular.
/// </summary>
public sealed class SignatureAnalyticsRepository(SignatureDbContext db) : ISignatureAnalyticsRepository
{
    public async Task<SignatureAnalyticsSnapshot> GetOrCreateForDayAsync(
        Guid tenantId,
        DateOnly day,
        SignatureCategory category,
        CancellationToken ct = default
    )
    {
        var existing = await db.SignatureAnalyticsSnapshots.FirstOrDefaultAsync(
            s => s.TenantId == tenantId && s.Day == day && s.Category == category,
            ct
        );
        if (existing is not null)
            return existing;

        var created = SignatureAnalyticsSnapshot.CreateEmpty(tenantId, day, category);
        await db.SignatureAnalyticsSnapshots.AddAsync(created, ct);
        return created;
    }
}
