using System.Text.RegularExpressions;

namespace TaxVision.BuildingBlocks.Testing.Architecture;

/// <summary>Tipo que recibe el bus de Wolverine por constructor.</summary>
public sealed record ConstructorBusFinding(string Path, string TypeName)
{
    public string Key => $"{Path} {TypeName}";
}

/// <summary>
/// NOT-22. Resuelto por service location, un <c>IMessageBus</c> de constructor es otro contexto: lo publicado no entra
/// en el outbox del handler y sale aunque el handler falle. El bus del handler llega por parámetro de método.
///
/// <para><b>Límites.</b> Lee el texto. No distingue un tipo que un handler arma a mano con su propio bus de uno que
/// sale del contenedor: esos se declaran con su motivo. Un <c>GetRequiredService&lt;IMessageBus&gt;</c> no se ve.</para>
/// </summary>
public static partial class ConstructorBusRules
{
    [GeneratedRegex(@"\b(?:class|record)\s+(?<name>\w+)(?:<[^>\r\n]*>)?\s*\((?<args>[^)]*)\)")]
    private static partial Regex PrimaryConstructor();

    [GeneratedRegex(
        @"\b(?:public|internal|protected|private)\s+(?<name>[A-Z]\w*)\s*\((?<args>[^)]*)\)\s*(?::\s*(?:base|this)\([^)]*\)\s*)?\{"
    )]
    private static partial Regex ExplicitConstructor();

    [GeneratedRegex(@"\bclass\s+(?<name>\w+)")]
    private static partial Regex ClassDeclaration();

    [GeneratedRegex(@"\bIMessage(?:Bus|Context)\s+\w+")]
    private static partial Regex BusParameter();

    /// <summary>Hallazgos de todos los <c>.cs</c> de una carpeta relativa a la raíz del repo.</summary>
    public static IReadOnlyList<ConstructorBusFinding> Scan(string sourceDirectory)
    {
        var root = JobPublishingRules.RepositoryRoot();
        return Directory
            .EnumerateFiles(Path.Combine(root, sourceDirectory), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGenerated(path) && !path.EndsWith("Controller.cs", StringComparison.Ordinal))
            .SelectMany(path => Classify(File.ReadAllText(path), Path.GetRelativePath(root, path).Replace('\\', '/')))
            .ToArray();
    }

    public static IReadOnlyList<ConstructorBusFinding> Classify(string source, string path = "")
    {
        if (!BusParameter().IsMatch(source))
            return [];

        var classes = ClassDeclaration().Matches(source).Select(m => m.Groups["name"].Value).ToHashSet();
        var primary = PrimaryConstructor().Matches(source).Where(TakesTheBus);
        var explicitOnes = ExplicitConstructor()
            .Matches(source)
            .Where(m => classes.Contains(m.Groups["name"].Value) && TakesTheBus(m));

        return primary
            .Concat(explicitOnes)
            .Select(m => new ConstructorBusFinding(path, m.Groups["name"].Value))
            .Distinct()
            .ToArray();
    }

    private static bool TakesTheBus(Match constructor) => BusParameter().IsMatch(constructor.Groups["args"].Value);

    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
