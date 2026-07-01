using BuildingBlocks.Common;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using System.Text.Json.Serialization;
using TaxVision.Payment.Application.SaaSPayments.IntegrationEvents;
using TaxVision.Payment.Infrastructure;
using TaxVision.Payment.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;
using BuildingBlocks.Messaging;
using BuildingBlocks.Health;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseTaxVisionSerilog("payment-service");
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddBuildingBlocks();
builder.Services.AddPaymentInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "payment-service");

var rabbitUri = new Uri(builder.Configuration["RabbitMq:Uri"]
    ?? throw new InvalidOperationException("RabbitMq:Uri is missing."));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<PaymentDbContext>("sql-server", tags: ["ready"])
    .AddCheck(
        "rabbitmq",
        new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port),
        tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(EnrollmentPaymentRequestedHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    var rabbit = builder.Configuration["RabbitMq:Uri"]
        ?? throw new InvalidOperationException("RabbitMq:Uri is missing.");

    options.UseRabbitMq(new Uri(rabbit)).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions()
        .WithDbContextAbstraction<IUnitOfWork, PaymentDbContext>();
    options.Policies.AutoApplyTransactions();

    // Publish payment events to taxvision-events exchange
    options.PublishMessage<EnrollmentPaymentCompletedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options.PublishMessage<EnrollmentPaymentFailedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatPaymentCompletedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatPaymentFailedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatRenewalPaymentCompletedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatRenewalPaymentFailedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionRenewalPaymentCompletedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionRenewalPaymentFailedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");

    // Listen for payment requests from the taxvision-events exchange
    options.ListenToRabbitQueue("payment-service-events", queue =>
    {
        queue.BindExchange("taxvision-events", string.Empty);
    }).UseDurableInbox();

    options.Policies.OnException<Exception>()
        .RetryWithCooldown(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "Payment API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapControllers();

app.Run();
