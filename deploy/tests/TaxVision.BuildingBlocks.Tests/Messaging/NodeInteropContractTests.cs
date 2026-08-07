using System.Reflection;
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
/// <b>Sentido .NET → Node (H-14).</b> Node no puede leer tipos CLR, así que traduce la cabecera
/// <c>dotnet-type-name</c> con el diccionario manual <c>CLR_TYPE_TO_EVENT_TYPE</c>
/// (<c>consumer-runtime.ts</c>). Sus claves son <b>namespaces CLR completos</b>: un refactor
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
        foreach (var svc in new[] { "Communication", "CommunicationTranscriptWorker" })
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
            .Where(alias => NodeOwnedPrefixes.Any(p => alias.StartsWith(p, StringComparison.Ordinal)))
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
}
