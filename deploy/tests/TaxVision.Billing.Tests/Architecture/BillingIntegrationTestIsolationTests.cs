using System.Runtime.CompilerServices;
using Xunit;

namespace TaxVision.Billing.Tests.Architecture;

/// <summary>
/// BIL-14: las T-INT no corren en el gate, así que esta regla sí. La cadena de la base local se resuelve en un
/// solo lugar; una prueba que la lea por su cuenta escribe en la base que usa el Billing en marcha.
/// </summary>
public sealed class BillingIntegrationTestIsolationTests
{
    private const string AllowedFile = "BillingTestDatabase.cs";

    [Fact]
    public void IntegrationTests_DoNotResolveTheLocalDatabase()
    {
        var offenders = Directory
            .GetFiles(IntegrationFolder(), "*.cs")
            .Where(file => Path.GetFileName(file) != AllowedFile)
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains("AddUserSecrets", StringComparison.Ordinal)
                    || source.Contains("BILLING_DB_CONNECTION", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName);

        Assert.Empty(offenders);
    }

    private static string IntegrationFolder([CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(thisFile)!)!, "Integration");
}
