using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BuildingBlocks.Messaging;
using Wolverine.Attributes;
using Xunit;
using Xunit.Abstractions;

namespace TaxVision.BuildingBlocks.Tests.Messaging;

/// <summary>
/// H-13 y H-14. El bus cruza dos runtimes y el puente son <b>strings</b> que nadie verificaba.
///
/// <para>
/// <b>Sentido Node → .NET (H-13).</b> Node publica con <c>type: 'communication.call.recording_ready.v1'</c>
/// y <b>sin</b> la cabecera <c>dotnet-type-name</c>. Wolverine resuelve el tipo CLR gracias a
/// <see cref="MessageIdentityAttribute"/> en el record. Si ese string y el que emite Node dejan de
/// coincidir por un typo, Wolverine no resuelve el tipo, no hay handler, y el mensaje <b>se descarta
/// en silencio</b> — sin log ni excepción. Verificado contra el broker real: el descarte silencioso
/// es el comportamiento por defecto de Wolverine 6.14 ante un mensaje sin handler.
/// </para>
///
/// <para>
/// <b>Sentido .NET → Node (H-14).</b> Node no puede leer tipos CLR, así que traduce la propiedad AMQP
/// <c>type</c> (<c>properties.type</c>: el alias de <see cref="MessageIdentityAttribute"/> o el nombre
/// CLR) con el diccionario manual <c>CLR_TYPE_TO_EVENT_TYPE</c> (<c>consumer-runtime.ts</c>). Sus claves son <b>namespaces CLR completos</b>: un refactor
/// inofensivo en .NET —mover un evento de carpeta, renombrar un namespace— rompe Node sin que la
/// solución .NET se entere. Compila, los tests pasan, y Communication deja de recibir eventos.
/// Esto es lo que conecta con BB-12: al unificar namespaces en BuildingBlocks nada obligaba a
/// actualizar este mapa.
/// </para>
///
/// <para>
/// El enunciado original de H-14 decía que el mapa estaba duplicado en dos archivos. <b>No lo
/// está</b>: hay una sola declaración; en <c>publisher.ts</c> el nombre solo aparece en un
/// doc-comment que explica que no lo necesita, porque lo que Node publica lleva <c>eventType</c> en
/// el body. Por eso aquí no hay nada que deduplicar — solo que vigilar.
/// </para>
/// </summary>
public sealed class NodeInteropContractTests(ITestOutputHelper output)
{
    /// <summary>Prefijos que identifican un evento del puente con Node.</summary>
    private static readonly string[] NodeOwnedPrefixes = ["communication.", "transcript."];

    /// <summary>Identidades fuera de esos prefijos que también publica Node (acuse del retiro).</summary>
    private static readonly string[] NodeOwnedAliases = ["auth.user.offboarding_step_completed.v1"];

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static IEnumerable<FileInfo> NodeSources()
    {
        var root = RepositoryRoot();
        foreach (var svc in new[] { "Communication", "CommunicationTranscriptWorker", "CommunicationRecorder" })
        {
            var src = new DirectoryInfo(Path.Combine(root.FullName, "src", "Services", svc, "src"));
            if (!src.Exists)
                continue;

            foreach (var f in src.EnumerateFiles("*.ts", SearchOption.AllDirectories))
                yield return f;
        }
    }

    /// <summary>
    /// H-13 — cada identidad del puente tiene que existir tal cual en el código de Node. Se lee el
    /// fuente TypeScript porque no hay forma de preguntárselo al runtime de Node desde aquí.
    /// </summary>
    [Fact]
    public void CadaMessageIdentityDelPuenteLaEmiteNode()
    {
        var bridged = typeof(IntegrationEvent)
            .Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<MessageIdentityAttribute>()?.Alias)
            .Where(alias => alias is not null)
            .Select(alias => alias!)
            .Where(alias =>
                NodeOwnedPrefixes.Any(p => alias.StartsWith(p, StringComparison.Ordinal))
                || NodeOwnedAliases.Contains(alias, StringComparer.Ordinal)
            )
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(bridged);

        var nodeText = string.Concat(NodeSources().Select(f => File.ReadAllText(f.FullName)));

        var missing = bridged
            .Where(alias =>
                !nodeText.Contains($"'{alias}'", StringComparison.Ordinal)
                && !nodeText.Contains($"\"{alias}\"", StringComparison.Ordinal)
            )
            .ToArray();

        output.WriteLine($"H-13 — identidades del puente Node→.NET verificadas: {bridged.Length}");
        foreach (var a in bridged)
            output.WriteLine($"  {a}");

