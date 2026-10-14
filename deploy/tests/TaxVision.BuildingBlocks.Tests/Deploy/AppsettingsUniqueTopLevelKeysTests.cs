using System.Text.Json;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// System.Text.Json (y por extension Microsoft.Extensions.Configuration.Json) toma solo la ULTIMA
/// aparicion cuando un objeto JSON lleva una clave repetida: el bloque anterior desaparece sin aviso.
/// Nos paso en CloudStorage al tener dos "CloudStorage" top-level en appsettings.json y perder
/// AssignmentVisibility/PerItemAccess en silencio en local dev. Este test barre todos los
/// appsettings*.json del repo (fuente, no artefactos de build) y asserta claves top-level unicas.
/// </summary>
public sealed class AppsettingsUniqueTopLevelKeysTests
{
    [Fact]
    public void Every_appsettings_file_has_unique_top_level_keys()
    {
        var repoRoot = FindRepoRoot();
        var files = Directory
            .EnumerateFiles(repoRoot, "appsettings*.json", SearchOption.AllDirectories)
            .Where(p => !IsBuildArtifact(p, repoRoot))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(files);

        var failures = new List<string>();
        foreach (var file in files)
        {
            var duplicates = FindDuplicateTopLevelKeys(file);
            if (duplicates.Count > 0)
            {
                var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                failures.Add($"{rel}: {string.Join(", ", duplicates)}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "appsettings con claves top-level duplicadas (System.Text.Json se queda con la ultima):\n"
                + string.Join('\n', failures)
        );
    }

    private static List<string> FindDuplicateTopLevelKeys(string path)
    {
        var bytes = File.ReadAllBytes(path);
        // Saltar BOM UTF-8 si lo trae.
        var span =
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
                ? bytes.AsSpan(3)
                : bytes.AsSpan();

        var options = new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var reader = new Utf8JsonReader(span, options);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dups = new List<string>();
        var depth = 0;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    depth++;
                    break;
                case JsonTokenType.EndObject:
                    depth--;
                    break;
                case JsonTokenType.PropertyName when depth == 1:
                    var name = reader.GetString() ?? string.Empty;
                    if (!seen.Add(name) && !dups.Contains(name))
                        dups.Add(name);
                    break;
            }
        }

        return dups;
    }

    private static bool IsBuildArtifact(string path, string repoRoot)
    {
        var rel = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
        return rel.Contains("/bin/", StringComparison.Ordinal)
            || rel.Contains("/obj/", StringComparison.Ordinal)
            || rel.StartsWith("bin/", StringComparison.Ordinal)
            || rel.StartsWith("obj/", StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (
                File.Exists(Path.Combine(dir.FullName, "TaxVision.sln"))
                || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                || Directory.Exists(Path.Combine(dir.FullName, "deploy"))
                    && Directory.Exists(Path.Combine(dir.FullName, "src"))
            )
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"No pude ubicar la raiz del repo caminando hacia arriba desde {AppContext.BaseDirectory}."
        );
    }
}
