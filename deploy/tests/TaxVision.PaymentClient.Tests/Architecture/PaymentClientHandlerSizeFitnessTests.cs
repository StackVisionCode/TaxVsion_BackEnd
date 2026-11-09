using System.Text.RegularExpressions;

namespace TaxVision.PaymentClient.Tests.Architecture;

/// <summary>Fitness de tamaño basada en texto (modelo: <c>HandlerSizeFitnessTests</c> de PaymentApp): un handler no vuelve
/// a mezclar decisión, proveedor y persistencia. Mira la clase, no el nombre del archivo: después de BB-8 muchos handlers
/// viven en *Command.cs.</summary>
public sealed partial class PaymentClientHandlerSizeFitnessTests
{
    private const int MaxHandlerFileLines = 200;

    /// <summary>Deuda conocida, fuera de PCL-11. Se va vaciando: una entrada que ya cumple hace fallar la prueba.</summary>
    private static readonly HashSet<string> TemporaryExceptions =
    [
        "TenantPayments/Commands/ReconcileTenantPayment/ReconcileTenantPaymentHandler.cs",
    ];

    [Fact]
    public void Handlers_DoNotExceedLineBudget()
    {
        var oversized = HandlerFileSizes()
            .Where(file => file.Lines > MaxHandlerFileLines && !TemporaryExceptions.Contains(file.Path))
            .Select(file => $"{file.Path} ({file.Lines})")
            .ToList();

        Assert.True(
            oversized.Count == 0,
            $"Handlers over {MaxHandlerFileLines} lines: {string.Join(", ", oversized)}. "
                + "Extract a helper or move the rule to the domain instead of growing the handler."
        );
    }

    [Fact]
    public void Temporary_exceptions_are_still_needed()
    {
        var sizes = HandlerFileSizes().ToDictionary(file => file.Path, file => file.Lines);

        var stale = TemporaryExceptions.Where(path =>
            !sizes.TryGetValue(path, out var lines) || lines <= MaxHandlerFileLines
        );

        Assert.Empty(stale);
    }

    private static IEnumerable<(string Path, int Lines)> HandlerFileSizes()
    {
        var root = ApplicationRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (
                relative.StartsWith("bin/", StringComparison.Ordinal)
                || relative.StartsWith("obj/", StringComparison.Ordinal)
            )
                continue;

            if (HandlerClass().IsMatch(File.ReadAllText(file)))
                yield return (relative, File.ReadAllLines(file).Length);
        }
    }

    private static string ApplicationRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        return dir is null
            ? throw new InvalidOperationException("Could not locate the repo root (TaxVision.slnx).")
            : Path.Combine(dir.FullName, "src", "Services", "PaymentClient", "TaxVision.PaymentClient.Application");
    }

    [GeneratedRegex(@"\bclass\s+\w+(Handler|Consumer)\b")]
    private static partial Regex HandlerClass();
}
