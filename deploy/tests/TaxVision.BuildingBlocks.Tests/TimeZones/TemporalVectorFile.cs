namespace TaxVision.BuildingBlocks.Tests.TimeZones;

/// <summary>Lee `deploy/tests/Shared/temporal-vectors.json` subiendo desde la carpeta de salida.</summary>
internal static class TemporalVectorFile
{
    private const string RelativePath = "deploy/tests/Shared/temporal-vectors.json";

    public static string Read()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{RelativePath}' walking up from {AppContext.BaseDirectory}."
        );
    }
}
