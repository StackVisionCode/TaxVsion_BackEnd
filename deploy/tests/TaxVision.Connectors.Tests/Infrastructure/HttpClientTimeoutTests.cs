using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Connectors.Infrastructure;
using TaxVision.Connectors.Infrastructure.Providers.Gmail;
using TaxVision.Connectors.Infrastructure.Providers.Graph;
using TaxVision.Connectors.Infrastructure.Providers.OAuth;
using TaxVision.Connectors.Infrastructure.Providers.Watch;

namespace TaxVision.Connectors.Tests.Infrastructure;

/// <summary>
/// Fase 3 (hardening) — prueba que <c>AddConnectorsInfrastructure</c> efectivamente configura un
/// timeout de 30s en cada uno de los HttpClient tipados que registra, en vez de caer al default de
/// 100s del framework. Resuelve por <see cref="IHttpClientFactory"/> usando el nombre convencional
/// que <c>AddHttpClient&lt;TClient&gt;()</c> asigna (el nombre corto del tipo) para no tener que
/// satisfacer el resto de las dependencias de cada client tipado — el objetivo acá es únicamente
/// verificar la configuración del HttpClient en sí, no el comportamiento del client.
/// </summary>
public sealed class HttpClientTimeoutTests
{
    private static readonly TimeSpan ExpectedTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(nameof(GoogleOAuthClient))]
    [InlineData(nameof(MicrosoftOAuthClient))]
    [InlineData(nameof(GmailApiClient))]
    [InlineData(nameof(GraphApiClient))]
    [InlineData(nameof(GmailWatchClient))]
    [InlineData(nameof(GraphWatchClient))]
    public void AddConnectorsInfrastructure_configures_30s_timeout_on_typed_http_client(string typedClientName)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=.;Database=ConnectorsTest;Trusted_Connection=True;",
                }
            )
            .Build();

        services.AddConnectorsInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        var client = httpClientFactory.CreateClient(typedClientName);

        Assert.Equal(ExpectedTimeout, client.Timeout);
    }
}
