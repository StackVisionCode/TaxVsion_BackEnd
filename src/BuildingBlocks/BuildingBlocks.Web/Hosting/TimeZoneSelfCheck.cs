using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.Hosting;

/// <summary>
/// Al arrancar deja en el log si el proceso resuelve las zonas IANA de prueba, su zona y si corre en modo invariante.
/// Solo registra: nunca detiene el arranque (P22).
/// </summary>
public sealed class TimeZoneSelfCheck(ILogger<TimeZoneSelfCheck> logger) : IHostedService
{
    private static readonly string[] Probes = ["America/New_York", "Asia/Tokyo"];

    public static IReadOnlyList<string> MissingZones(Func<string, bool> resolves) => Probes.Where(id => !resolves(id)).ToList();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var missing = MissingZones(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _));
        var invariant = AppContext.TryGetSwitch("System.Globalization.Invariant", out var on) && on;
        if (missing.Count > 0 || invariant)
            logger.LogWarning(
                "Time zone self-check: missing {MissingZones} of {ProbeZones}; invariant globalization {Invariant}; process zone {ProcessZone}",
                string.Join(", ", missing),
                string.Join(", ", Probes),
                invariant,
                TimeZoneInfo.Local.Id
            );
        else
            logger.LogInformation(
                "Time zone self-check: {ProbeZones} resolved; process zone {ProcessZone}",
                string.Join(", ", Probes),
                TimeZoneInfo.Local.Id
            );
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class TimeZoneSelfCheckExtensions
{
    /// <summary>Opcional y aditivo: registra <see cref="TimeZoneSelfCheck"/>.</summary>
    public static IServiceCollection AddTimeZoneSelfCheck(this IServiceCollection services)
    {
        services.AddHostedService<TimeZoneSelfCheck>();
        return services;
    }
}
