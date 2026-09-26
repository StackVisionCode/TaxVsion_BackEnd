using Xunit;

namespace TaxVision.Gateway.Tests.Pipeline;

/// <summary>
/// En cross-origin el navegador solo deja leer a la SPA los headers "simples"; sin exponerlos, el front
/// no ve <c>Retry-After</c> ni <c>X-RateLimit-*</c> tras un 429/503 y no puede decirle al usuario cuánto
/// esperar. La política CORS vive inline en Program.cs, así que se congela sobre el fuente (mismo
/// criterio que <see cref="GatewayWebSocketPipelineTests"/>).
/// </summary>
public sealed class GatewayCorsPolicyTests
{
    private static string ProgramSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "Gateway", "TaxVision.Gateway", "Program.cs"));
    }

    [Theory]
    [InlineData("Retry-After")]
    [InlineData("X-RateLimit-Limit")]
    [InlineData("X-RateLimit-Remaining")]
    [InlineData("X-RateLimit-Reset")]
    [InlineData("X-RateLimit-Policy")]
    [InlineData("X-RateLimit-Layer")]
    public void LaPoliticaSpa_ExponeLosHeadersDeRateLimit(string header)
    {
        var source = ProgramSource();
        var exposed = source.IndexOf("WithExposedHeaders(", StringComparison.Ordinal);

        Assert.True(exposed >= 0, "La política CORS \"spa\" no expone headers (falta WithExposedHeaders).");
        Assert.True(
            source.IndexOf($"\"{header}\"", exposed, StringComparison.Ordinal) >= 0,
            $"La política CORS \"spa\" no expone {header}."
        );
    }
}
