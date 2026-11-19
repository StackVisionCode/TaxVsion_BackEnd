using System.Text.RegularExpressions;
using Fluid;
using TaxVision.Scribe.Application.Templates.Seed;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Tests.Templates.Seed;

/// <summary>
/// Correos del Wallet (saldo bajo y recarga automática): plantillas nuevas, sembradas con su propia versión, que
/// declaran lo que usan, dicen lo importante en inglés y llevan al Wallet o a Settings → Wallet.
/// </summary>
public sealed class WalletAlertTemplateSeedTests
{
    private const int WalletAlertsVersion = 36;
    private static readonly FluidParser Parser = new();

    public static TheoryData<string, string> Seeds =>
        new()
        {
            { "wallet.low_balance.v1", "wallet.low_balance" },
            { "wallet.auto_recharge_succeeded.v1", "wallet.auto_recharge_succeeded" },
            { "wallet.auto_recharge_paused.v1", "wallet.auto_recharge_paused" },
            { "wallet.auto_recharge_daily_limit.v1", "wallet.auto_recharge_daily_limit" },
        };

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Wallet_templates_declare_every_variable_and_carry_their_version(string eventKey, string templateKey)
    {
        var seed = Seed(eventKey);
        var declared = seed.Variables.Select(v => v.Name).ToHashSet();
        var used = Regex
            .Matches(seed.Html + seed.Subject, @"(?:\{\{\s*|\{%\s*(?:if|unless|elsif)\s+)([a-z_][a-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        Assert.Empty(used.Except(declared));
        Assert.Equal(templateKey, seed.TemplateKey);
        Assert.Equal("system-base", seed.LayoutKey);
        Assert.Equal(TemplateChannel.Email, seed.Channel);
        Assert.True(seed.ContentVersion >= WalletAlertsVersion, $"ContentVersion {seed.ContentVersion} < 36");
        Assert.Contains(NotificationTemplateSeedSource.VariablesWithPreheader(seed), v => v.Name == "preheader");
        Assert.DoesNotContain("UTC", seed.Html);
    }

    [Fact]
    public void Low_balance_says_the_balance_the_threshold_and_links_to_the_wallet()
    {
        var (subject, html) = Render(
            "wallet.low_balance.v1",
            ("available_label", "$3.20"),
            ("threshold_label", "$5.00"),
            ("wallet_url", "https://acme.taxoffix.io/wallet"),
            ("settings_url", "https://acme.taxoffix.io/settings/wallet")
        );

        Assert.Equal("Your wallet balance is low", subject);
        Assert.Contains("$3.20", html);
        Assert.Contains("$5.00", html);
        Assert.Contains("href=\"https://acme.taxoffix.io/wallet\"", html);
        Assert.Contains("href=\"https://acme.taxoffix.io/settings/wallet\"", html);
    }

    [Fact]
    public void Receipt_shows_amount_method_balance_and_a_typed_date()
    {
        var seed = Seed("wallet.auto_recharge_succeeded.v1");
        var (subject, html) = Render(
            "wallet.auto_recharge_succeeded.v1",
            ("amount_label", "$25.00"),
            ("method_label", "Visa •••• 4242"),
            ("balance_after_label", "$27.50"),
            ("charged_at", "Wed, Nov 18, 2026, 10:30 AM · New York time, GMT-5"),
            ("wallet_url", "https://acme.taxoffix.io/wallet"),
            ("settings_url", "https://acme.taxoffix.io/settings/wallet")
        );

        Assert.Equal("Receipt: $25.00 automatic recharge", subject);
        Assert.Contains("$25.00", html);
        Assert.Contains("Visa •••• 4242", html);
        Assert.Contains("$27.50", html);
        Assert.Contains("Wed, Nov 18, 2026, 10:30 AM", html);
        Assert.Equal(VariableType.Date, seed.Variables.Single(v => v.Name == "charged_at").Type);
    }

    [Fact]
    public void Paused_says_why_that_it_will_not_retry_and_links_to_settings()
    {
        var (subject, html) = Render(
            "wallet.auto_recharge_paused.v1",
            ("reason_text", "We couldn't charge Visa •••• 4242 for your $25.00 automatic recharge."),
            ("amount_label", "$25.00"),
            ("method_label", "Visa •••• 4242"),
            ("settings_url", "https://acme.taxoffix.io/settings/wallet")
        );

        Assert.Equal("Automatic recharge is paused", subject);
        Assert.Contains("We couldn't charge Visa •••• 4242", html);
        Assert.Contains("won't retry on its own", html);
        Assert.Contains("Settings &rarr; Wallet", html);
        Assert.Contains("href=\"https://acme.taxoffix.io/settings/wallet\"", html);
    }

    [Fact]
    public void Daily_limit_says_the_limit_and_that_it_continues_tomorrow()
    {
        var (subject, html) = Render(
            "wallet.auto_recharge_daily_limit.v1",
            ("daily_limit", "3"),
            ("amount_label", "$25.00"),
            ("settings_url", "https://acme.taxoffix.io/settings/wallet")
        );

        Assert.Equal("Daily automatic recharge limit reached", subject);
        Assert.Contains("3 automatic recharges", html);
        Assert.Contains("tomorrow", html);
        Assert.Contains("href=\"https://acme.taxoffix.io/settings/wallet\"", html);
    }

    private static (string Subject, string Html) Render(string eventKey, params (string Name, object Value)[] values)
    {
        var seed = Seed(eventKey);
        var context = new TemplateContext();
        context.SetValue("product_name", "TaxOffix");
        foreach (var (name, value) in values)
            context.SetValue(name, value);
        return (Parser.Parse(seed.Subject).Render(context), Parser.Parse(seed.Html).Render(context));
    }

    private static NotificationTemplateSeed Seed(string eventKey) =>
        Assert.Single(NotificationTemplateSeedSource.All, item => item.EventKey == eventKey);
}
