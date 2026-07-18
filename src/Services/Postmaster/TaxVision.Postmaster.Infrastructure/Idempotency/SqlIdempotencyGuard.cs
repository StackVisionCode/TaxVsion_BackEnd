using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Domain.Idempotency;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Infrastructure.Idempotency;

/// <summary>
/// <see cref="IIdempotencyGuard"/> respaldado por la tabla <c>EmailIdempotency</c>. Persiste la
/// reserva de inmediato (no espera al SaveChanges del handler completo) para que un evento
/// duplicado que llega a otra instancia del servicio, en paralelo, la vea. Una carrera real entre
/// dos inserts simultáneos se resuelve por la PK compuesta — el perdedor recibe
/// <see cref="ConflictException"/> y se trata igual que "reserva ya tomada".
/// </summary>
public sealed class SqlIdempotencyGuard(PostmasterDbContext dbContext, ILogger<SqlIdempotencyGuard> logger)
    : IIdempotencyGuard
{
    private static readonly TimeSpan ReservationRetryWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReservationTimeToLive = TimeSpan.FromDays(7);

    public async Task<IdempotencyReservationResult> TryReserveAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        var existing = await FindAsync(tenantId, idempotencyKey, ct);
        if (existing is null)
            return await InsertReservationAsync(tenantId, idempotencyKey, ct);

        if (existing.CompletedAtUtc is not null)
            return IdempotencyReservationResult.AlreadyCompleted(existing.SentMessageId!.Value);

        var isWithinRetryWindow = DateTime.UtcNow - existing.CreatedAtUtc < ReservationRetryWindow;
        if (isWithinRetryWindow)
            return IdempotencyReservationResult.InProgress();

        await RemoveAbandonedReservationAsync(existing, ct);
        return await InsertReservationAsync(tenantId, idempotencyKey, ct);
    }

    /// <summary>
    /// No hace SaveChangesAsync propio a propósito — se completa dentro de la misma transacción del
    /// handler de Fase 5 (junto con MarkAsSent y el publish del callback), no aislado como la reserva.
    /// </summary>
    public async Task CompleteAsync(Guid tenantId, string idempotencyKey, Guid sentMessageId, CancellationToken ct)
    {
        var existing = await FindAsync(tenantId, idempotencyKey, ct);
        if (existing is null)
            throw new InvalidOperationException($"No idempotency reservation found for key '{idempotencyKey}'.");

        var completeResult = existing.Complete(sentMessageId, DateTime.UtcNow);
        if (completeResult.IsFailure)
            throw new InvalidOperationException(completeResult.Error.Message);
    }

    private Task<EmailIdempotency?> FindAsync(Guid tenantId, string idempotencyKey, CancellationToken ct) =>
        dbContext.EmailIdempotencies.FirstOrDefaultAsync(
            e => e.TenantId == tenantId && e.IdempotencyKey == idempotencyKey,
            ct
        );

    /// <summary>Reserva sin completar y fuera de la ventana de retry — el proceso original probablemente crasheó; se limpia para reintentar.</summary>
    private async Task RemoveAbandonedReservationAsync(EmailIdempotency existing, CancellationToken ct)
    {
        dbContext.EmailIdempotencies.Remove(existing);
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task<IdempotencyReservationResult> InsertReservationAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        var reserveResult = EmailIdempotency.Reserve(tenantId, idempotencyKey, DateTime.UtcNow, ReservationTimeToLive);
        if (reserveResult.IsFailure)
            throw new InvalidOperationException(reserveResult.Error.Message);

        await dbContext.EmailIdempotencies.AddAsync(reserveResult.Value, ct);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (ConflictException)
        {
            // Carrera real: dos inserts concurrentes leyeron "no existe reserva" antes de que
            // cualquiera escribiera. El índice único de EmailIdempotency resuelve el empate — el
            // perdedor cae acá y debe tratarse como InProgress, NUNCA como Reserved (ese era
            // exactamente el bug: ambos casos devolvían null y el caller no podía distinguirlos).
            logger.LogInformation(
                "Idempotency reservation race lost for {TenantId}/{IdempotencyKey}; treating as in-progress.",
                tenantId,
                idempotencyKey
            );
            return IdempotencyReservationResult.InProgress();
        }

        return IdempotencyReservationResult.Reserved();
    }
}
