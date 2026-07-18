using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using TaxVision.PaymentApp.Api.Authorization;
using TaxVision.PaymentApp.Api.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Infrastructure;
using TaxVision.PaymentApp.Infrastructure.Observability;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Scheduling;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging estructurado (Serilog → OTLP/Loki) ----------
builder.Host.UseTaxVisionSerilog("payment-app-service");

builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks (correlación + tenant context) ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddPaymentAppInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "payment-app-service", PaymentAppMetrics.MeterName);
builder.Services.AddRedisCache(builder.Configuration);

// Autorización por permiso ([HasPermission("payment_app.*")]); los admins pasan siempre.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// Rate limiter para /webhooks/*: 1000 req/min por IP (§28.4/§K.1 del diseño) — deja pasar
// reintentos legítimos del provider sin abrir la puerta a un flood.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "webhooks",
        context =>
        {
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: client,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1000,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            );
        }
    );
});

// Resuelve pagos atascados en Processing tras una caída a mitad de cobro (§B.6).
builder.Services.AddHostedService<PendingChargeReconciliationJob>();

// Reintenta cobros Failed con backoff hasta agotar el retry (§C.1).
builder.Services.AddHostedService<DunningJob>();

// Avisa 30 días antes de que un método de pago guardado venza (§D.5).
builder.Services.AddHostedService<ExpiringPaymentMethodsJob>();

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

var redisEndpoint = (builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379").Split(':');

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<PaymentAppDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"])
    .AddCheck("redis", new TcpEndpointHealthCheck(redisEndpoint[0], int.Parse(redisEndpoint[1])), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(ChargeSaaSPaymentCommand).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, PaymentAppDbContext>();
    options.Policies.AutoApplyTransactions();

    // Eventos propios: resultado de un cobro de renovación (suscripción base, seat, add-on).
    options.PublishMessage<SubscriptionRenewalPaymentSucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionRenewalPaymentFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatRenewalPaymentSucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatRenewalPaymentFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<AddOnRenewalPaymentSucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<AddOnRenewalPaymentFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options
        .PublishMessage<SubscriptionPlanChangePaymentSucceededIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionPlanChangePaymentFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SaaSPaymentMethodExpiringSoonIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // Consume TenantCreated/TenantStatusChanged (proyección local) y
    // SubscriptionRenewalDue/SeatRenewalDue/AddOnRenewalDue/SubscriptionPlanChangeDue
    // (intents de cobro) del exchange fan-out.
    options
        .ListenToRabbitQueue(
            "payment-app-events",
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "PaymentApp API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<SessionDenylistMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<JwtTenantContextMiddleware>();
app.UseMiddleware<TenantStatusGateMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();
