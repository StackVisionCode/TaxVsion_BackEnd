using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// A6 — el gate de módulo se sube escalón por escalón mediante configuración, y el plan cuenta con eso
/// como mecanismo de rollback ("<c>Enforce=false</c> por servicio, sin redespliegue de código"). Eso solo
/// es cierto si el flag está cableado en los DOS lados del control plane de producción: referenciado en
/// <c>docker-compose.yml</c> como <c>${VAR:-false}</c> y escrito por <c>deploy.yml</c> desde el secret
/// homónimo. Si falta cualquiera de los dos, poner el secret en "true" no tiene ningún efecto — y lo
/// que se cree encendido sigue en log-only. Mismo contrato que
/// <see cref="AssignmentVisibilityDeployWiringTests"/>.
/// </summary>
public sealed class ModuleGateDeployWiringTests
{
    /// <summary>Un flag por servicio con gate: 13 .NET + Communication (Node).</summary>
    private static readonly string[] ModuleGateEnvVars =
    [
        "CAMPAIGNS_MODULE_GATE_ENFORCE",
        "CONNECTORS_MODULE_GATE_ENFORCE",
        "CORRESPONDENCE_MODULE_GATE_ENFORCE",
        "POSTMASTER_MODULE_GATE_ENFORCE",
        "COMMUNICATION_MODULE_GATE_ENFORCE",
        "CUSTOMER_MODULE_GATE_ENFORCE",
        "SIGNATURE_MODULE_GATE_ENFORCE",
        "CLOUDSTORAGE_MODULE_GATE_ENFORCE",
        "DOCUMENTS_MODULE_GATE_ENFORCE",
        "SCRIBE_MODULE_GATE_ENFORCE",
        "CALENDAR_MODULE_GATE_ENFORCE",
        "NOTES_MODULE_GATE_ENFORCE",
        "REMINDER_MODULE_GATE_ENFORCE",
        "TASKS_MODULE_GATE_ENFORCE",
    ];

    [Theory]
    [MemberData(nameof(ModuleGateVars))]
    public void Module_gate_flag_is_mapped_in_compose(string varName)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Matches(new Regex(@"\$\{" + Regex.Escape(varName) + @"[:}]"), compose);
    }

    [Theory]
    [MemberData(nameof(ModuleGateVars))]
    public void Module_gate_flag_defaults_to_false_in_compose(string varName)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        // El default es lo que protege un despliegue que no quiso tocar el gate: sin secret, log-only.
        Assert.Contains("${" + varName + ":-false}", compose);
    }

    [Theory]
    [MemberData(nameof(ModuleGateVars))]
    public void Module_gate_flag_is_passed_by_deploy_workflow(string varName)
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        Assert.Contains($"{varName}=${{{{ secrets.{varName} }}}}", workflow);
    }

    [Fact]
    public void The_staged_module_list_is_never_a_variable()
    {
        // El grado de libertad tiene que ser SOLO el booleano. `Enforce: true` con la lista vacía hace
        // que el gate aplique TODOS los módulos de golpe, así que una lista tomada de un secret (que
        // puede llegar vacío) saltaría el escalón completo sin que nadie lo note. Por eso en el compose
        // la lista va literal, servicio por servicio.
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        var interpolated = Regex
            .Matches(
                compose,
                @"(Authorization__ModuleGate__EnforcedModules__\d+|COMMUNICATION_MODULE_GATE_ENFORCED_MODULES):\s*(?<value>.+)"
            )
            .Select(match => match.Groups["value"].Value.Trim())
            .Where(value => value.Contains('$', StringComparison.Ordinal))
            .ToList();

        Assert.True(
            interpolated.Count == 0,
            "EnforcedModules debe ser literal en el compose, no una variable: " + string.Join(", ", interpolated)
        );
    }

    [Fact]
    public void Every_service_with_the_gate_lists_at_least_one_module()
    {
        // Un servicio con `Enforce` cableado pero sin lista aplicaría TODOS los módulos en cuanto se
        // encendiera su secret — el estado final, no un escalón.
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        // Node tiene su propio par de variables (emite permisos de `comms` y de `meetings`), con la
        // lista igual de literal y por el mismo motivo.
        Assert.Contains("COMMUNICATION_MODULE_GATE_ENFORCED_MODULES:", compose, StringComparison.Ordinal);

        var enforceCount = Regex.Matches(compose, @"Authorization__ModuleGate__Enforce:").Count;
        var moduleCount = Regex.Matches(compose, @"Authorization__ModuleGate__EnforcedModules__\d+:").Count;

        Assert.Equal(13, enforceCount);
        Assert.True(
            moduleCount >= enforceCount,
            $"Hay {enforceCount} servicios con Enforce y solo {moduleCount} entradas de módulo."
        );
    }

    public static IEnumerable<object[]> ModuleGateVars() => ModuleGateEnvVars.Select(v => new object[] { v });

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' walking up from {AppContext.BaseDirectory}."
        );
    }
}
