using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Auth.Api.Bootstrap;

/// <summary>
/// Backfill de arranque: recomputa el set de permisos de cada rol de sistema existente
/// (Tenant Admin / Employee / Customer Portal, en cada tenant) contra el catálogo actual.
/// <para>
/// <see cref="TaxVision.Auth.Infrastructure.Persistence.Repositories.RoleRepository.EnsureSystemRolesAsync"/>
/// solo computa <see cref="PermissionCatalog.SystemRoleDefaults"/> UNA VEZ, al crear el tenant — un
/// permiso agregado al catálogo después de eso nunca llega a las filas RolePermission de tenants
/// ya existentes. Antes de esta sesión eso era inofensivo porque TenantAdmin pasaba HasPermission
/// por bypass de rol sin importar el claim real; al retirar ese bypass (auditoría de permisos
/// PlatformAdmin-only vs TenantAdmin) quedó expuesto: un TenantAdmin real recibió 403 en
/// Postmaster/ProvidersController pese a que el catálogo sí incluía postmaster.providers.read por
/// defecto — sus RolePermission nunca se habían actualizado desde que el tenant se creó.
/// </para>
/// Seguro por diseño: <see cref="Role.SetPermissions"/> solo permite reemplazar el set completo de
/// un rol de sistema vía <c>seeding: true</c> — los roles de sistema son inmutables fuera de este
/// mecanismo (<see cref="Role.Update"/>/<see cref="Role.Deactivate"/> los rechazan explícitamente),
/// así que no hay ninguna customización manual del tenant que este resync pueda pisar.
/// Idempotente: EF solo genera UPDATE/INSERT/DELETE para las filas que realmente cambiaron.
/// <para>
/// RBAC Fase 2 (RBAC_Hardening_Plan.md): el plan original pedía un <c>TenantAdminPermissionsBackfillService</c>
/// nuevo con su propia tabla de cursor. Se descartó a propósito — este servicio ya cubre exactamente
/// ese caso (incluye <c>SystemTenantAdmin</c>, corre en cada arranque, es idempotente por diseño) y
/// duplicar la lógica habría violado la Regla de oro #1 del plan ("cero rediseño"). Lo único que le
/// faltaba era publicar <see cref="RolePermissionsChangedIntegrationEvent"/> por cada rol
/// efectivamente modificado — gap ya documentado en <c>project_fase3_permissions_backfill.md</c>
/// ("SystemRolePermissionsSyncService bumps Role.PermissionsVersion without publishing the new
/// event") — así que se cierra acá en vez de en un servicio paralelo.
/// </para>
/// </summary>
public sealed class SystemRolePermissionsSyncService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<SystemRolePermissionsSyncService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // RBAC Fase 5 — resync cross-tenant deliberado (recomputa el rol de sistema de CADA
        // tenant contra el catálogo actual), no un olvido de filtrar por tenant.
        var systemRoles = await db
            .Roles.IgnoreQueryFilters()
            .Include(role => role.Permissions)
            .Where(role => role.IsSystem)
            .ToListAsync(cancellationToken);

        if (systemRoles.Count == 0)
            return;

        var codeById = PermissionCatalog.All.ToDictionary(definition => definition.Id, definition => definition.Code);
        var changedRoles = new List<Role>();
        foreach (var role in systemRoles)
        {
            var currentIds = role.Permissions.Select(link => link.PermissionId).ToHashSet();
            var expectedIds = PermissionCatalog.SystemRoleDefaults(role.Name).Select(PermissionCatalog.IdOf).ToList();

            if (currentIds.SetEquals(expectedIds))
                continue;

            role.SetPermissions(expectedIds, seeding: true);
            changedRoles.Add(role);
        }

        if (changedRoles.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);

            foreach (var role in changedRoles)
            {
                var permissionCodes = role
                    .Permissions.Select(link => link.PermissionId)
                    .Where(codeById.ContainsKey)
                    .Select(id => codeById[id])
                    .ToArray();

                await bus.PublishAsync(
                    new RolePermissionsChangedIntegrationEvent
                    {
                        TenantId = role.TenantId,
                        RoleId = role.Id,
                        RoleName = role.Name,
                        PermissionCodes = permissionCodes,
                        PermissionsVersion = role.PermissionsVersion,
                        CorrelationId = Guid.NewGuid().ToString("N"),
                    }
                );
            }

            logger.LogInformation(
                "SystemRolePermissionsSync: resynced {Updated} of {Total} system role(s) against the current PermissionCatalog.",
                changedRoles.Count,
                systemRoles.Count
            );
        }
    }
}
