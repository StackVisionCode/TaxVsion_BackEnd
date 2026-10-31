using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Infrastructure.Persistence;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Infrastructure.Avatars;

/// <summary>
/// Descarta las fotos pendientes vencidas (sin resultado del escaneo pasado el timeout). CloudStorage no
/// avisa todos los rechazos (política, cuota), así que sin esto quedarían "procesando" para siempre. Si
/// el archivo aparece después, el consumer lo trata como huérfano y pide su borrado.
/// </summary>
public sealed class CustomerAvatarPendingSweeper(CustomerDbContext db, TimeProvider time)
{
    public const int BatchSize = 200;

    /// <summary>Barre un lote (cross-tenant). Devuelve cuántas pendientes descartó.</summary>
    public async Task<int> SweepOnceAsync(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - DomainCustomer.PendingProfilePictureTimeout;

        // Cross-tenant a propósito (job de sistema): IgnoreQueryFilters explícito.
        var stale = await db
            .Customers.IgnoreQueryFilters()
            .Where(c =>
                c.PendingProfilePictureFileId != null
                && c.PendingProfilePictureRequestedAtUtc != null
                && c.PendingProfilePictureRequestedAtUtc < cutoff
                // En revisión de contenido espera al revisor: no es un escaneo que no respondió.
                && !(
                    c.ProfilePictureModerationState == ProfilePictureModerationState.UnderReview
                    && c.ProfilePictureModerationFileId == c.PendingProfilePictureFileId
                )
            )
            .OrderBy(c => c.PendingProfilePictureRequestedAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        var discarded = stale.Count(c => c.DiscardPendingProfilePicture(c.PendingProfilePictureFileId!.Value));
        if (discarded > 0)
            await db.SaveChangesAsync(ct);

        return discarded;
    }
}

/// <summary>Corre <see cref="CustomerAvatarPendingSweeper"/> cada pocos minutos, después del arranque.</summary>
internal sealed class CustomerAvatarPendingSweepJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<CustomerAvatarPendingSweepJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<CustomerAvatarPendingSweeper>();
                var discarded = await sweeper.SweepOnceAsync(stoppingToken);
                if (discarded > 0)
                    logger.LogInformation("Discarded {Count} expired pending customer avatars.", discarded);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Customer avatar pending sweep failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
