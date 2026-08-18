using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// H-12. Publicar un <c>IIntegrationEvent</c> sin ruta registrada <b>no es un error en Wolverine</b>:
/// el mensaje se entrega en proceso (local) y jamás sale a RabbitMQ. Sin excepción, sin log, sin
/// métrica. El handler devuelve <c>Task.CompletedTask</c> con normalidad.
///
/// <para>
/// Ese fallo silencioso ya mordió cuatro veces en este repositorio: 3 eventos de CloudStorage, 12
/// detectados de una sentada auditando tras un onboarding E2E, el evento de ToS/Privacy en Auth, y
/// 8 en PaymentClient encontrados el día que se escribió este test. Las cuatro se descubrieron por
/// accidente, buscando en el consumidor un bug que estaba en el productor.
/// </para>
///
/// <para>
/// El test compara, por servicio, lo que el código <b>publica</b> contra lo que <c>Program.cs</c>
/// <b>rutea</b>. Se lee el fuente en vez de usar reflexión porque las rutas se declaran dentro de
/// un lambda de <c>AddWolverine</c> que solo existe en runtime con toda la infraestructura viva.
/// </para>
/// </summary>
public sealed class EventRoutingContractTests(ITestOutputHelper output)
{
    /// <summary>
    /// Eventos que se publican a propósito sin salir a RabbitMQ. Cada entrada necesita su porqué:
    /// añadir una excepción tiene que costar un poco, para que nadie la use como escape rápido.
    /// </summary>
    private static readonly Dictionary<string, string> DeliberatelyLocal = new(StringComparer.Ordinal)
    {
        // (vacío — si algún evento pasa a ser local a propósito, documentar aquí el motivo)
    };

    /// <summary>
    /// <b>Estos NO son excepciones de diseño: son el bug que este test existe para cazar.</b> Están
    /// aquí para que el guardrail proteja de casos <i>nuevos</i> sin bloquear el build por código de
    /// otro propietario.
    ///
    /// <para>
    /// Los 8 aparecieron el día que se escribió el test — la cuarta vez que H-12 muerde en este
    /// repositorio. Ninguno tiene consumidores hoy (verificado en los 17 .NET y los 2 Node), así que
    /// no hay pérdida de datos en curso; lo que hay es una fachada: el handler parece notificar al
    /// resto del sistema y no notifica a nadie. Se decide con el dueño de PaymentClient si se rutean
    /// o si se borran por superficie muerta. <b>Al resolverlo, borrar la entrada de aquí.</b>
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> KnownUnroutedPendingOwner = new(StringComparer.Ordinal)
    {
        ["PaymentLinkCreatedIntegrationEvent"] = "PaymentClient — sin consumidores; pendiente del dueño del servicio",
        ["PaymentLinkExpiredIntegrationEvent"] = "PaymentClient — idem",
        ["PaymentLinkUsedIntegrationEvent"] = "PaymentClient — idem",
        ["PayoutCompletedIntegrationEvent"] = "PaymentClient — idem",
        ["PayoutFailedIntegrationEvent"] = "PaymentClient — idem",
        ["TenantConnectAccountEnabledIntegrationEvent"] = "PaymentClient — idem",
        ["TenantConnectAccountOnboardingRequiredIntegrationEvent"] = "PaymentClient — idem",
        ["TenantConnectAccountRestrictedIntegrationEvent"] = "PaymentClient — idem",
    };

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static IEnumerable<FileInfo> SourceFiles(DirectoryInfo service) =>
        service
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    // PublishAsync(new XxxIntegrationEvent(...)) — el 87% de los call sites del repo.
    private static readonly Regex PublishedInline = new(
        @"(?:PublishAsync|SendAsync)\s*(?:<[^>]*>)?\s*\(\s*new\s+([A-Za-z_][\w]*IntegrationEvent)\b",
        RegexOptions.Compiled
    );

    // PublishAsync(evt) — hay que resolver la variable a su construcción en el mismo archivo.
    private static readonly Regex PublishedByVariable = new(
        @"(?:PublishAsync|SendAsync)\s*(?:<[^>]*>)?\s*\(\s*([a-z_][\w]*)\s*[,)]",
        RegexOptions.Compiled
    );

    // El tipo puede venir completamente cualificado: PublishMessage<BuildingBlocks.Messaging.X.YEvent>()
    private static readonly Regex Routed = new(
        @"PublishMessage\s*<\s*(?:[\w.]*\.)?([A-Za-z_][\w]*IntegrationEvent)\s*>",
        RegexOptions.Compiled
    );

