using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Health;
using BuildingBlocks.Web.Hosting;
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
using TaxVision.Campaigns.Application;
using TaxVision.Campaigns.Infrastructure;
using TaxVision.Campaigns.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseTaxVisionSerilog("campaigns-service");

// ---------- MVC + JSON ----------
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks + Infrastructure + Auth + OTEL ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddCampaignsInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "campaigns-service");

// Autorización por permiso ([HasPermission("campaigns.manage")]); los admins pasan siempre.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// Gate de módulo (module.campaigns): fuente de entitlements desde la proyección local
// (EfTenantEntitlementModulesReader, alimentada por TenantEntitlementsChangedIntegrationEvent de
// Subscription), igual que Notes/Reminder/Tasks.
builder.Services.AddScoped<
    BuildingBlocks.Web.ActorTypeAuthorization.ITenantModuleEntitlementsSource,
    BuildingBlocks.Web.ActorTypeAuthorization.TenantModuleEntitlementsSource
>();

// Fuente de permisos de la Capa 2 (modo Projection). Requiere Authorization:PermissionsSource=Projection
// + IUserPermissionsProjectionReader registrado (proyección local en Infrastructure) + IMemoryCache.
builder.Services.AddMemoryCache();
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// Rate limiting por tenant/usuario.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// Tier-aware quotas. Flag OFF por default (fail-open a la cuota base sin escalar, vía los readers Null
// de AddTieredRateLimiting); con RateLimit:EnforceTierQuotas se enchufan los lectores reales
// (proyección local de PlanCode + catálogo de Subscription), registrados en AddRateLimitTierQuotas.
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

// ---------- Health checks ----------
var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<CampaignsDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(AssemblyMarker).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CampaignsDbContext>();
    options.Policies.AutoApplyTransactions();

    options.ApplyStandardFailurePolicies();

    // PUBLICADOS (guardrail 13) — un PublishMessage<T> por CADA evento que se publica; sin esta línea
    // Wolverine descarta el evento en silencio (nunca sale del outbox). Contrato dispatch/result +
    // ciclo de vida del run (BuildingBlocks.Messaging.CampaignsIntegrationEvents).
    options.PublishMessage<CampaignDispatchRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CampaignDispatchResultIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CampaignRunStartedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CampaignRunCompletedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // Restaura BuildingBlocks.Web.Tenancy.TenantContext dentro del scope que Wolverine crea por handler.
    options
        .Policies.ForMessagesOfType<IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

    // Cola propia desde el arranque (aunque todavía no haya consumers) — mismo patrón que Notes/Scribe.
    options
        .ListenToRabbitQueue("campaigns-events", queue => queue.BindExchange("taxvision-events", string.Empty))
        .UseDurableInbox();
});

builder.Services.AddTaxVisionClientIpForwarding(builder.Configuration);

var app = builder.Build();

// IP real del cliente detras de Cloudflare/Caddy/Gateway (compartido) — primer middleware.
app.UseTaxVisionClientIp();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Campaigns API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();

// El tenant se resuelve SOLO del claim tenant_id del JWT verificado. ANTES de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

public partial class Program;
