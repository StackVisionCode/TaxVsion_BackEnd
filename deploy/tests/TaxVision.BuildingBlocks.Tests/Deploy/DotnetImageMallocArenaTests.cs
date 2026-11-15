using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// En Linux, glibc crea hasta 8 arenas de malloc por núcleo y no devuelve al sistema la memoria nativa que el
/// proceso ya liberó (dotnet/runtime #122027, #121827): un .NET con 2 GB de RSS y 100 MB de heap. Con
/// <c>MALLOC_ARENA_MAX=2</c> cada servicio retiene menos. Va en la imagen, así vale igual en local y en el VM.
/// </summary>
public sealed class DotnetImageMallocArenaTests
{
    [Fact]
    public void Every_dotnet_image_limits_glibc_arenas()
    {
        var images = DotnetDockerfiles();
        Assert.NotEmpty(images);

        var offenders = images
            .Where(file =>
                !Regex.IsMatch(FinalStage(file.Content), @"^ENV MALLOC_ARENA_MAX=2\s*$", RegexOptions.Multiline)
            )
            .Select(file => file.Path)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Imagen .NET sin `ENV MALLOC_ARENA_MAX=2` en la etapa final:\n" + string.Join("\n", offenders)
        );
    }

    /// <summary>La etapa final: desde el último FROM.</summary>
    private static string FinalStage(string dockerfile)
    {
        var text = dockerfile.Replace("\r\n", "\n");
        var last = text.LastIndexOf("\nFROM ", StringComparison.Ordinal);
        return last < 0 ? text : text[(last + 1)..];
    }

    /// <summary>Dockerfiles cuya etapa final corre .NET (aspnet o la base de Playwright con .NET).</summary>
    private static List<(string Path, string Content)> DotnetDockerfiles()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        return Directory
            .EnumerateFiles(src, "Dockerfile", SearchOption.AllDirectories)
            .Where(path => !path.Contains("node_modules", StringComparison.Ordinal))
            .Select(path => (Path: Path.GetRelativePath(src, path).Replace('\\', '/'), Content: File.ReadAllText(path)))
            .Where(file =>
                Regex.IsMatch(
                    FinalStage(file.Content),
                    @"^FROM \S*(dotnet/aspnet|playwright/dotnet)\S*",
                    RegexOptions.Multiline
                )
            )
            .ToList();
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("TaxVision.slnx");
    }
}
