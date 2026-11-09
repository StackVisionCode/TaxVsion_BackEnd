using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Api.Jobs;

/// <summary>
/// Retira los denies por usuario que ya vencieron y avisa a las proyecciones. Un deny temporal
/// ("sin acceso a facturación hasta el cierre del mes") deja de restar en cuanto pasa su fecha —
/// la consulta de permisos efectivos ya lo ignora—, pero la fila sigue ahí y, sobre todo, las
/// proyecciones locales de los 24 servicios no se enteran hasta que alguien le toque los roles.
/// Este barrido cierra ese hueco: borra la fila vencida y publica el
/// <c>UserRolesChangedIntegrationEvent</c> del titular con sus códigos ya recalculados.
/// <para>
/// Es el anti-lockout de la capa de denies: si el administrador que lo puso se fue, o nadie se
/// acuerda de quitarlo, el acceso vuelve solo en la fecha pactada.
/// </para>
/// Idempotente y seguro con varias réplicas: quien pierde la carrera no encuentra filas vencidas.
/// </summary>
public sealed class ExpiredPermissionDeniesService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<ExpiredPermissionDeniesService> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>Tope por tick: un barrido acotado no bloquea la tabla ni inunda el bus.</summary>
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Igual que AuthMaintenanceService: el primer tick espera a que la app arranque, porque
        // el outbox de Wolverine revienta si Wolverine todavía no está.
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Expired permission denies sweep failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        IReadOnlyList<(Guid UserId, Guid TenantId)> affected;
        IReadOnlyList<Permission> catalog;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            affected = await roles.GetUsersWithExpiredDeniesAsync(now, BatchSize, ct);
            if (affected.Count == 0)
                return;
            catalog = await roles.GetPermissionsCatalogAsync(ct);
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var removed = 0;

        // Una unidad por titular: el borrado del deny y su aviso se confirman juntos.
        var summary = await TransactionalJobUnit.RunEachAsync<Guid, AuthDbContext>(
            scopeFactory,
            affected.Select(entry => entry.UserId).Distinct(),
            async (userId, unit, token) =>
            {
                var roles = unit.Services.GetRequiredService<IRoleRepository>();
                var users = unit.Services.GetRequiredService<IUserRepository>();
                var removedForUser = await roles.RemoveExpiredDeniesAsync(userId, now, token);
                removed += removedForUser;
                var user = await users.GetByIdAsync(userId, token);
                if (user is null)
                    return removedForUser > 0;

                // El fan-out lee los denies vigentes de la base, y la consulta ya ignora los vencidos:
                // el evento y la tabla cuentan lo mismo al confirmar.
                await RolePermissionsFanOut.PublishForUsersAsync(
                    [user],
                    catalog,
                    roles,
                    unit.Outbox,
                    correlationId,
                    token
                );
                return true;
            },
            logger,
            ct
        );

        logger.LogInformation(
            "Expired permission denies: removed {Removed} deny row(s) and re-published access for {Users} user(s).",
            removed,
            summary.Committed
        );
    }
}
