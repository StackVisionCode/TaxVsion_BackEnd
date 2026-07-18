using System.Text.Encodings.Web;
using Fluid;
using TaxVision.Scribe.Application.Templates.BaseLayouts;

namespace TaxVision.Scribe.Tests.Templates.BaseLayouts;

/// <summary>
/// Prueba que el HTML autorado en Fase 4.6 sea Liquid válido y se comporte como FluidTemplateRenderer
/// lo va a usar (body sin doble-escapar, tenant_logo_missing gobierna el banner) — sin esto, un typo en
/// el {% if %} o el filtro | raw no se detectaría hasta el primer render real en Fase 5.
/// </summary>
public sealed class BaseLayoutHtmlTests
{
    private static readonly FluidParser Parser = new();

    [Fact]
    public async Task SystemBaseV1_parses_and_renders_body_and_current_year()
    {
        var parsed = Parser.TryParse(BaseLayoutHtml.SystemBaseV1, out var template, out var error);
        Assert.True(parsed, error);

        var context = new TemplateContext();
        context.SetValue("body", "<p>Hola</p>");
        context.SetValue("current_year", 2026);

        var html = await template!.RenderAsync(context, HtmlEncoder.Default);

        Assert.Contains("<p>Hola</p>", html);
        Assert.Contains("2026", html);
        Assert.Contains("cid:logo-header", html);
    }

    [Fact]
    public async Task TenantBaseV1_shows_the_banner_only_when_tenant_logo_missing_is_true()
    {
        var parsed = Parser.TryParse(BaseLayoutHtml.TenantBaseV1, out var template, out var error);
        Assert.True(parsed, error);

        var withFallback = new TemplateContext();
        withFallback.SetValue("body", "<p>Hola</p>");
        withFallback.SetValue("tenant_name", "Acme & Co");
        withFallback.SetValue("tenant_address", "123 Main St");
        withFallback.SetValue("tenant_logo_missing", true);
        var htmlWithFallback = await template!.RenderAsync(withFallback, HtmlEncoder.Default);
        Assert.Contains("Configura tu logo", htmlWithFallback);
        Assert.Contains("Acme &amp; Co", htmlWithFallback);

        var withOwnLogo = new TemplateContext();
        withOwnLogo.SetValue("body", "<p>Hola</p>");
        withOwnLogo.SetValue("tenant_name", "Acme");
        withOwnLogo.SetValue("tenant_address", "123 Main St");
        withOwnLogo.SetValue("tenant_logo_missing", false);
        var htmlWithOwnLogo = await template.RenderAsync(withOwnLogo, HtmlEncoder.Default);
        Assert.DoesNotContain("Configura tu logo", htmlWithOwnLogo);
    }
}
