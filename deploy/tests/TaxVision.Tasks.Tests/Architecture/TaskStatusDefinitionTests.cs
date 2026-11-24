using System.Text.RegularExpressions;

namespace TaxVision.Tasks.Tests.Architecture;

/// <summary>
/// "Abierta" y "cerrada" se definen una vez, en el dominio (<c>TaskItemStatuses</c>): ninguna capa vuelve a escribir la
/// lista ni el par Completed/Cancelled a mano (R-03).
/// </summary>
public sealed class TaskStatusDefinitionTests
{
    private static readonly Regex HandWritten = new(
        @"TaskItemStatus\.Completed\s+or\s+TaskItemStatus\.Cancelled|OpenStatuses\s*=\s*\[",
        RegexOptions.Compiled
    );

    [Fact]
    public void Open_and_closed_are_written_only_in_the_domain_definition()
    {
        var offenders = new[]
        {
            "TaxVision.Tasks.Application",
            "TaxVision.Tasks.Infrastructure",
            "TaxVision.Tasks.Api",
            "TaxVision.Tasks.Domain",
        }
            .SelectMany(project =>
                Directory.EnumerateFiles(Path.Combine(TasksRoot, project), "*.cs", SearchOption.AllDirectories)
            )
            .Where(file => !IsGeneratedOrBuild(file) && !file.EndsWith("TaskItemStatuses.cs", StringComparison.Ordinal))
            .Where(file => HandWritten.IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0, "Usar TaskItemStatuses: " + string.Join(", ", offenders));
    }

    private static bool IsGeneratedOrBuild(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}");

    private static string TasksRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
                directory = directory.Parent;
            return Path.Combine(directory!.FullName, "src", "Services", "Tasks");
        }
    }
}
