using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// Guardrail de tiempo (P22): los contenedores corren en UTC declarado. Va en la imagen (vale en local y en el VM)
/// y en el compose (cubre también las imágenes de terceros). Ningún cálculo de negocio usa la zona del proceso.
/// </summary>
public sealed class ContainerUtcTimeZoneDeployTests
{
    [Fact]
    public void Every_image_declares_utc_in_its_final_stage()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var offenders = Directory
            .EnumerateFiles(src, "Dockerfile", SearchOption.AllDirectories)
            .Where(path => !path.Contains("node_modules", StringComparison.Ordinal))
            .Where(path =>
                !Regex.IsMatch(FinalStage(File.ReadAllText(path)), @"^ENV TZ=Etc/UTC\s*$", RegexOptions.Multiline)
            )
            .Select(path => Path.GetRelativePath(src, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Imagen sin `ENV TZ=Etc/UTC` en la etapa final:\n" + string.Join("\n", offenders)
        );
    }

    [Fact]
    public void Every_compose_service_declares_utc()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "deploy", "docker", "docker-compose.yml"))
            .Replace("\r\n", "\n");
        const string header = "\nservices:\n";
        var start = compose.IndexOf(header, StringComparison.Ordinal) + header.Length;
        var end = Regex.Match(compose[start..], @"^[a-z]", RegexOptions.Multiline);
        var services = end.Success ? compose[start..(start + end.Index)] : compose[start..];
        var blocks = Regex
            .Split(services, @"^(?=  [a-z0-9_-]+:\s*$)", RegexOptions.Multiline)
            .Where(block => Regex.IsMatch(block, @"^  [a-z0-9_-]+:\s*$", RegexOptions.Multiline))
            .ToList();
        Assert.True(blocks.Count >= 40, $"Solo se encontraron {blocks.Count} servicios en el compose.");

        var offenders = blocks
            .Where(block => !Regex.IsMatch(block, @"^      TZ: Etc/UTC\s*$", RegexOptions.Multiline))
            .Select(block => block.Split('\n')[0].Trim().TrimEnd(':'))
            .ToList();

        Assert.True(offenders.Count == 0, "Servicio del compose sin `TZ: Etc/UTC`:\n" + string.Join("\n", offenders));
    }

    private static string FinalStage(string dockerfile)
    {
        var text = dockerfile.Replace("\r\n", "\n");
        var last = text.LastIndexOf("\nFROM ", StringComparison.Ordinal);
        return last < 0 ? text : text[(last + 1)..];
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
