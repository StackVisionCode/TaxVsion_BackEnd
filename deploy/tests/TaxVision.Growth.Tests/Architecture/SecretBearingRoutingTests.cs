using System.Runtime.CompilerServices;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxVision.Growth.Infrastructure.Persistence;
using Wolverine.Runtime;

namespace TaxVision.Growth.Tests.Architecture;

/// <summary>
/// Los eventos con el código canjeable en claro tienen un solo consumidor (Notification): van directo a su cola,
/// nunca al fanout que reciben todos los servicios. Se mide sobre el runtime real.
/// </summary>
public sealed class SecretBearingRoutingTests(GrowthRoutingHostFactory factory)
    : IClassFixture<GrowthRoutingHostFactory>
{
    // Program siembra el programa de referidos al arrancar: base en memoria para no tocar SQL.
    private readonly WebApplicationFactory<Program> _host = factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<GrowthDbContext>>();
            services.RemoveAll<DbContextOptions<GrowthDbContext>>();
            services.AddDbContext<GrowthDbContext>(options => options.UseInMemoryDatabase("growth-routing"));
        })
    );

    public static TheoryData<Type> SecretBearingEvents => [typeof(GiftcardShareRequestedIntegrationEvent)];

    [Theory]
    [MemberData(nameof(SecretBearingEvents))]
    public void Messages_that_carry_a_secret_go_only_to_the_notification_queue(Type eventType)
    {
        var runtime = _host.Services.GetRequiredService<IWolverineRuntime>();
        var message = RuntimeHelpers.GetUninitializedObject(eventType);

        var destinations = runtime
            .RoutingFor(eventType)
            .RouteForPublish(message, null)
            .Select(envelope => envelope.Destination?.ToString() ?? string.Empty)
            .ToList();

        var routes = string.Join(", ", destinations);
        Assert.True(
            destinations is ["rabbitmq://queue/notification-events"],
            $"{eventType.Name} must go only to the notification-events queue. Routes: {routes}"
        );
        Assert.DoesNotContain(destinations, d => d.Contains("taxvision-events", StringComparison.OrdinalIgnoreCase));
    }
}
