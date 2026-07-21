using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Auth.Api.Bootstrap;

/// <summary>
/// Backfill de arranque (Fase 3 del plan de notificaciones dinámicas): re-publica
/// <see cref="UserRolesChangedIntegrationEvent"/> (ya con el contrato arreglado de la Fase 1,
/// <c>PermissionCodes</c>/<c>RoleIds</c> incluidos) para cada usuario activo que ya tuvo al
/// menos un cambio de rol alguna vez (<c>PermissionsVersion &gt; 0</c>) pero todavía no fue
/// reparado. Sin esto, los datos que quedaron mal por el bug de mapeo de campos (Fase 1) antes
/// de este fix — la proyección <c>UserPermissionsProjection</c> de Communication con
/// <c>Permissions = []</c> — quedan mal para siempre, porque nadie los va a volver a tocar
/// naturalmente si ese usuario no recibe otro cambio de rol.
///
/// <para>
/// Mismo patrón que <see cref="TenantDomainBackfillService"/>: nada de HTTP M2M — reproduce el
/// mismo evento de siempre, dirigido, con un cursor persistido (<c>User.PermissionsBackfilledAt</c>)
/// en vez de un flag "ya corrí" separado, así la condición de "qué falta" se recalcula con una
/// query normal en cada arranque y se hace cada vez más chica hasta llegar a cero filas — en cero
/// filas este <see cref="IHostedService"/> es un no-op instantáneo, sin loop infinito.
/// </para>
/// Solo usuarios activos: un usuario desactivado no debe re-"activarse" en la proyección de
/// Communication (su <c>upsert</c> siempre escribe <c>isActive: true</c> para este evento).
/// </summary>
public sealed class PermissionsBackfillService(
    IServiceScopeFactory scopeFactory,
    ILogger<PermissionsBackfillService> logger
) : IHostedService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan DelayBetweenBatches = TimeSpan.FromSeconds(2);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var totalRepublished = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            var pending = await db
                .Users.Where(user =>
                    user.IsActive && user.PermissionsVersion > 0 && user.PermissionsBackfilledAt == null
                )
                .OrderBy(user => user.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0)
                break;

            foreach (var user in pending)
            {
                var userRoles = await roles.GetUserRolesAsync(user.Id, cancellationToken);
                var permissionCodes = await roles.GetEffectivePermissionCodesAsync(user.Id, cancellationToken);

                await bus.PublishAsync(
                    new UserRolesChangedIntegrationEvent
                    {
                        TenantId = user.TenantId,
                        UserId = user.Id,
                        PermissionsVersion = user.PermissionsVersion,
                        RoleNames = userRoles.Select(role => role.Name).ToArray(),
                        RoleIds = userRoles.Select(role => role.Id).ToArray(),
                        PermissionCodes = permissionCodes.ToArray(),
                    }
                );
                user.MarkPermissionsBackfilled(DateTime.UtcNow);
            }

            await db.SaveChangesAsync(cancellationToken);
            totalRepublished += pending.Count;
            logger.LogInformation(
                "PermissionsBackfill: re-published {Count} UserRolesChanged event(s) this batch ({Total} total so far).",
                pending.Count,
                totalRepublished
            );

            if (pending.Count < BatchSize)
                break;

            await Task.Delay(DelayBetweenBatches, cancellationToken);
        }

        if (totalRepublished > 0)
        {
            logger.LogInformation("PermissionsBackfill: completed, {Total} user(s) re-published.", totalRepublished);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
