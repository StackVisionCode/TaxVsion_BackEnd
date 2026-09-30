using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using Wolverine;

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
        // SaveChangesAsync auto-publica por Wolverine y revienta si Wolverine todavía no está.
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
        using var scope = scopeFactory.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var now = DateTime.UtcNow;
        var affected = await roles.GetUsersWithExpiredDeniesAsync(now, BatchSize, ct);
        if (affected.Count == 0)
            return;

        var removed = 0;
        var holders = new List<Domain.Users.User>(affected.Count);
        foreach (var (userId, _) in affected)
        {
            removed += await roles.RemoveExpiredDeniesAsync(userId, now, ct);
            if (await users.GetByIdAsync(userId, ct) is { } user)
                holders.Add(user);
        }

        // El borrado tiene que estar aplicado antes de recalcular: el fan-out lee los denies
        // vigentes de la base, y una fila vencida todavía sin borrar no cambia el resultado
        // (la consulta ya la filtra por fecha), pero así el evento y la tabla cuentan lo mismo.
        var catalog = await roles.GetPermissionsCatalogAsync(ct);
        await RolePermissionsFanOut.PublishForUsersAsync(
            holders,
            catalog,
            roles,
            bus,
            Guid.NewGuid().ToString("N"),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Expired permission denies: removed {Removed} deny row(s) and re-published access for {Users} user(s).",
            removed,
            holders.Count
        );
    }
}
