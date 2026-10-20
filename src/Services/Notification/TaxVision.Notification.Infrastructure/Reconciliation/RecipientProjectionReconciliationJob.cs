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
