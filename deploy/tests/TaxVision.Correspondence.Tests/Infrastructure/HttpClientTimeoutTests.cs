using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Correspondence.Infrastructure;

namespace TaxVision.Correspondence.Tests.Infrastructure;

/// <summary>
/// Fase 1 (hardening) — prueba que <c>AddCorrespondenceInfrastructure</c> efectivamente configura un
/// timeout de 30s en cada uno de los HttpClient tipados que registra, en vez de caer al default de
/// 100s del framework. Resuelve por <see cref="IHttpClientFactory"/> usando el nombre que
/// <c>AddHttpClient&lt;TClient, TImplementation&gt;()</c> asigna — a diferencia del overload de un
/// solo tipo que usa Connectors (nombre = tipo concreto), este archivo registra todos sus clientes
/// con el overload de dos tipos (interfaz + implementación), y el framework nombra el HttpClient
/// según <c>typeof(TClient).Name</c>, es decir la INTERFAZ (primer parámetro de tipo), no la clase
/// concreta — de ahí el prefijo "I" en cada InlineData. Se usan literales en vez de <c>nameof()</c>
/// porque las interfaces de los clientes internos (p.ej. <c>ICorrespondenceServiceTokenAcquirer</c>)
/// son también <c>internal</c> a Infrastructure. El objetivo acá es únicamente verificar la
/// configuración del HttpClient en sí, no el comportamiento del client.
/// </summary>
public sealed class HttpClientTimeoutTests
{
    private static readonly TimeSpan ExpectedTimeout = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData("ICorrespondenceServiceTokenAcquirer")]
    [InlineData("ICorrespondenceCustomerClient")]
    [InlineData("IConnectorsClient")]
    [InlineData("ICloudStorageClient")]
    [InlineData("IPostmasterClient")]
    public void AddCorrespondenceInfrastructure_configures_30s_timeout_on_typed_http_client(string typedClientName)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=.;Database=CorrespondenceTest;Trusted_Connection=True;",
                }
            )
            .Build();

        services.AddCorrespondenceInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

        var client = httpClientFactory.CreateClient(typedClientName);

        Assert.Equal(ExpectedTimeout, client.Timeout);
    }
}