        Assert.True(
            missing.Length == 0,
            "Estos [MessageIdentity] declaran un alias que Node NO emite en ningún sitio: "
                + string.Join(", ", missing)
                + ". Wolverine no resolverá el tipo, no habrá handler, y el mensaje se descarta en "
                + "SILENCIO. Alinear el string en ambos lados."
        );
    }

    /// <summary>
    /// H-14 — cada clave del mapa de Node tiene que ser un tipo CLR que exista de verdad. Es lo que
    /// convierte un renombrado de namespace en un build rojo en vez de en Communication mudo.
    /// </summary>
    [Fact]
    public void CadaClaveDelMapaDeNodeApuntaAUnTipoQueExiste()
    {
        var mapFile = new FileInfo(
            Path.Combine(
                RepositoryRoot().FullName,
                "src",
                "Services",
                "Communication",
                "src",
                "infrastructure",
                "rabbit",
                "consumer-runtime.ts"
            )
        );

        Assert.True(
            mapFile.Exists,
            $"No se encontró {mapFile.FullName}. Si el archivo se movió, actualizar este test."
        );

        var text = File.ReadAllText(mapFile.FullName);
        var block = Regex.Match(text, @"CLR_TYPE_TO_EVENT_TYPE[^{]*\{(.*?)\n\};", RegexOptions.Singleline);
        Assert.True(block.Success, "No se pudo aislar el bloque de CLR_TYPE_TO_EVENT_TYPE.");

        var keys = Regex
            .Matches(block.Groups[1].Value, @"'(BuildingBlocks\.[A-Za-z0-9_.]+)'\s*:")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(keys);

        var known = typeof(IntegrationEvent)
            .Assembly.GetTypes()
            .Select(t => t.FullName)
            .Where(n => n is not null)
            .ToHashSet(StringComparer.Ordinal)!;

        var dangling = keys.Where(k => !known.Contains(k)).ToArray();

        output.WriteLine($"H-14 — claves del mapa de Node verificadas contra tipos CLR reales: {keys.Length}");

        Assert.True(
            dangling.Length == 0,
            "El mapa CLR_TYPE_TO_EVENT_TYPE de Node referencia tipos que ya NO existen en "
                + "BuildingBlocks:\n  "
                + string.Join("\n  ", dangling)
                + "\nEsos eventos llegan a Communication y no matchean: se descartan en silencio. "
                + "Suele pasar tras mover un evento de carpeta o renombrar un namespace."
        );
    }

    /// <summary>
    /// Cada evento que .NET publica al fanout <c>taxvision-events</c> llega a la cola de Communication.
    /// Si Node no tiene handler ni lo lista como ignorado a propósito, cae en «event type has no
    /// registered handler»: ruido en el log o, peor, un handler que falta (la cancelación de citas y el
    /// cliente restaurado se perdían así). La prueba obliga a decidir cada evento nuevo.
    /// </summary>
    [Fact]
    public void CadaEventoDelFanoutTieneHandlerEnNodeOEstaIgnorado()
    {
        var root = RepositoryRoot().FullName;
        var communication = Path.Combine(root, "src", "Services", "Communication", "src");
        var rabbit = Path.Combine(communication, "infrastructure", "rabbit");
        var clrMap = ReadClrMap(Path.Combine(rabbit, "consumer-runtime.ts"));
        var ignored = ReadIgnored(Path.Combine(rabbit, "ignored-event-types.ts"));
        var registered = ReadRegistered(communication);
        var published = FanoutEventTypes(root);

        Assert.NotEmpty(published);
        Assert.NotEmpty(registered);

        var uncovered = new List<string>();
        foreach (var type in published)
        {
            var amqpType = type.GetCustomAttribute<MessageIdentityAttribute>()?.Alias ?? type.FullName!;
            // Mismo orden que normalizeEnvelope: eventType del cuerpo, mapa CLR y el header tal cual.
            var nodeType = BodyEventType(type) ?? clrMap.GetValueOrDefault(amqpType) ?? amqpType;
            if (registered.Contains(nodeType) || ignored.Contains(nodeType) || ignored.Contains(amqpType))
                continue;

            uncovered.Add(nodeType == amqpType ? nodeType : $"{nodeType} (type: {amqpType})");
        }

        output.WriteLine($"Eventos .NET del fanout verificados contra Node: {published.Count}");

        Assert.True(
            uncovered.Count == 0,
            "Estos eventos que .NET publica a taxvision-events no tienen handler en Communication ni están "
                + "en ignored-event-types.ts:\n  "
                + string.Join("\n  ", uncovered.Distinct().Order(StringComparer.Ordinal))
                + "\nAgregar el handler (y su entrada en CLR_TYPE_TO_EVENT_TYPE si viaja con nombre CLR) o "
                + "listarlo como ignorado a propósito."
        );
    }

    /// <summary>Tipos de <c>PublishMessage&lt;T&gt;().ToRabbitExchange("taxvision-events")</c> en los servicios .NET.</summary>
    private static List<Type> FanoutEventTypes(string root)
    {
        var messaging = typeof(IntegrationEvent)
            .Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, FullName: not null })
            .ToArray();
        var pattern = new Regex(
            @"PublishMessage<\s*([A-Za-z0-9_.]+)\s*>\s*\(\s*\)\s*\.ToRabbitExchange\(\s*(?:""taxvision-events""|EventsExchange)\s*\)"
        );
        var separator = Path.DirectorySeparatorChar;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            var file in Directory.EnumerateFiles(
                Path.Combine(root, "src", "Services"),
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            if (file.Contains($"{separator}obj{separator}") || file.Contains($"{separator}bin{separator}"))
                continue;

            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
                names.Add(m.Groups[1].Value);
        }

        var unresolved = new List<string>();
        var types = new List<Type>();
        foreach (var name in names)
        {
            var matches = name.Contains('.')
                ? messaging.Where(t => t.FullName == name || t.FullName!.EndsWith("." + name, StringComparison.Ordinal))
                : messaging.Where(t => t.Name == name);
            var found = matches.ToArray();
            if (found.Length == 0)
                unresolved.Add(name);
            types.AddRange(found);
        }

        Assert.True(
            unresolved.Count == 0,
            "Estos eventos se publican al fanout pero no viven en BuildingBlocks.Messaging: "
                + string.Join(", ", unresolved)
                + ". Mover el contrato a BuildingBlocks o ajustar esta prueba."
        );
        return types.Distinct().ToList();
    }

    /// <summary>El <c>eventType</c> que Wolverine serializa en el cuerpo (Billing, Growth, Documents…), si lo hay.</summary>
    private static string? BodyEventType(Type type)
    {
        var property = type.GetProperty("EventType", BindingFlags.Public | BindingFlags.Instance);
        if (property?.PropertyType != typeof(string))
            return null;

        return property.GetValue(RuntimeHelpers.GetUninitializedObject(type)) as string;
    }

    private static Dictionary<string, string> ReadClrMap(string path)
    {
        var block = Regex.Match(
            File.ReadAllText(path),
            @"CLR_TYPE_TO_EVENT_TYPE[^{]*\{(.*?)\n\};",
            RegexOptions.Singleline
        );
        Assert.True(block.Success, "No se pudo aislar el bloque de CLR_TYPE_TO_EVENT_TYPE.");
        return Regex
            .Matches(block.Groups[1].Value, @"'([^']+)'\s*:\s*'([^']+)'")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
    }

    /// <summary>Todos los literales del archivo de ignorados, sin las líneas de comentario.</summary>
    private static HashSet<string> ReadIgnored(string path)
    {
        Assert.True(File.Exists(path), $"No se encontró {path}. Si el archivo se movió, actualizar este test.");
        var code = string.Join(
            "\n",
            File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
        );
        return Regex.Matches(code, @"'([^']+)'").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Tipos registrados con <c>register('…')</c> o <c>registerProjection('…')</c> en los event-handlers, como
    /// literal o como constante <c>Objeto.Clave</c> definida en el código de Communication.
    /// </summary>
    private static HashSet<string> ReadRegistered(string communicationSrc)
    {
        var allText = string.Concat(
            Directory.EnumerateFiles(communicationSrc, "*.ts", SearchOption.AllDirectories).Select(File.ReadAllText)
        );
        var registered = new HashSet<string>(StringComparer.Ordinal);
        var handlers = Path.Combine(communicationSrc, "application", "event-handlers");
        foreach (var file in Directory.EnumerateFiles(handlers, "*.ts"))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\bregister(?:Projection)?\(\s*'([^']+)'"))
                registered.Add(m.Groups[1].Value);

            foreach (
                Match m in Regex.Matches(text, @"\bregister(?:Projection)?\(\s*([A-Z][A-Za-z0-9_]*)\.([A-Za-z0-9_]+)\b")
            )
            {
                var obj = Regex.Match(allText, $@"\b{m.Groups[1].Value}\s*=\s*\{{(.*?)\}}", RegexOptions.Singleline);
                if (!obj.Success)
                    continue;

                var value = Regex.Match(obj.Groups[1].Value, $@"\b{m.Groups[2].Value}\s*:\s*'([^']+)'");
                if (value.Success)
                    registered.Add(value.Groups[1].Value);
            }
        }

        return registered;
    }
}
