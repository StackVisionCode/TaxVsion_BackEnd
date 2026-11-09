using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Recipients;
using TaxVision.Notification.Infrastructure.Persistence;

namespace TaxVision.Notification.Infrastructure.Reconciliation;

/// <summary>
/// Reconciles recipient projections from Auth.
/// </summary>
internal sealed class RecipientProjectionReconciliationJob(
    IServiceProvider serviceProvider,
    ILogger<RecipientProjectionReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        var options = serviceProvider.GetRequiredService<IOptions<RecipientProjectionReconciliationOptions>>().Value;
        if (!options.ReconciliationEnabled)
        {
            logger.LogInformation("RecipientProjectionReconciliationJob disabled by config; not running.");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.ReconciliationIntervalHours));
        var pageSize = Math.Max(1, options.ReconciliationPageSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceSafeAsync(pageSize, stoppingToken);
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOnceSafeAsync(int pageSize, CancellationToken ct)
    {
        try
        {
            await ReconcileOwnersAsync(pageSize, ct);
            await ReconcilePortalAccountsAsync(pageSize, ct);
            await ReconcileUserEmailDirectoryAsync(pageSize, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RecipientProjectionReconciliationJob iteration failed.");
        }
    }

    private async Task ReconcileOwnersAsync(int pageSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<INotificationAuthReconciliationClient>();
        var owners = scope.ServiceProvider.GetRequiredService<ITenantOwnerProjectionRepository>();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var page = 1;
        while (!ct.IsCancellationRequested)
        {
            var result = await client.ListOwnersAsync(page, pageSize, ct);
            if (result is null)
                return;

            foreach (var owner in result.Items)
            {
                var existing = await owners.GetByUserAsync(owner.TenantId, owner.UserId, ct);
                if (existing is null)
                    await owners.AddAsync(
                        TenantOwnerProjection.Create(
                            owner.TenantId,
                            owner.UserId,
                            owner.RegisteredAtUtc,
                            owner.IsActive
                        ),
                        ct
                    );
                else
                    existing.Synchronize(owner.RegisteredAtUtc, owner.IsActive);
            }

            await unitOfWork.SaveChangesAsync(ct);
            // Liberar entidades traqueadas antes de la siguiente página: evita que un
            // DbContext de scope largo acumule memoria y haga más lento cada SaveChanges.
            db.ChangeTracker.Clear();
            if (!result.HasMore)
                break;
            page++;
        }
    }

    private async Task ReconcilePortalAccountsAsync(int pageSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<INotificationAuthReconciliationClient>();
        var portals = scope.ServiceProvider.GetRequiredService<ICustomerPortalAccountProjectionRepository>();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var page = 1;
        while (!ct.IsCancellationRequested)
        {
            var result = await client.ListPortalAccountsAsync(page, pageSize, ct);
            if (result is null)
                return;

            foreach (var portal in result.Items)
            {
                await UpsertPortalAccountAsync(portals, portal, ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            if (!result.HasMore)
                break;
            page++;
        }
    }

    // Repasa las filas inactivas u obsoletas del directorio de correos y las corrige con lo que dice
    // Auth. Una unidad por usuario: un fallo (Auth caído, choque) no corta el lote.
    internal async Task<int> ReconcileUserEmailDirectoryAsync(int pageSize, CancellationToken ct)
    {
        var repaired = 0;
        var lastId = Guid.Empty;
        var started = false;
        while (!ct.IsCancellationRequested)
        {
            List<(Guid Id, Guid TenantId, Guid UserId)> page;
            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                var rows = await db
                    .UserEmailDirectoryEntries.IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(e => !e.IsActive || e.IsStale)
                    .Where(e => !started || e.Id.CompareTo(lastId) > 0)
                    .OrderBy(e => e.Id)
                    .Take(pageSize)
                    .Select(e => new
                    {
                        e.Id,
                        e.TenantId,
                        e.UserId,
                    })
                    .ToListAsync(ct);
                page = rows.Select(r => (r.Id, r.TenantId, r.UserId)).ToList();
            }

            foreach (var row in page)
                repaired += await ReconcileUserEmailAsync(row.TenantId, row.UserId, ct) ? 1 : 0;

            if (page.Count < pageSize)
                break;
            lastId = page[^1].Id;
            started = true;
        }

        if (repaired > 0)
            logger.LogInformation(
                "RecipientProjectionReconciliationJob repaired {Count} email directory row(s).",
                repaired
            );
        return repaired;
    }

    private async Task<bool> ReconcileUserEmailAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        try
        {
            var remote = await services
                .GetRequiredService<IUserContactSnapshotClient>()
                .FetchContactAsync(tenantId, userId, ct);
            if (remote is null)
                return false;

            var entry = await services
                .GetRequiredService<IUserEmailDirectoryRepository>()
                .FindAsync(tenantId, userId, ct);
            if (
                entry is null
                || (entry.IsActive == remote.IsActive && !entry.IsStale && entry.Email == remote.Email.Trim())
            )
                return false;

            entry.UpdateEmail(remote.Email, remote.IsActive, DateTime.UtcNow);
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "RecipientProjectionReconciliationJob skipped email directory user {UserId} of tenant {TenantId}.",
                userId,
                tenantId
            );
            return false;
        }
    }

    // Tolerante a N cuentas de portal por cliente (ya se vio en Auth): la proyección mantiene
    // unique por (TenantId, CustomerId) para que la resolución de audiencia sea determinista.
    // Reglas:
    //   - Si no hay fila para el cliente, se crea con el usuario entrante.
    //   - Si la fila existente coincide por UserId: sync idempotente.
    //   - Si existe otra fila con el mismo UserId (otro cliente), sync idempotente ahí también.
    //   - Si existe con otro UserId: la nueva solo gana si la existente está inactiva o si la
    //     nueva está activa y la existente no. Un conflicto entre dos cuentas activas se loguea
    //     y preserva la existente para evitar flip-flop en cada corrida.
    private async Task UpsertPortalAccountAsync(
        ICustomerPortalAccountProjectionRepository portals,
        RemotePortalAccount portal,
        CancellationToken ct
    )
    {
        var byUser = await portals.GetByUserAsync(portal.TenantId, portal.UserId, ct);
        if (byUser is not null)
        {
            byUser.Synchronize(portal.UserId, portal.IsActive);
            return;
        }

        var byCustomer = await portals.GetByCustomerAsync(portal.TenantId, portal.CustomerId, ct);
        if (byCustomer is null)
        {
            await portals.AddAsync(
                CustomerPortalAccountProjection.Create(
                    portal.TenantId,
                    portal.CustomerId,
                    portal.UserId,
                    portal.IsActive
                ),
                ct
            );
            return;
        }

        if (portal.IsActive && !byCustomer.IsActive)
        {
            byCustomer.Synchronize(portal.UserId, true);
            return;
        }

        if (byCustomer.IsActive && portal.IsActive)
        {
            logger.LogWarning(
                "Multiple active portal accounts for customer {CustomerId} in tenant {TenantId}; keeping existing user {ExistingUserId}, incoming {IncomingUserId} ignored.",
                portal.CustomerId,
                portal.TenantId,
                byCustomer.UserId,
                portal.UserId
            );
        }
    }
}
