using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Infrastructure;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Qué implementaciones de envío registra <c>AddNotificationInfrastructure</c> tal como lo ve el
/// proceso real (antes nadie pasaba por ahí: los tests construían las clases directo).
///
/// <para>Hasta 2026-10-02 esta clase probaba que el flag <c>Notification:UsePostmasterDispatch</c>
/// elegía entre dos caminos. El camino `false` se retiró —resolvía contra
/// <c>EmailProviderConfigurations</c>, una tabla vacía en dev y en producción, así que el "rollback"
/// no encendía nada— y con él el flag. Lo que estos tests fijan ahora es lo contrario de lo que
/// fijaban antes: que **no hay nada que elegir**. Si alguien vuelve a meter una rama condicional
/// acá, el segundo test lo caza.</para>
/// </summary>
/// <remarks>
/// Se inspeccionan los <see cref="ServiceDescriptor"/> registrados en vez de construir el
/// <see cref="IServiceProvider"/> completo y resolver: <c>AddNotificationInfrastructure</c> registra
/// dependencias (repositorios, <c>NotificationDbContext</c>) que solo se pueden resolver end-to-end
/// contra una conexión SQL Server real, algo fuera de alcance de un test unitario de "qué implementación
/// se registró".
/// </remarks>
public sealed class NotificationDispatchDefaultRegistrationTests
{
    /// <summary>
    /// El default REAL, no uno hardcodeado en el test: carga el <c>appsettings.json</c> que de verdad
    /// se despliega con <c>TaxVision.Notification.Api</c>.
    /// </summary>
    [Fact]
    public void The_shipped_appsettings_registers_both_Postmaster_paths()
    {
        var appsettingsPath = GetShippedNotificationApiAppSettingsPath();
        Assert.True(File.Exists(appsettingsPath), $"No se encontró appsettings.json en '{appsettingsPath}'.");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appsettingsPath, optional: false)
            .AddInMemoryCollection(RequiredBootstrapConfig())
            .Build();

        var services = new ServiceCollection();
        services.AddNotificationInfrastructure(configuration);

        AssertLastRegisteredImplementation<IEmailDispatchGateway, EventBasedEmailDispatchGateway>(services);
        AssertLastRegisteredImplementation<IEmailDeliveryService, PostmasterEmailDeliveryService>(services);
    }

    /// <summary>
    /// El invariante que reemplaza al flag: ninguna configuración —ni el flag viejo puesto a
    /// <c>false</c> a mano, ni una config completamente vacía— puede hacer que se registre otra cosa.
    ///
    /// <para>Importa el caso de <c>false</c>: el valor puede seguir vivo en el <c>.env</c> de alguien
    /// o en un secret de GitHub después del despliegue. Que ahora se ignore no puede ser un accidente
    /// que nadie compruebe — si alguna vez vuelve a tener efecto, el correo saldría por un camino que
    /// ya no existe.</para>
    /// </summary>
    [Theory]
    [InlineData("false")]
    [InlineData("true")]
    [InlineData(null)]
    public void No_configuration_can_register_anything_else(string? legacyFlagValue)
    {
        var values = RequiredBootstrapConfig();
        if (legacyFlagValue is not null)
            values["Notification:UsePostmasterDispatch"] = legacyFlagValue;

        var services = new ServiceCollection();
        services.AddNotificationInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

        AssertLastRegisteredImplementation<IEmailDispatchGateway, EventBasedEmailDispatchGateway>(services);
        AssertLastRegisteredImplementation<IEmailDeliveryService, PostmasterEmailDeliveryService>(services);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string GetShippedNotificationApiAppSettingsPath([CallerFilePath] string testSourceFilePath = "")
    {
        // Este archivo vive en deploy/tests/TaxVision.Notification.Tests/. Tres niveles arriba es la
        // raíz del repo (deploy/tests/TaxVision.Notification.Tests -> deploy/tests -> deploy -> raíz).
        var testProjectDir = Path.GetDirectoryName(testSourceFilePath)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testProjectDir, "..", "..", ".."));
        return Path.Combine(
            repoRoot,
            "src",
            "Services",
            "Notification",
            "TaxVision.Notification.Api",
            "appsettings.json"
        );
    }

    private static Dictionary<string, string?> RequiredBootstrapConfig() =>
        new()
        {
            // AddNotificationInfrastructure exige esta clave (throw si falta) — solo un requisito
            // de arranque del método.
            ["ConnectionStrings:Default"] =
                "Server=(local);Database=TaxVisionNotificationTest;Trusted_Connection=True;",
        };

    private static void AssertLastRegisteredImplementation<TService, TExpectedImplementation>(
        IServiceCollection services
    )
        where TService : class
        where TExpectedImplementation : class, TService
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(TService));
        Assert.NotNull(descriptor);
        Assert.Equal(typeof(TExpectedImplementation), descriptor!.ImplementationType);
    }
}
