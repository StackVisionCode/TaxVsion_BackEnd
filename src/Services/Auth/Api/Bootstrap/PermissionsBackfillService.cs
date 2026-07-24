using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Auth.Api.Bootstrap;

/// <summary>
/// Backfill de arranque: re-publica <see cref="UserRolesChangedIntegrationEvent"/> (contrato con
/// <c>PermissionCodes</c>/<c>RoleIds</c> incluidos) para cada usuario activo que todavía no fue
/// reparado (<c>PermissionsBackfilledAt == null</c>).
///
/// <para>
/// Se agregó originalmente para el bug de mapeo de campos de la Fase 1 (proyección de
/// Communication con <c>Permissions = []</c>) y por eso filtraba también por
/// <c>PermissionsVersion &gt; 0</c> — pero ese filtro dejaba afuera a cualquier usuario dado de
/// alta por invitación, porque <c>AcceptInvitationHandler</c> nunca bumpeaba esa versión (bug
/// aparte, corregido ahí mismo). Con RBAC Fase 7/7.5 este job pasó a ser crítico para
/// autorización — <c>ProjectionPermissionsSource</c> rechaza en frío cualquier request sin fila de
/// proyección — así que el filtro de versión ya no tiene sentido: hay que reparar a TODO usuario
/// activo sin reparar, tenga o no un cambio de rol explícito en su historial.
/// </para>
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
    IHostApplicationLifetime lifetime,
    ILogger<PermissionsBackfillService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    private const int BatchSize = 50;
    private static readonly TimeSpan DelayBetweenBatches = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var totalRepublished = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();

            // RBAC Fase 5 — este job recorre usuarios de TODOS los tenants en un mismo batch, no
            // uno solo — IgnoreQueryFilters() explícito porque es el descubrimiento cross-tenant
            // que el job existe para hacer, no un descuido.
            var pending = await db
                .Users.IgnoreQueryFilters()
                .Where(user => user.IsActive && user.PermissionsBackfilledAt == null)
                .OrderBy(user => user.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0)
                break;

            foreach (var user in pending)
            {
                // RBAC Fase 5 — cada usuario procesado es de su propio tenant real; sin esto,
                // GetUserRolesAsync/GetEffectivePermissionCodesAsync (que consultan Role, sí
                // tenant-owned) devolverían 0 filas bajo el filtro fail-closed sin tenant seteado.
                tenantContext.SetTenant(user.TenantId);

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
}
