using System.Reflection;
using System.Text.RegularExpressions;
using BuildingBlocks.Messaging;
using Wolverine.Attributes;
using Wolverine.Util;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// BB-4. Cada contrato de <c>BuildingBlocks.Messaging</c> declara su nombre de cable con
/// <see cref="MessageIdentityAttribute"/>, para que mover o renombrar el tipo no cambie lo que viaja.
/// Los que no lo tenían reciben el nombre por defecto de Wolverine (el nombre completo del tipo), así
/// que el cable no cambia: <c>MessageWireNames.txt</c> guarda esos nombres tal como viajaban.
/// </summary>
public sealed class MessageIdentityTests
{
    private const string ClrStylePrefix = "BuildingBlocks.";

    private static readonly Assembly Messaging = typeof(IIntegrationEvent).Assembly;

    // Eventos de integración y los comandos que cruzan servicios.
    private static readonly Type[] Contracts = Messaging
        .GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } && (t.IsPublic || t.IsNestedPublic))
        .Where(t =>
            typeof(IIntegrationEvent).IsAssignableFrom(t)
            || t.Name.EndsWith("IntegrationEvent", StringComparison.Ordinal)
            || t.Name.EndsWith("Command", StringComparison.Ordinal)
        )
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void AllIntegrationEvents_HaveStableIdentity()
    {
        Assert.NotEmpty(Contracts);

        var missing = Contracts
            .Where(t => string.IsNullOrWhiteSpace(t.GetCustomAttribute<MessageIdentityAttribute>()?.Alias))
            .Select(t => t.FullName)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{missing.Length} contratos sin [MessageIdentity]. Usar el nombre que hoy viaja "
                + "(el nombre completo del tipo) o, si es nuevo, un alias 'servicio.evento.v1':\n  "
                + string.Join("\n  ", missing)
        );

        var duplicated = Contracts
            .GroupBy(t => t.ToMessageTypeName(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.Name))}")
            .ToArray();

        Assert.True(duplicated.Length == 0, "Nombres de cable repetidos:\n  " + string.Join("\n  ", duplicated));
    }

    [Fact]
    public void Identity_EqualsCurrentWireName()
    {
        var snapshot = WireNameSnapshot();
        var current = Contracts
            .GroupBy(t => t.ToMessageTypeName(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        // Todo nombre que viajaba sigue resolviendo a exactamente un tipo.
        var unresolved = snapshot.Where(n => current.GetValueOrDefault(n) != 1).ToArray();
        Assert.True(
            unresolved.Length == 0,
            "Estos nombres viajaban en el cable y ya no resuelven a un único tipo. Los mensajes en "
                + "colas, outbox y DLQ con ese nombre se perderían. Dejar el alias literal en el tipo:\n  "
                + string.Join("\n  ", unresolved)
        );

        // Un alias con forma de nombre CLR solo vale si es el nombre por defecto que ya viajaba.
        var invented = Contracts
            .Select(t => t.GetCustomAttribute<MessageIdentityAttribute>()?.Alias)
            .Where(a => a is not null && a.StartsWith(ClrStylePrefix, StringComparison.Ordinal))
            .Where(a => !snapshot.Contains(a!))
            .ToArray();
        Assert.True(
            invented.Length == 0,
            "Alias con forma de nombre CLR que nunca viajó (¿typo?). Para un contrato nuevo usar "
                + "'servicio.evento.v1':\n  "
                + string.Join("\n  ", invented)
        );
    }

    /// <summary>Sustento del snapshot: en Wolverine 6.14 el nombre por defecto es el nombre completo.</summary>
    [Fact]
    public void WolverineDefaultName_IsFullTypeName()
    {
        Assert.Equal(
            typeof(MessageIdentityUnnamedProbe).FullName,
            typeof(MessageIdentityUnnamedProbe).ToMessageTypeName()
        );
        Assert.Equal("probe.named.v1", typeof(MessageIdentityNamedProbe).ToMessageTypeName());
    }

    /// <summary>
    /// Communication (Node) traduce el header AMQP <c>type</c> con <c>CLR_TYPE_TO_EVENT_TYPE</c>. Cada
    /// entrada tiene que seguir resolviendo con el nombre que .NET pone en ese header.
    /// </summary>
    [Fact]
    public void NodeClrTypeMap_KeysStillMatchWireNames()
    {
        var mapFile = Path.Combine(
            RepositoryRoot().FullName,
            "src",
            "Services",
            "Communication",
            "src",
            "infrastructure",
            "rabbit",
            "consumer-runtime.ts"
        );
        Assert.True(File.Exists(mapFile), $"No se encontró {mapFile}.");

        var block = Regex.Match(
            File.ReadAllText(mapFile),
            @"CLR_TYPE_TO_EVENT_TYPE[^{]*\{(.*?)\n\};",
            RegexOptions.Singleline
        );
        Assert.True(block.Success, "No se pudo aislar el bloque de CLR_TYPE_TO_EVENT_TYPE.");

        var entries = Regex
            .Matches(block.Groups[1].Value, @"'(BuildingBlocks\.[A-Za-z0-9_.]+)'\s*:\s*'([^']+)'")
            .Select(m => (Clr: m.Groups[1].Value, EventType: m.Groups[2].Value))
            .ToArray();
        Assert.NotEmpty(entries);

        var wireByFullName = Contracts.ToDictionary(
            t => t.FullName!,
            t => t.ToMessageTypeName(),
            StringComparer.Ordinal
        );

        // Resuelve si el header trae la clave, o si ya trae el alias y Node lo usa tal cual.
        var broken = entries
            .Where(e => !wireByFullName.TryGetValue(e.Clr, out var wire) || (wire != e.Clr && wire != e.EventType))
            .Select(e => e.Clr)
            .ToArray();
        Assert.True(
            broken.Length == 0,
            "Claves de CLR_TYPE_TO_EVENT_TYPE que .NET ya no manda en el header 'type' "
                + "(Communication las descartaría en silencio):\n  "
                + string.Join("\n  ", broken)
        );
    }

    private static HashSet<string> WireNameSnapshot()
    {
        var path = Path.Combine(
            RepositoryRoot().FullName,
            "deploy",
            "tests",
            "TaxVision.BuildingBlocks.Tests",
            "Messaging",
            "MessageWireNames.txt"
        );
        Assert.True(File.Exists(path), $"No se encontró {path}.");

        var names = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(names);
        return names;
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }
}

// Tipos sueltos (no anidados), con la misma forma que los contratos reales.
internal sealed record MessageIdentityUnnamedProbe;

[MessageIdentity("probe.named.v1")]
internal sealed record MessageIdentityNamedProbe;
