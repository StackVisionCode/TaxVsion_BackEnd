using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization;

/// <summary>
/// A6 — el escalón del gate de módulo.
///
/// Encender el gate es visible para los tenants: un dato malo se paga con 403 en cosas que sí
/// pagaron. Por eso se sube módulo a módulo (primero `campaigns`, luego `email`, luego `comms`) y
/// no de golpe. Lo que se blinda acá es que el escalón **no se salte solo**: cualquier duda deja el
/// módulo en log-only, que es el lado que no rompe a nadie.
/// </summary>
public sealed class ModuleGateSettingsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Fact]
    public void Without_configuration_nothing_is_enforced()
    {
        // El default tiene que ser log-only: un servicio que no sepa de esto no empieza a denegar.
        Assert.False(ModuleGateSettings.ShouldEnforce(Config(), "campaigns"));
    }

    [Fact]
    public void Enforce_false_ignores_the_staged_list()
    {
        // El interruptor general manda: con él apagado, listar módulos no enciende nada.
        var config = Config(
            (ModuleGateSettings.EnforceKey, "false"),
            ($"{ModuleGateSettings.EnforcedModulesKey}:0", "campaigns")
        );

        Assert.False(ModuleGateSettings.ShouldEnforce(config, "campaigns"));
    }

    [Fact]
    public void Only_the_listed_module_is_enforced()
    {
        // El escalón real: `campaigns` deniega y todo lo demás sigue solo midiéndose.
        var config = Config(
            (ModuleGateSettings.EnforceKey, "true"),
            ($"{ModuleGateSettings.EnforcedModulesKey}:0", "campaigns")
        );

        Assert.True(ModuleGateSettings.ShouldEnforce(config, "campaigns"));
        Assert.False(ModuleGateSettings.ShouldEnforce(config, "comms"));
        Assert.False(ModuleGateSettings.ShouldEnforce(config, "documents"));
    }

    [Fact]
    public void Several_modules_can_share_a_step()
    {
        var config = Config(
            (ModuleGateSettings.EnforceKey, "true"),
            ($"{ModuleGateSettings.EnforcedModulesKey}:0", "campaigns"),
            ($"{ModuleGateSettings.EnforcedModulesKey}:1", "email")
        );

        Assert.True(ModuleGateSettings.ShouldEnforce(config, "campaigns"));
        Assert.True(ModuleGateSettings.ShouldEnforce(config, "email"));
        Assert.False(ModuleGateSettings.ShouldEnforce(config, "comms"));
    }

    [Fact]
    public void The_module_name_is_case_insensitive()
    {
        // La lista la escribe una persona en un appsettings; un "Campaigns" no puede dejar el gate
        // apagado en silencio.
        var config = Config(
            (ModuleGateSettings.EnforceKey, "true"),
            ($"{ModuleGateSettings.EnforcedModulesKey}:0", "Campaigns")
        );

        Assert.True(ModuleGateSettings.ShouldEnforce(config, "campaigns"));
    }

    [Fact]
    public void Enforce_without_a_list_applies_to_everything()
    {
        // El estado final, cuando ya se confía en todos los módulos: no obliga a enumerarlos.
        var config = Config((ModuleGateSettings.EnforceKey, "true"));

        Assert.True(ModuleGateSettings.ShouldEnforce(config, "campaigns"));
        Assert.True(ModuleGateSettings.ShouldEnforce(config, "comms"));
    }

    [Fact]
    public void An_empty_list_is_not_read_as_enforce_nothing()
    {
        // `"EnforcedModules": []` en un appsettings es ambiguo. Se interpreta como "sin escalón"
        // (igual que no ponerla), no como "ningún módulo": lo contrario apagaría el gate de un
        // despliegue final sin que nadie lo notara.
        var config = Config((ModuleGateSettings.EnforceKey, "true"), (ModuleGateSettings.EnforcedModulesKey, null));

        Assert.True(ModuleGateSettings.ShouldEnforce(config, "campaigns"));
    }

    // ---------- A6: una errata en la lista no puede apagar el gate en silencio ----------

    [Fact]
    public void An_unknown_module_while_enforcing_fails_at_startup()
    {
        // "campains" dejaría ese módulo en log-only y quien escribió la configuración creería que lo
        // encendió. Descubrirlo meses después —porque nunca denegó— es peor que no arrancar.
        var config = Config(
            (ModuleGateSettings.EnforceKey, "true"),
            ($"{ModuleGateSettings.EnforcedModulesKey}:0", "campains")
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ModuleGateSettings.ValidateOrThrow(config, NullLogger.Instance)
        );

        // El mensaje tiene que decir QUÉ nombre está mal y cuáles son los válidos.
        Assert.Contains("campains", ex.Message, StringComparison.Ordinal);
        Assert.Contains("campaigns", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_gate_off_an_unknown_module_only_warns()
    {
        // Apagado, la errata todavía no engaña a nadie: no vale tirar un servicio por eso.
        var config = Config(
            (ModuleGateSettings.EnforceKey, "false"),
            ($"{ModuleGateSettings.EnforcedModulesKey}:0", "campains")
        );

        ModuleGateSettings.ValidateOrThrow(config, NullLogger.Instance);
    }

    [Fact]
    public void A_correct_list_passes()
    {
        var config = Config(
            (ModuleGateSettings.EnforceKey, "true"),
            ($"{ModuleGateSettings.EnforcedModulesKey}:0", "campaigns"),
            ($"{ModuleGateSettings.EnforcedModulesKey}:1", "COMMS")
        );

        ModuleGateSettings.ValidateOrThrow(config, NullLogger.Instance);
    }

    [Fact]
    public void Every_module_of_the_commercial_catalog_is_known_by_the_map()
    {
        // Si Subscription vende un módulo que `PermissionModuleMap` no conoce, ese módulo NO gatea
        // nada aunque el plan lo cobre: el tenant paga por algo que nadie protege.
        // `marketing`, `builder`, `irs` y `miles` quedan fuera a propósito — son de frontend y no
        // tienen endpoints propios (está documentado en el mapa).
        //
        // `reports` está en el mapa pero NO en esta lista: su único permiso (`reports.view`) existe en
        // el catálogo y **ningún endpoint lo exige** (medido, 0 resultados), así que hoy se comporta
        // como los de frontend. Está en el mapa para el día que tenga endpoints; encender su escalón
        // ahora no gatearía nada. Candidato a `IsReserved` (A3.5).
        string[] soldWithEndpoints = ["customers", "signatures", "documents", "planner", "email", "comms", "campaigns"];

        foreach (var module in soldWithEndpoints)
            Assert.Contains(module, PermissionModuleMap.KnownModules);

        // El mapa sigue conociéndolo, para que su escalón se pueda configurar sin tocar código.
        Assert.Contains("reports", PermissionModuleMap.KnownModules);
    }
}
