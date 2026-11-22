using System.Text.RegularExpressions;
using Fluid;
using TaxVision.Scribe.Application.Templates.Seed;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Tests.Templates.Seed;

/// <summary>
/// D-N48: correos de los avisos obligatorios a la oficina (depósito fallido, cuenta de cobros, malware, DMCA y
/// retención legal). Solo usan lo que ya trae el evento, declaran cada variable y llevan su versión.
/// </summary>
public sealed class OfficeAlertTemplatesTests
{
    private const int OfficeAlertsVersion = 38;
    private static readonly FluidParser Parser = new();

    // Clave y variables propias (nombre, tipo, obligatoria); language y product_name van en todas.
    private static readonly Dictionary<string, (string Name, VariableType Type, bool Required)[]> Expected = new()
    {
        ["payments.payout_failed.v1"] =
        [
            ("amount_label", VariableType.String, true),
            ("failed_at", VariableType.Date, true),
            ("failure_reason", VariableType.String, false),
            ("payout_reference", VariableType.String, false),
            ("action_url", VariableType.Url, true),
        ],
        ["payments.connect_account_restricted.v1"] =
        [
            ("restricted_at", VariableType.Date, true),
            ("requirements_count", VariableType.Number, false),
            ("action_url", VariableType.Url, true),
        ],
        ["payments.connect_account_onboarding_required.v1"] =
        [
            ("requirements_count", VariableType.Number, false),
            ("action_url", VariableType.Url, true),
        ],
        ["storage.file_infected.v1"] =
        [
            ("detected_at", VariableType.Date, true),
            ("scan_result", VariableType.String, false),
            ("action_url", VariableType.Url, true),
        ],
        ["storage.dmca_takedown.v1"] =
        [
            ("blocked_at", VariableType.Date, true),
            ("notice_reference", VariableType.String, false),
            ("action_url", VariableType.Url, true),
        ],
        ["storage.dmca_counter_notice.v1"] =
        [
            ("submitted_at", VariableType.Date, true),
            ("notice_reference", VariableType.String, false),
            ("action_url", VariableType.Url, true),
        ],
        ["storage.dmca_reinstated.v1"] =
        [
            ("reinstated_at", VariableType.Date, true),
            ("notice_reference", VariableType.String, false),
            ("action_url", VariableType.Url, true),
        ],
        ["storage.legal_hold_placed.v1"] =
        [
            ("placed_at", VariableType.Date, true),
            ("reason", VariableType.String, false),
            ("action_url", VariableType.Url, true),
        ],
        ["storage.legal_hold_lifted.v1"] =
        [
            ("lifted_at", VariableType.Date, true),
            ("action_url", VariableType.Url, true),
        ],
    };

    public static TheoryData<string> Keys => [.. Expected.Keys];

