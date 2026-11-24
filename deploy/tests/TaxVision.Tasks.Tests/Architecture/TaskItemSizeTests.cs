namespace TaxVision.Tasks.Tests.Architecture;

/// <summary>
/// TaskItem también tiene tope (R-21): ningún archivo parcial pasa de 600 líneas y el agregado entero no pasa de
/// 1300, para que no siga creciendo sin decidirlo.
/// </summary>
public sealed class TaskItemSizeTests
{
    private const int PerFile = 600;
    private const int Total = 1300;

    [Fact]
    public void TaskItem_stays_within_its_size_budget()
    {
        var files = Directory
            .EnumerateFiles(DomainTasksDirectory, "TaskItem*.cs")
            .Where(file =>
                Path.GetFileName(file) is var name
                && (name == "TaskItem.cs" || name.StartsWith("TaskItem.", StringComparison.Ordinal))
            )
            .Select(file => (Name: Path.GetFileName(file), Lines: File.ReadAllLines(file).Length))
            .ToList();

        var tooLong = files.Where(f => f.Lines > PerFile).Select(f => $"{f.Name} = {f.Lines}").ToList();
        Assert.True(tooLong.Count == 0, "Partir en otro archivo parcial: " + string.Join(", ", tooLong));
        Assert.True(
            files.Sum(f => f.Lines) <= Total,
            $"TaskItem suma {files.Sum(f => f.Lines)} líneas (tope {Total})."
        );
    }

    private static string DomainTasksDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
                directory = directory.Parent;
            return Path.Combine(directory!.FullName, "src", "Services", "Tasks", "TaxVision.Tasks.Domain", "Tasks");
        }
    }
}
