using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Infrastructure;
using Xunit;

namespace TaxVision.Billing.Tests.Architecture;

/// <summary>Un stub registrado responde "no existe" sin avisar: si algo lo inyecta, debe fallar al arrancar.</summary>
public sealed class BillingDependencyInjectionTests
{
    [Fact]
    public void NoStubRepositoryRegistered()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=127.0.0.1,1;Database=unused;Trusted_Connection=False",
                }
            )
            .Build();

        var services = new ServiceCollection().AddBillingInfrastructure(configuration);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IPaymentReceiptRepository));
    }
}
