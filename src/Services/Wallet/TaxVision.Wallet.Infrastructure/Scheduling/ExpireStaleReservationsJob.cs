using System.Diagnostics.Metrics;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Wallet.Application.Reservations.Abstractions;
using TaxVision.Wallet.Application.Reservations.Commands;
using TaxVision.Wallet.Domain.Reservations;
using Wolverine;

namespace TaxVision.Wallet.Infrastructure.Scheduling;

/// <summary>
/// WAL-4: cada <see cref="ReservationExpiryOptions.ExpiryInterval"/> busca reservas <c>Open</c> más viejas que
/// el TTL de su tipo y vence cada una con su propio <see cref="ExpireReservationCommand"/> (su transacción y su
/// outbox): un conflicto no arrastra a las demás. Candado en Redis para una sola réplica. Cada vencimiento es un
/// error del consumidor: va al log como advertencia y suma a la métrica <c>wallet.reservations.expired</c>.
/// </summary>
public sealed class ExpireStaleReservationsJob(
    IServiceScopeFactory scopeFactory,
    IDistributedLockFactory lockFactory,
    IHostApplicationLifetime lifetime,
    IOptions<ReservationExpiryOptions> options,
    ILogger<ExpireStaleReservationsJob> logger
) : BackgroundService
{
    private const string LockKey = "expire-stale-reservations";

    // Meter con el nombre del servicio: OpenTelemetryRegistration ya lo exporta.
    private static readonly Meter Meter = new("wallet-service");
    private static readonly Counter<long> ExpiredCounter = Meter.CreateCounter<long>(
        "wallet.reservations.expired",
        description: "Open reservations released by TTL (each one is a consumer that never settled)."
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.ExpiryEnabled)
        {
            logger.LogInformation("Reservation expiry job is disabled by configuration.");
            return;
        }

        // Sin esperar el arranque, invocar puede ganarle a Wolverine (WolverineHasNotStartedException).
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(settings.ExpiryInterval);
        do
        {
            try
            {
                await using var handle = await lockFactory.TryAcquireAsync(
                    LockKey,
                    settings.ExpiryInterval,
                    stoppingToken
                );
                if (handle is null)
                    continue;

                using var scope = scopeFactory.CreateScope();
                await ExpireBatchAsync(
                    scope.ServiceProvider.GetRequiredService<IReservationRepository>(),
                    scope.ServiceProvider.GetRequiredService<IMessageBus>(),
                    settings,
                    logger,
                    DateTime.UtcNow,
                    stoppingToken
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reservation expiry iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Una pasada: candidatos por tipo según su TTL y un comando por reserva. Público para probarlo con fakes.</summary>
    public static async Task<int> ExpireBatchAsync(
        IReservationRepository reservations,
        IMessageBus bus,
        ReservationExpiryOptions settings,
        ILogger logger,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var candidates = new List<WalletReservation>();
        foreach (var (referenceType, ttl) in settings.OpenTtl)
            candidates.AddRange(
                await reservations.ListOpenOlderThanAsync(
                    nowUtc - ttl,
                    settings.ExpiryBatchSize,
                    referenceType,
                    null,
                    ct
                )
            );
        candidates.AddRange(
            await reservations.ListOpenOlderThanAsync(
                nowUtc - settings.DefaultOpenTtl,
                settings.ExpiryBatchSize,
                null,
                settings.OpenTtl.Keys.ToList(),
                ct
            )
        );

        var expired = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await bus.InvokeAsync<Result<ReservationExpiry>>(
                    new ExpireReservationCommand(candidate.TenantId, candidate.ReferenceType, candidate.ReferenceId),
                    ct
                );
                if (result is null)
                    continue;
                if (result.IsFailure)
                {
                    logger.LogWarning(
                        "Reservation {RefType}:{RefId} (tenant {TenantId}) not expired: {Code}. Retrying next round.",
                        candidate.ReferenceType,
                        candidate.ReferenceId,
                        candidate.TenantId,
                        result.Error.Code
                    );
                    continue;
                }
                if (!result.Value.Expired)
                    continue;

                expired++;
                ExpiredCounter.Add(1, new KeyValuePair<string, object?>("reference_type", candidate.ReferenceType));
                logger.LogWarning(
                    "Reservation {RefType}:{RefId} (tenant {TenantId}) expired unsettled after TTL; released {ReleasedMicros} micros (created {CreatedAtUtc:o}).",
                    candidate.ReferenceType,
                    candidate.ReferenceId,
                    candidate.TenantId,
                    result.Value.ReleasedMicros,
                    candidate.CreatedAtUtc
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "Reservation {RefType}:{RefId} (tenant {TenantId}) expiry threw. Retrying next round.",
                    candidate.ReferenceType,
                    candidate.ReferenceId,
                    candidate.TenantId
                );
            }
        }

        return expired;
    }
}
