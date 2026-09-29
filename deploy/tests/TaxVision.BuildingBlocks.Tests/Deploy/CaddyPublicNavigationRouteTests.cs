using System.Text.RegularExpressions;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Deploy;

/// <summary>
/// El host de un tenant (<c>manfer.taxproffice.com</c>) sirve la SPA y la API a la vez, y Caddy las
/// separa por el header <c>Accept</c>: si dice <c>text/html</c> es navegación del navegador y va a la
/// SPA. Esa regla se come cualquier endpoint público que el usuario ABRE (no que la SPA consume por
/// XHR): llega con <c>Accept: text/html</c>, cae en la SPA, Angular no tiene esa ruta y muestra su
/// 404. El síntoma no se parece en nada a la causa — el servicio ni se entera, así que sus logs
/// salen limpios.
///
/// <para>Pasó dos veces: primero con la descarga pública de un documento firmado y después con la
/// URL estable de una factura (2026-09-29), que dejó a los clientes sin poder pagar. Cada uno de
/// esos endpoints necesita su propia regla ANTES de <c>@spa_nav</c>, y el orden es justamente lo que
/// se rompe en silencio: la regla puede estar escrita y no servir de nada si quedó debajo.</para>
/// </summary>
public sealed class CaddyPublicNavigationRouteTests
{
    [Theory]
    [InlineData("/storage/public/*")]
    [InlineData("/payments-client/invoices/*")]
    public void The_prefix_is_routed_to_the_gateway_before_the_spa_catches_the_navigation(string prefix)
    {
        var tenantSnippet = TenantSnippet();

        var matcher = Regex.Match(
            tenantSnippet,
            $@"^\s*@(?<name>\w+) path .*{Regex.Escape(prefix)}",
            RegexOptions.Multiline
        );
        Assert.True(
            matcher.Success,
            $"'{prefix}' no tiene matcher en el snippet (taxvision_tenant) del Caddyfile: en un host de "
                + "tenant lo atrapa @spa_nav y el usuario ve el 404 de Angular."
        );

        var handle = tenantSnippet.IndexOf($"handle @{matcher.Groups["name"].Value}", StringComparison.Ordinal);
        var spaNav = tenantSnippet.IndexOf("handle @spa_nav", StringComparison.Ordinal);
        Assert.True(spaNav >= 0, "No se encontró 'handle @spa_nav' en el snippet (taxvision_tenant).");

        Assert.True(
            handle >= 0 && handle < spaNav,
            $"El handle de '{prefix}' está DESPUÉS de @spa_nav. Los handle de Caddy se evalúan en el "
                + "orden del archivo, así que escrito ahí abajo no se ejecuta nunca."
        );
    }

    [Theory]
    [InlineData("/storage/public/*")]
    [InlineData("/payments-client/invoices/*")]
    public void The_prefix_goes_to_the_gateway_and_not_to_the_spa(string prefix)
    {
        // Apuntarlo a app.taxproffice.com sería el mismo 404 con una regla que aparenta arreglarlo.
        var tenantSnippet = TenantSnippet();
        var name = Regex
            .Match(tenantSnippet, $@"^\s*@(?<name>\w+) path .*{Regex.Escape(prefix)}", RegexOptions.Multiline)
            .Groups["name"]
            .Value;

        var block = Regex.Match(tenantSnippet, $@"handle @{name} \{{(?<body>[^}}]*)\}}", RegexOptions.Singleline);
        Assert.True(block.Success, $"No se pudo leer el bloque 'handle @{name}'.");
        Assert.Contains("reverse_proxy gateway:8080", block.Groups["body"].Value, StringComparison.Ordinal);
    }

    /// <summary>El snippet de hosts de tenant, que es el único donde conviven SPA y API.</summary>
    private static string TenantSnippet()
    {
        var caddyfile = ReadRepoFile("deploy/docker/caddy/Caddyfile").Replace("\r\n", "\n");
        var start = caddyfile.IndexOf("(taxvision_tenant) {", StringComparison.Ordinal);
        Assert.True(start >= 0, "No se encontró el snippet (taxvision_tenant) en el Caddyfile.");

        var end = caddyfile.IndexOf("\n}", start, StringComparison.Ordinal);
        Assert.True(end > start, "El snippet (taxvision_tenant) no cierra.");
        return caddyfile[start..end];
    }

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
