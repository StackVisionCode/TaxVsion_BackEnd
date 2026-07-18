using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Email.Sending;
using TaxVision.Notification.Infrastructure;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Hardening Fase 21 (2026-07-18) — cierra un hueco de cobertura real: antes de esta fase ningún test
/// ejercitaba <c>AddNotificationInfrastructure</c> tal como lo ve el proceso real. Todos los tests
/// existentes de <see cref="EventBasedEmailDispatchGateway"/>/<see cref="PostmasterEmailDeliveryService"/>
/// (y de sus contrapartes <see cref="InProcessEmailDispatchGateway"/>/<see cref="EmailDeliveryService"/>,
/// esta última sin test propio) construían la clase directo — nunca pasaban por el <c>if/else</c> de
/// resolución de DI que decide cuál implementación gana según <c>Notification:UsePostmasterDispatch</c>.
/// </summary>
/// <remarks>
/// Se inspeccionan los <see cref="ServiceDescriptor"/> registrados en vez de construir el
/// <see cref="IServiceProvider"/> completo y resolver: <c>AddNotificationInfrastructure</c> registra
/// dependencias (repositorios, <c>NotificationDbContext</c>) que solo se pueden resolver end-to-end
/// contra una conexión SQL Server real, algo fuera de alcance de un test unitario de "qué implementación
/// se registró". Inspeccionar <see cref="ServiceDescriptor.ImplementationType"/> alcanza para probar la
/// decisión sin acoplar el test a infraestructura real.
/// </remarks>
public sealed class NotificationDispatchDefaultRegistrationTests
{
    /// <summary>
    /// El test que prueba el default REAL, no un default hardcodeado a mano en el test: carga el
    /// <c>appsettings.json</c> que de verdad se despliega con <c>TaxVision.Notification.Api</c> — el
    /// mismo archivo que la Fase 21 editó para fijar <c>"UsePostmasterDispatch": true</c> — y confirma
    /// que, con esa configuración shippeada, `AddNotificationInfrastructure` registra los dos paths
    /// basados en Postmaster. Si alguien revierte el valor en el JSON, este test lo detecta sin que
    /// nadie tenga que acordarse de mantenerlo sincronizado a mano.
    /// </summary>
    [Fact]
    public void Shipped_Notification_Api_appsettings_json_defaults_both_dispatch_paths_to_Postmaster()
    {
        var appsettingsPath = GetShippedNotificationApiAppSettingsPath();
        Assert.True(File.Exists(appsettingsPath), $"No se encontró appsettings.json en '{appsettingsPath}'.");

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appsettingsPath, optional: false)
            .AddInMemoryCollection(RequiredBootstrapConfig())
            .Build();

        // Sanity check explícito antes de mirar DI: si esto falla, el JSON shippeado cambió el default,
        // no el código de este test.
        Assert.True(configuration.GetValue<bool>("Notification:UsePostmasterDispatch"));

        var services = new ServiceCollection();
        services.AddNotificationInfrastructure(configuration);

        AssertLastRegisteredImplementation<IEmailDispatchGateway, EventBasedEmailDispatchGateway>(services);
        AssertLastRegisteredImplementation<IEmailDeliveryService, PostmasterEmailDeliveryService>(services);
    }

    [Fact]
    public void AddNotificationInfrastructure_with_flag_explicitly_true_registers_Postmaster_based_paths()
    {
        var services = new ServiceCollection();
        var configuration = BuildInMemoryConfiguration(usePostmasterDispatch: "true");

        services.AddNotificationInfrastructure(configuration);

        AssertLastRegisteredImplementation<IEmailDispatchGateway, EventBasedEmailDispatchGateway>(services);
        AssertLastRegisteredImplementation<IEmailDeliveryService, PostmasterEmailDeliveryService>(services);
    }

    /// <summary>
    /// El rollback operacional (ver DependencyInjection.cs y README §28.1) sigue vivo: overridear el
    /// flag a `false` explícitamente tiene que seguir cayendo a los paths in-process/SMTP-directo
    /// originales — este test es la garantía de que la Fase 21 no rompió esa vía de escape.
    /// </summary>
    [Fact]
    public void AddNotificationInfrastructure_with_flag_explicitly_false_falls_back_to_InProcess_paths()
    {
        var services = new ServiceCollection();
        var configuration = BuildInMemoryConfiguration(usePostmasterDispatch: "false");

        services.AddNotificationInfrastructure(configuration);

        AssertLastRegisteredImplementation<IEmailDispatchGateway, InProcessEmailDispatchGateway>(services);
        AssertLastRegisteredImplementation<IEmailDeliveryService, EmailDeliveryService>(services);
    }

    /// <summary>
    /// Caso degenerado, distinto del default real de la aplicación: si a <c>AddNotificationInfrastructure</c>
    /// se le pasa un <see cref="IConfiguration"/> que NO tiene la clave en absoluto (ni siquiera vía
    /// <c>appsettings.json</c>), <c>GetValue&lt;bool&gt;</c> resuelve al default de C#, <c>false</c> — tal
    /// como documenta el comentario en <c>DependencyInjection.cs</c>. Es exactamente por esto que el
    /// default real de la app no depende de este fallback: está fijado explícitamente en
    /// <c>appsettings.json</c> (ver el primer test de esta clase) y en <c>docker-compose.yml</c>.
    /// </summary>
    [Fact]
    public void AddNotificationInfrastructure_with_key_entirely_absent_falls_back_to_InProcess_paths()
    {
        var services = new ServiceCollection();
        var configuration = BuildInMemoryConfiguration(usePostmasterDispatch: null);

        services.AddNotificationInfrastructure(configuration);

        AssertLastRegisteredImplementation<IEmailDispatchGateway, InProcessEmailDispatchGateway>(services);
        AssertLastRegisteredImplementation<IEmailDeliveryService, EmailDeliveryService>(services);
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
            // AddNotificationInfrastructure exige esta clave (throw si falta) — no relacionada con el
            // flag bajo prueba, solo un requisito de arranque del método.
            ["ConnectionStrings:Default"] =
                "Server=(local);Database=TaxVisionNotificationTest;Trusted_Connection=True;",
        };

    private static IConfiguration BuildInMemoryConfiguration(string? usePostmasterDispatch)
    {
        var values = RequiredBootstrapConfig();
        if (usePostmasterDispatch is not null)
        {
            values["Notification:UsePostmasterDispatch"] = usePostmasterDispatch;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

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
