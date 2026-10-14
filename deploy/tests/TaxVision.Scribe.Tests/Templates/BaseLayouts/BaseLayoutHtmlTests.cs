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

    /// <summary>Texto visible al humano: sin tags HTML y con las variables de layout fuera. Con esto, un wordmark
    /// partido en <c>Tax&lt;span&gt;Proffice&lt;/span&gt;</c> aparece como "TaxProffice" (el mismo string que ve el navegador).</summary>
    private static string StripTags(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>|\\{%[^%]*%\\}|\\{\\{[^}]*\\}\\}", string.Empty);

    [Fact]
    public async Task SystemBaseV1_parses_and_renders_body_and_current_year()
    {
        var parsed = Parser.TryParse(BaseLayoutHtml.SystemBaseV1, out var template, out var error);
        Assert.True(parsed, error);

        var context = new TemplateContext();
        context.SetValue("body", "<p>Hola</p>");
        context.SetValue("current_year", 2026);
        context.SetValue("product_name", "TaxOffix");
        context.SetValue("platform_legal_name", "TaxOffix LLC");

        var html = await template!.RenderAsync(context, HtmlEncoder.Default);

        Assert.Contains("<p>Hola</p>", html);
        Assert.Contains("2026", html);
        // La marca sale de las variables de plataforma (las pone el renderer), nunca escrita en el layout.
        Assert.Contains("TaxOffix", html);
        Assert.Contains("TaxOffix LLC", html);
        // Comprobar el string pelado no basta: el bug era un wordmark partido en `Tax<span>Proffice</span>`
        // que el navegador juntaba y el `Contains` no. El layout se evalúa sin tags.
        Assert.DoesNotContain(
            "TaxProffice",
            StripTags(BaseLayoutHtml.SystemBaseV1),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain(
            "TaxProffice",
            StripTags(BaseLayoutHtml.TenantBaseV1),
            StringComparison.OrdinalIgnoreCase
        );

        // v14: el logo del sistema (deploy.png) ya trae la marca dibujada, así que la cabecera solo tiene
        // que mostrar el <img>. Antes había un segundo `product_name` como wordmark al lado, que se
        // renderizaba junto al logo y duplicaba el nombre. En el header del template `product_name` debe
        // aparecer UNA sola vez: como `alt=` del logo (fallback accesible si el cliente no baja imágenes).
        // Mide solo lo visible del correo: desde `<body` hasta `{{ body | raw }}`. En el `<head>` también
        // va `{{ product_name }}` dentro del `<title>` (eso lo lee el navegador, no se ve en el correo).
        var raw = BaseLayoutHtml.SystemBaseV1;
        var bodyStart = raw.IndexOf("<body", StringComparison.Ordinal);
        var contentStart = raw.IndexOf("{{ body | raw }}", StringComparison.Ordinal);
        var header = raw.Substring(bodyStart, contentStart - bodyStart);
        var usesInHeader = System.Text.RegularExpressions.Regex.Matches(header, @"\{\{\s*product_name\s*\}\}").Count;
        Assert.Equal(1, usesInHeader);
        Assert.Matches(@"<img[^>]+cid:logo-header[^>]+alt=""\{\{\s*product_name\s*\}\}""", header);
    }

    /// <summary>
    /// Sin logo la cabecera cae al nombre de la oficina; con logo va la imagen inline. En ninguno de los
    /// dos casos aparece el aviso de configuracion (v7): tenant-base lo lee el CLIENTE de la oficina.
    /// </summary>
    [Fact]
    public async Task TenantBaseV1_falls_back_to_the_office_name_without_leaking_setup_copy()
    {
        var parsed = Parser.TryParse(BaseLayoutHtml.TenantBaseV1, out var template, out var error);
        Assert.True(parsed, error);

        var withFallback = new TemplateContext();
        withFallback.SetValue("body", "<p>Hola</p>");
        withFallback.SetValue("tenant_name", "Acme & Co");
        withFallback.SetValue("tenant_address", "123 Main St");
        withFallback.SetValue("tenant_logo_missing", true);
        var htmlWithFallback = await template!.RenderAsync(withFallback, HtmlEncoder.Default);
        Assert.Contains("Acme &amp; Co", htmlWithFallback);
        Assert.DoesNotContain("cid:logo-header", htmlWithFallback);
        Assert.DoesNotContain("Set your logo", htmlWithFallback);

        var withOwnLogo = new TemplateContext();
        withOwnLogo.SetValue("body", "<p>Hola</p>");
        withOwnLogo.SetValue("tenant_name", "Acme");
        withOwnLogo.SetValue("tenant_address", "123 Main St");
        withOwnLogo.SetValue("tenant_logo_missing", false);
        var htmlWithOwnLogo = await template.RenderAsync(withOwnLogo, HtmlEncoder.Default);
        Assert.Contains("cid:logo-header", htmlWithOwnLogo);
        Assert.DoesNotContain("Set your logo", htmlWithOwnLogo);
    }
}
