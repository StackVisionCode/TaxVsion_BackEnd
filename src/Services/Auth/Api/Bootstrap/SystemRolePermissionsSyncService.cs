using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Infrastructure.Persistence;

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
/// </summary>
public sealed class SystemRolePermissionsSyncService(
    IServiceScopeFactory scopeFactory,
    ILogger<SystemRolePermissionsSyncService> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var systemRoles = await db
            .Roles.Include(role => role.Permissions)
            .Where(role => role.IsSystem)
            .ToListAsync(cancellationToken);

        if (systemRoles.Count == 0)
            return;

        var updated = 0;
        foreach (var role in systemRoles)
        {
            var currentIds = role.Permissions.Select(link => link.PermissionId).ToHashSet();
            var expectedIds = PermissionCatalog.SystemRoleDefaults(role.Name).Select(PermissionCatalog.IdOf).ToList();

            if (currentIds.SetEquals(expectedIds))
                continue;

            role.SetPermissions(expectedIds, seeding: true);
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "SystemRolePermissionsSync: resynced {Updated} of {Total} system role(s) against the current PermissionCatalog.",
                updated,
                systemRoles.Count
            );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
