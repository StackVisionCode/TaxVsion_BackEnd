using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Health;
using BuildingBlocks.Web.Observability;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TaxVision.Connectors.Infrastructure.Persistence;
using TaxVision.Connectors.MailboxListener.Observability;
using Wolverine;
using Wolverine.RabbitMQ;

namespace TaxVision.Connectors.MailboxListener;

/// <summary>
/// Contenedor aparte de Connectors API: mantiene una conexión IMAP IDLE por cuenta y solo publica
/// avisos. Clase con nombre (no top-level) para no chocar con el <c>Program</c> de la API en los tests.
/// </summary>
public static class MailboxListenerProgram
{
    public const string ServiceName = "connectors-mailbox-listener";

    /// <summary>El exchange común de eventos; Connectors API lo escucha con su cola.</summary>
    public const string EventsExchange = "taxvision-events";

    public static void Main(string[] args) => Build(args).Run();

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseTaxVisionSerilog(ServiceName);

        builder.Services.AddBuildingBlocks();
        builder.Services.AddMailboxListener(builder.Configuration);
        builder.Services.AddTaxVisionOpenTelemetry(
            builder.Configuration,
            ServiceName,
            MailboxListenerMetrics.MeterName
        );

        var rabbitUri = new Uri(
            builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
        );
        builder
            .Services.AddHealthChecks()
            .AddDbContextCheck<ConnectorsDbContext>("sql-server", tags: ["ready"])
            .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

        builder.Host.UseWolverine(options =>
        {
            // Solo publica: sin handlers, sin colas propias y sin outbox (el aviso es descartable,
            // la reconciliación cubre lo que se pierda).
            options.Discovery.DisableConventionalDiscovery();
            options.UseRabbitMq(rabbitUri).AutoProvision();
            options.PublishMessage<ConnectorsMailboxChangeDetectedIntegrationEvent>().ToRabbitExchange(EventsExchange);
            options.PublishMessage<ConnectorsMailboxAuthFailedIntegrationEvent>().ToRabbitExchange(EventsExchange);
        });

        var app = builder.Build();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }
        );
        return app;
    }
}