    [Theory]
    [MemberData(nameof(Keys))]
    public void Declares_its_variables_and_carries_its_version(string key)
    {
        var seed = Seed(key);
        var declared = seed.Variables.ToDictionary(v => v.Name);

        foreach (var (name, type, required) in Expected[key])
        {
            Assert.True(declared.TryGetValue(name, out var variable), $"{key}: missing {name}");
            Assert.Equal(type, variable.Type);
            Assert.Equal(required, variable.Required);
        }
        Assert.Contains(seed.Variables, v => v.Name == "language" && !v.Required && v.DefaultValue == "En");
        Assert.Contains(seed.Variables, v => v.Name == "product_name" && !v.Required);
        Assert.Equal(Expected[key].Length + 2, seed.Variables.Count);

        var used = Regex
            .Matches(seed.Html + seed.Subject, @"(?:\{\{\s*|\{%\s*(?:if|unless|elsif)\s+)([a-z_][a-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        Assert.Empty(used.Except(declared.Keys));

        Assert.Equal(key, seed.EventKey);
        Assert.Equal("system-base", seed.LayoutKey);
        Assert.Equal(TemplateChannel.Email, seed.Channel);
        Assert.True(seed.ContentVersion >= OfficeAlertsVersion, $"{key}: ContentVersion {seed.ContentVersion}");
        Assert.Contains(NotificationTemplateSeedSource.VariablesWithPreheader(seed), v => v.Name == "preheader");
    }

    /// <summary>Las fechas llegan tipadas: Scribe las imprime en la zona de la oficina, nunca "UTC" escrito.</summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void Dates_use_only_the_typed_markers(string key)
    {
        var seed = Seed(key);

        Assert.DoesNotContain("UTC", seed.Html);
        Assert.DoesNotContain("| date", seed.Html);
        foreach (var date in seed.Variables.Where(v => v.Type == VariableType.Date))
            Assert.Contains("{{ " + date.Name + " }}", seed.Html);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Links_to_the_crm_and_speaks_to_the_office(string key)
    {
        var (_, html) = Render(Seed(key), new() { ["action_url"] = "https://office.example/target" });

        Assert.Contains("href=\"https://office.example/target\"", html);
        Assert.DoesNotContain("tenant", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".manage", html);
    }

    [Fact]
    public void Payout_failed_shows_the_optional_rows_only_with_a_value()
    {
        var seed = Seed("payments.payout_failed.v1");
        var vars = new Dictionary<string, object?>
        {
            ["amount_label"] = "120.00 USD",
            ["failed_at"] = "Nov 22, 2026",
            ["language"] = "Es",
        };

        var (subject, without) = Render(seed, vars);
        Assert.Contains("120.00 USD", subject);
        Assert.DoesNotContain(">Motivo<", without);
        Assert.DoesNotContain(">Referencia<", without);

        vars["failure_reason"] = "account_closed";
        vars["payout_reference"] = "po_123";
        var (_, with) = Render(seed, vars);
        Assert.Contains(">Motivo<", with);
        Assert.Contains("account_closed", with);
        Assert.Contains("po_123", with);
    }

    [Theory]
    [InlineData("payments.connect_account_restricted.v1")]
    [InlineData("payments.connect_account_onboarding_required.v1")]
    public void Connect_account_shows_the_pending_count_only_when_sent(string key)
    {
        var (_, without) = Render(Seed(key), new());
        Assert.DoesNotContain("Pending items", without);

        var (_, with) = Render(Seed(key), new() { ["requirements_count"] = 3 });
        Assert.Contains("Pending items", with);
        Assert.Contains(">3<", with);
    }

    /// <summary>Contranotificación DMCA (N8-01): versión propia y solo los datos que ya muestra el takedown.</summary>
    [Fact]
    public void Dmca_counter_notice_carries_its_own_version_and_no_counter_notice_text()
    {
        var seed = Seed("storage.dmca_counter_notice.v1");

        Assert.Equal(40, seed.ContentVersion);
        var (subject, html) = Render(
            seed,
            new()
            {
                ["submitted_at"] = "Nov 22, 2026",
                ["notice_reference"] = "notice-1",
                ["action_url"] = "https://office.example/target",
                ["language"] = "Es",
            }
        );
        Assert.Contains("contranotificación", subject);
        Assert.Contains(">Referencia del aviso<", html);
        Assert.Contains("notice-1", html);
        Assert.DoesNotContain(seed.Variables, v => v.Name.Contains("text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Legal_hold_placed_shows_the_reason_only_when_sent()
    {
        var seed = Seed("storage.legal_hold_placed.v1");

        Assert.DoesNotContain(">Reason<", Render(seed, new()).Html);
        Assert.Contains("Pending audit", Render(seed, new() { ["reason"] = "Pending audit" }).Html);
    }

    private static (string Subject, string Html) Render(NotificationTemplateSeed seed, Dictionary<string, object?> vars)
    {
        var context = new TemplateContext();
        foreach (var (name, value) in vars)
            context.SetValue(name, value ?? string.Empty);
        return (
            Parser.Parse(seed.Subject).Render(context, NullEncoder.Default),
            Parser.Parse(seed.Html).Render(context, NullEncoder.Default)
        );
    }

    private static NotificationTemplateSeed Seed(string key) =>
        Assert.Single(NotificationTemplateSeedSource.All, item => item.TemplateKey == key);
}
