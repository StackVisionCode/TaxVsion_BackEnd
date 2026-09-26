using System.Text.RegularExpressions;

namespace TaxVision.PaymentApp.Tests.Deploy;

public sealed class PaymentAppDeployConfigWiringTests
{
    private static readonly string[] PaymentProviderEnvVars =
    [
        "STRIPE_SECRET_KEY",
        "STRIPE_WEBHOOK_SECRET",
        "STRIPE_ONBOARDING_ENABLED",
        "STRIPE_ONBOARDING_DISABLED_REASON",
        "PAYPAL_BASE_URL",
        "PAYPAL_CLIENT_ID",
        "PAYPAL_CLIENT_SECRET",
        "PAYPAL_WEBHOOK_ID",
        "PAYPAL_ONBOARDING_ENABLED",
        "PAYPAL_ONBOARDING_DISABLED_REASON",
    ];

    [Theory]
    [MemberData(nameof(PaymentProviderVars))]
    public void Payment_provider_env_var_is_mapped_in_compose(string varName)
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Matches(new Regex(@"\$\{" + Regex.Escape(varName) + @"[:}]"), compose);
    }

    [Theory]
    [MemberData(nameof(PaymentProviderVars))]
    public void Payment_provider_env_var_is_passed_by_deploy_workflow(string varName)
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        Assert.Contains($"{varName}=${{{{ secrets.{varName} }}}}", workflow);
    }

    /// <summary>
    /// El recibo lo firma CloudStorage, y PaymentApp lo llama por HTTP. El cliente tiene un default
    /// http://localhost:5330 que dentro del contenedor no es nadie: si el compose no define la URL,
    /// la llamada muere en connection refused y la descarga responde 400 sin que nada falle al arrancar.
    /// </summary>
    [Fact]
    public void PaymentApp_points_at_cloudstorage_in_compose()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");
        var service = ComposeServiceBlock(compose, "payment-app-api");

        Assert.Contains("CloudStorageClient__BaseUrl: http://cloudstorage-api:8080", service);
    }

    /// <summary>El bloque de un servicio del compose: desde su clave hasta la del siguiente.</summary>
    private static string ComposeServiceBlock(string compose, string serviceName)
    {
        var header = Regex.Match(compose, "^  " + Regex.Escape(serviceName) + ":$", RegexOptions.Multiline);
        Assert.True(header.Success, "El compose no declara el servicio " + serviceName + ".");

        var rest = compose[(header.Index + header.Length)..];
        var next = Regex.Match(rest, "^  [a-z0-9-]+:$", RegexOptions.Multiline);
        return next.Success ? compose.Substring(header.Index, header.Length + next.Index) : compose[header.Index..];
    }

    [Fact]
    public void Onboarding_catalog_maps_configured_stripe_and_paypal_methods()
    {
        var compose = ReadRepoFile("deploy/docker/docker-compose.yml");

        Assert.Contains("PaymentMethods__Onboarding__0__Provider: Stripe", compose);
        Assert.Contains("PaymentMethods__Onboarding__0__Method: Card", compose);
        Assert.Contains("PaymentMethods__Onboarding__0__Enabled: ${STRIPE_ONBOARDING_ENABLED:-true}", compose);
        Assert.Contains("PaymentMethods__Onboarding__1__Provider: PayPal", compose);
        Assert.Contains("PaymentMethods__Onboarding__1__Method: Wallet", compose);
        Assert.Contains("PaymentMethods__Onboarding__1__Enabled: ${PAYPAL_ONBOARDING_ENABLED:-false}", compose);
        Assert.Contains(
            "PaymentMethods__Onboarding__1__DisabledReason: ${PAYPAL_ONBOARDING_DISABLED_REASON:-}",
            compose
        );
    }

    public static IEnumerable<object[]> PaymentProviderVars() => PaymentProviderEnvVars.Select(v => new object[] { v });

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' walking up from {AppContext.BaseDirectory}."
        );
    }
}
