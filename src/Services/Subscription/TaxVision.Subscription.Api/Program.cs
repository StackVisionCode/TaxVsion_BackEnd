using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Health;
using BuildingBlocks.Web.Middleware;
using BuildingBlocks.Web.Observability;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Security;
using BuildingBlocks.Web.Session;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using StackExchange.Redis;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using TaxVision.Subscription.Infrastructure;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Scheduling;
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
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks (correlación + tenant context) ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddSubscriptionInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "subscription-service");
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);

// Jobs de renovacion/expiracion/grace (Fase 4). Cada uno es independiente: renovar la
// suscripcion base no renueva seats ni add-ons, y viceversa (ver diseno §34).
builder.Services.AddHostedService<TenantSubscriptionRenewalJob>();
builder.Services.AddHostedService<SeatRenewalJob>();
builder.Services.AddHostedService<AddOnRenewalJob>();
builder.Services.AddHostedService<TrialExpirationJob>();
builder.Services.AddHostedService<GracePeriodExpirationJob>();
builder.Services.AddHostedService<SubscriptionExpirationJob>();
builder.Services.AddHostedService<SeatExpirationJob>();
builder.Services.AddHostedService<AddOnExpirationJob>();
builder.Services.AddHostedService<RenewalNotificationJob>();

// Los downgrades agendados (PendingDowngrade) los aplica TenantSubscriptionRenewalJob mismo,
// justo antes de facturar la renovación — no hay un job separado.

// Solo llamadas service-to-service (Auth consultando /internal/users/{id}/access) pasan
// esta policy. No se expone vía gateway público.
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// H-05 — fuente de permisos de la Capa 2. Revienta al arrancar si hay endpoints con
// [HasPermission] y la config no pide "Projection": el claim `perm` ya no se emite (Fase
// 7.5.10), así que en modo Jwt esos endpoints darían 403 siempre, en silencio.
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// Rate limiting por tenant/usuario (Fase 4.10 del plan) — arrancaba en cero, Subscription no
// tenia ningun AddRateLimiter/EnableRateLimiting nativo que preservar (los 5 endpoints exentos
// son D-category publicos sin limiter previo o M2M-only, ver doc-comment de
// RateLimitPolicyCatalog). AddRedisCache ya registra IDistributedCache pero no
// IConnectionMultiplexer — IRateCounter lo necesita directo, mismo patron que
// Signature/Correspondence.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// RateLimit Fase 2 — mismo piloto que Customer (Fase 6)/Tenant (Fase 2.1)/Connectors. Flag OFF
// por default (fail-open a la cuota base sin escalar, vía NullTenantPlanCodeReader/
// NullPlanRateLimitReader de AddTieredRateLimiting) hasta rollout coordinado.
//
// Auditoría RateLimit hallazgo #2 — IPlanRateLimitReader ya no cae en Null acá: Subscription
// resuelve su propio catálogo de PlanRateLimits directo (ScopedDirectPlanRateLimitReader, sin
// HTTP/M2M circular — ver AddRateLimitTierQuotas en SubscriptionInfrastructure.DependencyInjection).
if (builder.Configuration.GetValue<bool>("RateLimit:EnforceTierQuotas"))
{
    builder.Services.AddSingleton<
        BuildingBlocks.RateLimiting.ITenantPlanCodeReader,
        BuildingBlocks.Infrastructure.RateLimiting.ScopedTenantPlanCodeReader
    >();
    builder.Services.AddSingleton<
        BuildingBlocks.RateLimiting.IPlanRateLimitReader,
        TaxVision.Subscription.Infrastructure.RateLimiting.ScopedDirectPlanRateLimitReader
    >();
}
builder.Services.AddTieredRateLimiting();

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

var redisEndpoint = (builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379").Split(':');

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<SubscriptionDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"])
    .AddCheck("redis", new TcpEndpointHealthCheck(redisEndpoint[0], int.Parse(redisEndpoint[1])), tags: ["ready"]);

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

    // Eventos publicados hacia Auth (límites), CloudStorage, Communication y demás
    // servicios. TenantEntitlementsChangedIntegrationEvent es el único evento de "algo
    // cambió en la suscripción" — reemplaza a los antiguos Activated/PlanChanged/
    // Suspended/SeatsPurchased (retirados en la fase de cleanup).
    options.PublishMessage<TenantEntitlementsChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatAssignedToUserIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatReleasedFromUserIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<AddOnActivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<AddOnCancelledIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionRenewalDueIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionPlanChangeDueIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatRenewalDueIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<AddOnRenewalDueIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionRenewalUpcomingIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatRenewalUpcomingIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // PayFlow (Fase 16) — publicado por InternalSubscriptionActivationController.
    options.PublishMessage<SubscriptionActivatedForOnboardingIntegrationEvent>().ToRabbitExchange("taxvision-events");

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

    options.ApplyStandardFailurePolicies();

    // RBAC Fase 5 — restaura BuildingBlocks.Web.Tenancy.TenantContext dentro del scope que Wolverine
    // crea para cada handler (bus.InvokeAsync local o consumer de integration event).
    options
        .Policies.ForMessagesOfType<BuildingBlocks.Messaging.IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));
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

// Resuelve el tenant solo del claim tenant_id del JWT verificado. Va antes de
// UseAuthorization: en modo Projection, [HasPermission] consulta una proyección tenant-scoped
// durante su propia evaluación, que corre dentro de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();

public partial class Program;
