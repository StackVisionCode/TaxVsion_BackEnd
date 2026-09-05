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

    [Fact]
    public void Payment_provider_config_verifier_runs_in_deploy_workflow()
    {
        var workflow = ReadRepoFile(".github/workflows/deploy.yml");

        Assert.Contains("Validate payment provider configuration", workflow);
        Assert.Contains("scripts/verify-payment-provider-config.ps1 -Strict", workflow);
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
