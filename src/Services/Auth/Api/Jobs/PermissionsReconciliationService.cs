using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Web.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Auth.Api.Jobs;

/// <summary>
/// Anti-entropy periódico de las proyecciones de permisos (§44). El primo periódico de
/// <c>PermissionsBackfillService</c>: ese repara UNA vez al arrancar (cursor persistido
/// <c>PermissionsBackfilledAt</c>); éste re-publica <see cref="UserRolesChangedIntegrationEvent"/>
/// para TODOS los usuarios activos cada intervalo, sin cursor, de modo que un servicio que perdió un
/// evento (RabbitMQ caído sin reiniciar Auth) converge sin intervención manual. Auth es la fuente de
/// verdad; cada servicio ya reconcilia su proyección local con este mismo evento.
/// <para>
/// Empuja (no consulta): un solo job acá reemplaza un job de reconciliación en cada uno de los ~23
/// servicios. Espera un intervalo completo antes del primer pase (el backfill de arranque ya cubre el
/// arranque en frío). Cross-tenant a propósito (IgnoreQueryFilters + tenant por usuario), batcheado
/// para no saturar el bus. No bumpea la versión de permisos: no invalida JWT vigentes.
/// </para>
/// </summary>
public sealed class PermissionsReconciliationService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<PermissionsReconciliationService> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int BatchSize = 50;
    private static readonly TimeSpan DelayBetweenBatches = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Esperar a que la app arranque: SaveChanges/PublishAsync antes de que Wolverine esté listo
        // revienta (mismo motivo que AuthMaintenanceService).
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Permissions reconciliation run failed.");
            }
        }
    }

    /// <summary>Re-publica el evento de permisos de cada usuario activo, paginando por keyset sobre el
    /// Id (sin estado persistido: cada pase recorre a todos).</summary>
    internal async Task<int> ReconcileOnceAsync(CancellationToken ct)
    {
        var totalRepublished = 0;
        var afterUserId = Guid.Empty;

        while (!ct.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();

            // Cross-tenant a propósito: el anti-entropy re-emite para TODOS los tenants en un pase.
            var batch = await db
                .Users.IgnoreQueryFilters()
                .Where(user => user.IsActive && user.Id > afterUserId)
                .OrderBy(user => user.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            foreach (var user in batch)
            {
                // GetUserRolesAsync/GetEffectivePermissionCodesAsync consultan Role (tenant-owned):
                // sin setear el tenant del usuario devolverían 0 filas bajo el filtro fail-closed.
                tenantContext.SetTenant(user.TenantId);

                var userRoles = await roles.GetUserRolesAsync(user.Id, ct);
                var permissionCodes = await roles.GetEffectivePermissionCodesAsync(user.Id, ct);

                await bus.PublishAsync(
                    new UserRolesChangedIntegrationEvent
                    {
                        TenantId = user.TenantId,
                        CorrelationId = correlation.CorrelationId,
                        UserId = user.Id,
                        PermissionsVersion = user.PermissionsVersion,
                        RoleNames = userRoles.Select(role => role.Name).ToArray(),
                        RoleIds = userRoles.Select(role => role.Id).ToArray(),
                        PermissionCodes = permissionCodes.ToArray(),
                        ActorType = user.ActorType.ToString(),
                    }
                );
            }

            totalRepublished += batch.Count;
            afterUserId = batch[^1].Id;

            if (batch.Count < BatchSize)
                break;

            await Task.Delay(DelayBetweenBatches, ct);
        }

        if (totalRepublished > 0)
            logger.LogInformation(
                "PermissionsReconciliation: re-published {Total} user permission event(s) for anti-entropy.",
                totalRepublished
            );

        return totalRepublished;
    }
}