    private static (HashSet<string> Published, Dictionary<string, string> Where) PublishedEvents(DirectoryInfo service)
    {
        HashSet<string> published = new(StringComparer.Ordinal);
        Dictionary<string, string> where = new(StringComparer.Ordinal);

        foreach (var file in SourceFiles(service))
        {
            var text = File.ReadAllText(file.FullName);

            foreach (Match m in PublishedInline.Matches(text))
            {
                published.Add(m.Groups[1].Value);
                where.TryAdd(m.Groups[1].Value, file.Name);
            }

            foreach (Match m in PublishedByVariable.Matches(text))
            {
                // `var evt = new XxxIntegrationEvent(` o `XxxIntegrationEvent evt = new(`
                var variable = Regex.Escape(m.Groups[1].Value);
                var declaration = Regex.Match(
                    text,
                    $@"(?:var\s+{variable}\s*=\s*new\s+([A-Za-z_][\w]*IntegrationEvent)\b"
                        + $@"|([A-Za-z_][\w]*IntegrationEvent)\s+{variable}\s*=)"
                );

                if (!declaration.Success)
                    continue;

                var name = declaration.Groups[1].Success ? declaration.Groups[1].Value : declaration.Groups[2].Value;

                published.Add(name);
                where.TryAdd(name, file.Name);
            }
        }

        return (published, where);
    }

    private static HashSet<string> RoutedEvents(DirectoryInfo service)
    {
        HashSet<string> routed = new(StringComparer.Ordinal);
        foreach (var file in SourceFiles(service).Where(f => f.Name == "Program.cs"))
        {
            foreach (Match m in Routed.Matches(File.ReadAllText(file.FullName)))
                routed.Add(m.Groups[1].Value);
        }
        return routed;
    }

    [Fact]
    public void TodoEventoPublicadoTieneRutaAlExchange()
    {
        var services = RepositoryRoot()
            .GetDirectories(Path.Combine("src", "Services"))
            .SelectMany(d => d.GetDirectories())
            .ToArray();

        Assert.NotEmpty(services);

        List<string> offenders = [];
        foreach (var service in services.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            var (published, where) = PublishedEvents(service);
            var routed = RoutedEvents(service);

            var missing = published
                .Except(routed)
                .Where(e => !DeliberatelyLocal.ContainsKey(e))
                .Where(e => !KnownUnroutedPendingOwner.ContainsKey(e))
                .OrderBy(e => e, StringComparer.Ordinal)
                .ToArray();

            foreach (var e in missing)
                offenders.Add($"{service.Name}: {e} (publicado en {where.GetValueOrDefault(e, "?")})");
        }

        // Que la deuda conocida siga siendo visible en cada corrida, no enterrada en un diccionario.
        if (KnownUnroutedPendingOwner.Count > 0)
        {
            output.WriteLine(
                $"H-12 — {KnownUnroutedPendingOwner.Count} eventos sin ruta ya conocidos, pendientes "
                    + "de decisión del dueño del servicio (no bloquean el build):"
            );
            foreach (var (name, why) in KnownUnroutedPendingOwner.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                output.WriteLine($"  {name} — {why}");
        }

        Assert.True(
            offenders.Count == 0,
            "Eventos publicados sin PublishMessage<T>() en su Program.cs. Wolverine los entrega en "
                + "proceso y NUNCA salen a RabbitMQ — ningún otro servicio se entera, y el fallo es "
                + "silencioso:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    /// <summary>
    /// H-20 Fase 20.1 — no cambia ningún binding, solo hace visible el contrato de suscripción que
    /// hoy está implícito. Con el fanout puro las 17 colas reciben todo, así que el binding no
    /// documenta nada: sin esto nadie puede responder "¿quién escucha este evento?" sin grep.
    /// </summary>
    [Fact]
    public void MapaDeConsumoPorServicio()
    {
        var handler = new Regex(
            @"(?:Handle|Consume)\s*\(\s*(?:this\s+)?(?:[\w.]*\.)?([A-Za-z_][\w]*IntegrationEvent)\b",
            RegexOptions.Compiled
        );

        var services = RepositoryRoot()
            .GetDirectories(Path.Combine("src", "Services"))
            .SelectMany(d => d.GetDirectories())
            .OrderBy(d => d.Name, StringComparer.Ordinal);

        Dictionary<string, SortedSet<string>> consumers = new(StringComparer.Ordinal);
        foreach (var service in services)
        {
            SortedSet<string> handled = new(StringComparer.Ordinal);
            foreach (var file in SourceFiles(service))
            {
                foreach (Match m in handler.Matches(File.ReadAllText(file.FullName)))
                    handled.Add(m.Groups[1].Value);
            }
            if (handled.Count > 0)
                consumers[service.Name] = handled;
        }

        output.WriteLine("Quién consume qué (fuente para el binding de un topic exchange, H-20):");
        foreach (var (service, events) in consumers.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            output.WriteLine($"  {service} ({events.Count}): {string.Join(", ", events)}");

        Assert.NotEmpty(consumers);
    }
}
