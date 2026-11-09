namespace TaxVision.Customer.Tests.Architecture;

/// <summary>CUS-6: ningún handler del servicio supera el presupuesto de líneas. Lo que crece va a un colaborador.</summary>
public sealed class CustomerHandlerSizeTests
{
    private const int MaxLines = 300;

    [Fact]
    public void HandlersUnder300Lines()
    {
        var application = Path.Combine(
            RepositoryRoot(),
            "src",
            "Services",
            "Customer",
            "TaxVision.Customer.Application"
        );

        var offenders = Directory
            .EnumerateFiles(application, "*Handler.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => (Name: Path.GetFileName(file), Lines: File.ReadLines(file).Count()))
            .Where(file => file.Lines >= MaxLines)
            .Select(file => $"{file.Name} = {file.Lines}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0, $"Handlers de {MaxLines} líneas o más: {string.Join(" | ", offenders)}");
    }

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No se encontró TaxVision.slnx.");
    }
}
