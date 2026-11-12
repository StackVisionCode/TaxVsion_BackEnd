using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaxVision.Subscription.Infrastructure.Catalog;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Tests.TestDoubles;
using Wolverine;

namespace TaxVision.Subscription.Tests.Architecture;

/// <summary>
/// Host real de Subscription solo para leer las rutas de Wolverine: base en memoria (los seeders corren al
/// arrancar), sin Rabbit, sin persistencia de mensajes y sin jobs. No se conecta a nada, así que corre en el gate.
/// </summary>
public sealed class SubscriptionRoutingHostFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"subscription-routing-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Server=127.0.0.1,1;Database=unused;Trusted_Connection=False");
        builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1");
        builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<SubscriptionDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<SubscriptionDbContext>>();
            services.AddDbContext<SubscriptionDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.DisableAllExternalWolverineTransports();
            services.DisableAllWolverineMessagePersistence();
            // Sin base ni Redis de verdad: las revisiones del catálogo tienen sus pruebas de integración.
            services.AddSingleton<ICatalogRevisionRunner, SkipCatalogRevisions>();
            foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
            {
                if (hosted.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal) == true)
                    services.Remove(hosted);
            }
        });
    }
}
