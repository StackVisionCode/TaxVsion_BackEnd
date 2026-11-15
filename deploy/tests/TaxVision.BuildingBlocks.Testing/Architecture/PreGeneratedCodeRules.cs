using System.Reflection;
using System.Text.RegularExpressions;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;
using Xunit;

namespace TaxVision.BuildingBlocks.Testing.Architecture;

/// <summary>
/// Guardarraíl del código pregenerado de Wolverine. En producción el host corre en Static: si falta el tipo de
/// un handler, el primer mensaje de ese tipo revienta (6.14 no lo valida al arrancar). Cada servicio llama a
/// estas reglas con su host de prueba sin infraestructura. Con <c>TAXVISION_CODEGEN_WRITE=1</c> la regla de
/// frescura reescribe el código versionado en vez de compararlo (así se regenera).
/// </summary>
public static class PreGeneratedCodeRules
{
    /// <summary>Variable que pone la regla de frescura en modo escritura.</summary>
    public const string WriteVariable = "TAXVISION_CODEGEN_WRITE";

    /// <summary>Carpeta del código generado dentro del Api, relativa a la raíz del repo.</summary>
    public static string GeneratedDirectory(string apiProjectDirectory) =>
        Path.Combine(RepositoryRoot(), apiProjectDirectory, "Internal", "Generated");

    /// <summary>En Static, todos los handlers salen de tipos ya compilados en el Api; ninguno se compila.</summary>
    public static void AssertAllHandlersArePreGenerated(IServiceProvider services, Assembly applicationAssembly)
    {
        var runtime = services.GetRequiredService<IWolverineRuntime>();
        Assert.Equal(TypeLoadMode.Static, runtime.Options.CodeGeneration.TypeLoadMode);

        foreach (var collection in services.GetServices<ICodeFileCollection>())
            collection.AssertPreBuildTypesExist(services);

        var graph = services.GetServices<ICodeFileCollection>().OfType<HandlerGraph>().Single();
        Assert.NotEmpty(graph.Chains);
        var notPreGenerated = graph
            .Chains.Where(chain => graph.HandlerFor(chain.MessageType)?.GetType().Assembly != applicationAssembly)
            .Select(chain => chain.MessageType.FullName)
            .ToList();
        Assert.True(
            notPreGenerated.Count == 0,
            "Handlers sin código pregenerado (regenerar con scripts/codegen/regenerate-wolverine-code.ps1):\n"
                + string.Join("\n", notPreGenerated)
        );
    }

    /// <summary>
    /// Regenera el código en <paramref name="scratchDirectory"/> (el host debe apuntar ahí su
    /// <c>GeneratedCodeOutputPath</c>) y lo compara con lo versionado: un handler nuevo, quitado o cambiado sin
    /// regenerar falla acá y no en producción. Wolverine no ordena igual en cada corrida las líneas que no
    /// dependen entre sí ni los handlers de una misma cadena, así que cada archivo se compara por sus líneas sin
    /// importar el orden ni el número de las variables locales.
    /// </summary>
    public static void AssertGeneratedCodeIsCurrent(
        IServiceProvider services,
        string committedDirectory,
        string scratchDirectory
    )
    {
        var collections = services.GetServices<ICodeFileCollection>().ToArray();
        Assert.All(
            collections,
            collection =>
                Assert.Equal(
                    Path.GetFullPath(scratchDirectory),
                    Path.GetFullPath(collection.Rules.GeneratedCodeOutputPath)
                )
        );
        var builder = new DynamicCodeBuilder(services, collections)
        {
            // Igual que el comando codegen: sin esto no resuelve los IDomainEventScraper del contenedor.
            ServiceVariableSource = services.GetRequiredService<IServiceVariableSource>(),
        };
        builder.WriteGeneratedCode(_ => { });

        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
        {
            Replace(committedDirectory, scratchDirectory);
            return;
        }

        var expected = ReadTree(scratchDirectory);
        var actual = ReadTree(committedDirectory);
        var problems = expected
            .Keys.Union(actual.Keys)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
                !actual.ContainsKey(name) ? $"falta: {name}"
                : !expected.ContainsKey(name) ? $"sobra: {name}"
                : actual[name] != expected[name] ? $"desactualizado: {name}"
                : null
            )
            .OfType<string>()
            .ToList();
        Assert.True(
            problems.Count == 0,
            "El código pregenerado no coincide (regenerar con scripts/codegen/regenerate-wolverine-code.ps1):\n"
                + string.Join("\n", problems)
        );
    }

    /// <summary>
    /// Variables locales numeradas (<c>outgoing1</c>, <c>outgoing2</c>): en una cadena con varios handlers el
    /// número sigue el orden de descubrimiento, que Wolverine no fija entre procesos.
    /// </summary>
    private static readonly Regex NumberedLocal = new(@"\b([a-z][A-Za-z_]*?)\d+\b", RegexOptions.Compiled);

    private static Dictionary<string, string> ReadTree(string directory) =>
        Directory.Exists(directory)
            ? Directory
                .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .ToDictionary(
                    file => Path.GetRelativePath(directory, file).Replace('\\', '/'),
                    file =>
                        string.Join(
                            "\n",
                            File.ReadAllLines(file)
                                .Select(line => NumberedLocal.Replace(line.TrimEnd(), "$1#"))
                                .Where(line => line.Length > 0)
                                .Order(StringComparer.Ordinal)
                        )
                )
            : [];

    private static void Replace(string committedDirectory, string generatedDirectory)
    {
        if (Directory.Exists(committedDirectory))
            Directory.Delete(committedDirectory, recursive: true);
        foreach (var file in Directory.EnumerateFiles(generatedDirectory, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(committedDirectory, Path.GetRelativePath(generatedDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("No se encontró TaxVision.slnx subiendo desde " + AppContext.BaseDirectory);
    }
}
