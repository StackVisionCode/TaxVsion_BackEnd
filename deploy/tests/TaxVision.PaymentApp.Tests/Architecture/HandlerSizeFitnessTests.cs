using System.Text.RegularExpressions;

namespace TaxVision.PaymentApp.Tests.Architecture;

/// <summary>
/// Fitness de tamaño basada en texto (no hay Roslyn en el repo y agregarlo solo para esto sería sobre-ingeniería): un
/// proxy tosco pero suficiente para que un handler no vuelva a mezclar decisión, proveedor y persistencia. No reemplaza
/// a NetArchTest para reglas de dependencias.
/// </summary>
public sealed partial class HandlerSizeFitnessTests
{
    private const int MaxHandleBodyLines = 40;
    private const int MaxHandlerFileLines = 200;

    private const string ApplicationRoot = "src/Services/PaymentApp/TaxVision.PaymentApp.Application";

    /// <summary>Deuda conocida, fuera de PAY-7. Se va vaciando: una entrada que ya cumple hace fallar la prueba.</summary>
    private static readonly HashSet<string> TemporaryExceptions =
    [
        "ProviderCustomers/Commands/ProcessPayPalVaultWebhook/ProcessPayPalVaultWebhookHandler.cs",
        "SaaSPayments/Commands/ReconcileHostedCheckout/ReconcileHostedCheckoutCommand.cs",
        "SaaSPayments/IntegrationEvents/CheckoutCancelledOrExpiredSaaSPaymentConsumer.cs",
    ];

    [Fact]
    public void PaymentApp_handlers_under_limit()
    {
        var oversized = HandlerFileSizes()
            .Where(file => file.Lines > MaxHandlerFileLines && !TemporaryExceptions.Contains(file.Path))
            .Select(file => $"{file.Path} ({file.Lines})");

        Assert.True(
            !oversized.Any(),
            $"Handlers over {MaxHandlerFileLines} lines: {string.Join(", ", oversized)}. "
                + "Extract a helper or move the rule to the domain instead of growing the handler."
        );
    }

    [Fact]
    public void Temporary_exceptions_are_still_needed()
    {
        var sizes = HandlerFileSizes().ToDictionary(file => file.Path, file => file.Lines);

        var stale = TemporaryExceptions.Where(path => !sizes.TryGetValue(path, out var lines) || lines <= MaxHandlerFileLines);

        Assert.Empty(stale);
    }

    [Fact]
    public void CreateOnboardingCheckoutHandler_Handle_StaysSmall()
    {
        var path = HandlerSourceLocator.FindRepoFile(
            $"{ApplicationRoot}/OnboardingCheckouts/Commands/CreateOnboardingCheckoutHandler.cs"
        );
        var lineCount = HandlerSourceLocator.CountHandleMethodBodyLines(path);

        Assert.True(
            lineCount <= MaxHandleBodyLines,
            $"CreateOnboardingCheckoutHandler.Handle has {lineCount} body lines (max {MaxHandleBodyLines}). "
                + "Extract another private helper instead of growing this method."
        );
    }

    // Después de BB-8 muchos handlers viven en *Command.cs: se mira la clase, no el nombre del archivo.
    private static IEnumerable<(string Path, int Lines)> HandlerFileSizes()
    {
        var root = HandlerSourceLocator.FindRepoDirectory(ApplicationRoot);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("bin/", StringComparison.Ordinal) || relative.StartsWith("obj/", StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(file);
            if (HandlerClass().IsMatch(text))
                yield return (relative, File.ReadAllLines(file).Length);
        }
    }

    [GeneratedRegex(@"\bclass\s+\w+(Handler|Consumer)\b")]
    private static partial Regex HandlerClass();
}
