using Microsoft.EntityFrameworkCore;
using TaxVision.Wallet.Application.Reservations.Abstractions;
using TaxVision.Wallet.Domain.Reservations;

namespace TaxVision.Wallet.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de <see cref="WalletReservation"/>. Lecturas con <c>IgnoreQueryFilters()</c> + filtro
/// explícito por <c>tenantId</c> (mismo criterio que <see cref="WalletRepository"/>): el tenant viene
/// validado desde Application y en un handler de Wolverine el TenantContext puede no estar poblado.
/// </summary>
public sealed class ReservationRepository(WalletDbContext db) : IReservationRepository
{
    public Task<WalletReservation?> GetByReferenceAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        CancellationToken ct = default
    ) =>
        db
            .Reservations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.ReferenceType == referenceType && r.ReferenceId == referenceId,
                ct
            );

    public async Task AddAsync(WalletReservation reservation, CancellationToken ct = default) =>
        await db.Reservations.AddAsync(reservation, ct);
}
