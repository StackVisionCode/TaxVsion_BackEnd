using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

/// <summary>
/// A6 — alta del gate de módulo en un servicio: registra la fuente de entitlements y **valida la
/// configuración del escalón en el mismo acto**.
///
/// Las dos cosas van juntas a propósito. Cuando estaban separadas, el registro se copió en 13
/// servicios y la validación solo llegó a uno: los otros 12 podían arrancar con un módulo mal escrito
/// en <c>EnforcedModules</c>, quedarse en log-only y hacer creer que el gate estaba puesto. Un fallo
/// silencioso hacia el lado peligroso, exactamente lo que la validación existe para evitar. Metida en
/// el registro, no se puede olvidar en el siguiente servicio.
/// </summary>
public static class ModuleGateRegistration
{
    /// <param name="logger">
    /// Opcional. Se usa para el aviso cuando el gate está APAGADO y la lista nombra un módulo
    /// desconocido (encendido no se avisa: se lanza) y para dejar el modo en el registro. Sin logger se
    /// crea uno de consola en el acto, porque esto corre antes de que exista el proveedor de logging de
    /// la app y ese aviso es la única señal que tiene quien escribió la configuración.
    /// </param>
    public static IServiceCollection AddModuleGate(
        this IServiceCollection services,
        IConfiguration configuration,
        ILogger? logger = null
    )
    {
        if (logger is not null)
        {
            Announce(configuration, logger);
        }
        else
        {
            using var factory = LoggerFactory.Create(logging => logging.AddConsole());
            Announce(configuration, factory.CreateLogger("ModuleGate"));
        }

        services.AddScoped<ITenantModuleEntitlementsSource, TenantModuleEntitlementsSource>();

        return services;
    }

    /// <summary>
    /// Valida la configuración y deja el modo en el registro de arranque. La línea existe porque el
    /// escalón se sube por configuración en 13 servicios: sin ella, la única forma de saber si un
    /// despliegue quedó en log-only o denegando es provocar un 403 en producción.
    /// </summary>
    private static void Announce(IConfiguration configuration, ILogger logger)
    {
        ModuleGateSettings.ValidateOrThrow(configuration, logger);

        var staged = configuration.GetSection(ModuleGateSettings.EnforcedModulesKey).Get<string[]>();
        var enforcing = configuration.GetValue(ModuleGateSettings.EnforceKey, false);

        logger.LogInformation(
            "Module gate registered: {Mode} ({Scope}).",
            enforcing ? "ENFORCING" : "log-only",
            enforcing && staged is { Length: > 0 } ? $"modules: {string.Join(", ", staged)}" : "all modules"
        );
    }
}
