using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using TaxVision.Subscription.Infrastructure;
using TaxVision.Subscription.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging estructurado (Serilog → OTLP/Loki) ----------
builder.Host.UseTaxVisionSerilog("subscription-service");

builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks (correlación + tenant context) ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddSubscriptionInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "subscription-service");

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<SubscriptionDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(ChangePlanHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, SubscriptionDbContext>();
    options.Policies.AutoApplyTransactions();

    // Eventos publicados hacia Auth (límites) y demás servicios.
    options.PublishMessage<SubscriptionActivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionPlanChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionSuspendedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatsPurchasedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // Consume TenantCreated (alta de suscripción trial).
    options
        .ListenToRabbitQueue(
            "subscription-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
});

var app = builder.Build();

await using (var seedScope = app.Services.CreateAsyncScope())
{
    var seedDb = seedScope.ServiceProvider.GetRequiredService<SubscriptionDbContext>();
    await SubscriptionPlanCatalogSeeder.SeedAsync(seedDb, CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Subscription API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();
