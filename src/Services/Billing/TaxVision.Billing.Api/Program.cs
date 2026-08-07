using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Infrastructure.RateLimit;
using BuildingBlocks.Messaging;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using BuildingBlocks.Web.RateLimiting;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using StackExchange.Redis;
using TaxVision.Billing.Api.Common;
using TaxVision.Billing.Infrastructure;
using TaxVision.Billing.Infrastructure.Observability;
using TaxVision.Billing.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseTaxVisionSerilog("billing-service");

builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddBuildingBlocks();
builder.Services.AddBillingInfrastructure(builder.Configuration);

// Emisor de las facturas de onboarding (pre-tenant) = la plataforma. Config Billing:PlatformIssuer.
builder
    .Services.AddOptions<TaxVision.Billing.Application.Invoices.IntegrationEvents.PlatformIssuerOptions>()
    .Bind(builder.Configuration.GetSection(
        TaxVision.Billing.Application.Invoices.IntegrationEvents.PlatformIssuerOptions.SectionName
    ));

// RateLimit Fase 2 — CachedTenantPlanCodeReader (5 min TTL) y HttpPlanRateLimitReader (catálogo
// de Subscription, cacheado 5 min) dependen de ICacheService. Billing ya requiere
// ConnectionStrings:Redis para IRateCounter (línea abajo); AddRedisCache reutiliza esa misma
// conexión para el ICacheService compartido, mismo patrón que Tenant/Customer.
builder.Services.AddRedisCache(builder.Configuration);

builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "billing-service", BillingMetrics.MeterName);

builder.Services.Configure<AuthorizationOptions>(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// Rate limiting por tenant/usuario (Fase 4.11 del plan) — arrancaba en cero, Billing no tenia
// ningun AddRateLimiter/EnableRateLimiting nativo ni Redis/IConnectionMultiplexer que
// preservar (los 7 endpoints son todos staff-only autenticados, sin M2M/publico/webhook).
// Mismo [RateLimit]/IRateCounter tiered que ya corre en el resto del monorepo desde Fase
// 3/4.2, mismo patron de wiring que Correspondence Fase 4.9/Subscription Fase 4.10.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// RateLimit Fase 2 — piloto Tenant/Customer extendido a Billing. Flag OFF por default (fail-open
// a la cuota base sin escalar, vía NullTenantPlanCodeReader/NullPlanRateLimitReader de
// AddTieredRateLimiting) hasta rollout coordinado.
if (builder.Configuration.GetValue<bool>("RateLimit:EnforceTierQuotas"))
{
    builder.Services.AddSingleton<
        BuildingBlocks.RateLimiting.ITenantPlanCodeReader,
        BuildingBlocks.Infrastructure.RateLimiting.ScopedTenantPlanCodeReader
    >();
    builder.Services.AddSingleton<
        BuildingBlocks.RateLimiting.IPlanRateLimitReader,
        BuildingBlocks.Infrastructure.RateLimiting.ScopedPlanRateLimitReader
    >();
}
builder.Services.AddTieredRateLimiting();

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<BillingDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(Assembly.GetExecutingAssembly());
    options.Discovery.IncludeAssembly(Assembly.Load("TaxVision.Billing.Application"));
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConnection =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConnection, BillingSchemas.Integration);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, BillingDbContext>();
    options.Policies.AutoApplyTransactions();

    options
        .ListenToRabbitQueue("billing-events", queue => queue.BindExchange("taxvision-events", string.Empty))
        .UseDurableInbox();

    // Restaura el TenantContext dentro del scope que Wolverine crea para cada handler: desde el
    // envelope para integration events (consumer de documents.generation.completed) y desde el
    // comando local (GenerateInvoicePdf despachado post-commit por IssueInvoice).
    options
        .Policies.ForMessagesOfType<BuildingBlocks.Messaging.IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Tenancy.LocalCommandTenantMiddleware));

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Billing API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<JwtTenantContextMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program { }
