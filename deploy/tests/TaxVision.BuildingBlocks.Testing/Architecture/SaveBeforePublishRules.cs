using System.Text.RegularExpressions;

namespace TaxVision.BuildingBlocks.Testing.Architecture;

/// <summary>Publicación seguida de un guardado en el mismo camino del método.</summary>
public sealed record SaveBeforePublishFinding(string Path, int PublishLine, int SaveLine);

/// <summary>
/// BB-16. Guardar antes de publicar: por <c>InvokeAsync</c>, lo publicado antes de un guardado que falla sale
/// igual en el reintento en línea. Cada publicación se sigue hasta lo primero que venga: un guardado (falla) o
/// un <c>return</c>/<c>throw</c> de la rama (el camino termina sin guardar, está bien).
///
/// <para><b>Límites.</b> Lee el texto, método por método. Una guarda <c>if (...) return</c> no corta el camino.
/// No ve lo que un servicio publique por dentro ni el guardado que hace Wolverine al salir del handler.</para>
/// </summary>
public static partial class SaveBeforePublishRules
{
    [GeneratedRegex(
        @"^[ \t]*(?:\[[^\]\r\n]*\][ \t]*)*(?:public|private|internal|protected)\b[^=;\r\n]*\(",
        RegexOptions.Multiline
    )]
    private static partial Regex MethodStart();

    [GeneratedRegex(
        @"\b(?:\w*[bB]us|\w*[cC]ontext|messaging|messages|outbox)\s*\.\s*(?:Publish\w*|Send\w*|Schedule\w*|Broadcast\w*|RequestSave\w*)Async\s*[<(]|(?<![\w.])Publish\w*Async\s*\(|\.\s*Publish\w*Async\s*\("
    )]
    private static partial Regex Publish();

    [GeneratedRegex(@"\.\s*SaveChangesAsync\s*\(|^[ \t]*(?<end>return\b|throw\b)", RegexOptions.Multiline)]
    private static partial Regex SaveOrEnd();

    /// <summary>Hallazgos de todos los <c>.cs</c> de una carpeta relativa a la raíz del repo.</summary>
    public static IReadOnlyList<SaveBeforePublishFinding> Scan(string sourceDirectory)
    {
        var root = JobPublishingRules.RepositoryRoot();
        return Directory
            .EnumerateFiles(Path.Combine(root, sourceDirectory), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGenerated(path))
            .SelectMany(path => Classify(File.ReadAllText(path), Path.GetRelativePath(root, path).Replace('\\', '/')))
            .ToArray();
    }

    /// <summary>Un hallazgo por método: el primero basta para mirar ese camino.</summary>
    public static IReadOnlyList<SaveBeforePublishFinding> Classify(string source, string path = "")
    {
        if (!source.Contains("SaveChangesAsync", StringComparison.Ordinal))
            return [];

        var findings = new List<SaveBeforePublishFinding>();
        var methods = MethodStart().Matches(source);
        for (var i = 0; i < methods.Count; i++)
        {
            var bodyStart = methods[i].Index + methods[i].Length;
            var bodyEnd = i + 1 < methods.Count ? methods[i + 1].Index : source.Length;
            var body = source[bodyStart..bodyEnd];

            foreach (Match publish in Publish().Matches(body))
            {
                var save = FindSaveOnTheSamePath(body, publish.Index + publish.Length);
                if (save is null)
                    continue;

                findings.Add(
                    new SaveBeforePublishFinding(
                        path,
                        LineOf(source, bodyStart + publish.Index),
                        LineOf(source, bodyStart + save.Value)
                    )
                );
                break;
            }
        }
        return findings;
    }

    private static int? FindSaveOnTheSamePath(string body, int from)
    {
        foreach (Match next in SaveOrEnd().Matches(body, from))
        {
            if (!next.Groups["end"].Success)
                return next.Index;
            if (!IsGuard(body, next.Index))
                return null;
        }
        return null;
    }

    // "if (...)" en la línea de arriba: el camino normal sigue después del return.
    private static bool IsGuard(string body, int index)
    {
        var previous = body[..index].TrimEnd();
        var line = previous[(previous.LastIndexOf('\n') + 1)..].Trim();
        return line.StartsWith("if", StringComparison.Ordinal)
            || line.StartsWith("else", StringComparison.Ordinal)
            || (line.EndsWith(')') && !line.EndsWith(");", StringComparison.Ordinal));
    }

    private static int LineOf(string source, int index) => source.AsSpan(0, index).Count('\n') + 1;

    private static bool IsGenerated(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.Contains("/Migrations/", StringComparison.Ordinal);
    }
}
