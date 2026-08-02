using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Infrastructure.RateLimit;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Session;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using TaxVision.Connectors.Api.Options;
using TaxVision.Connectors.Application;
using TaxVision.Connectors.Application.Common;
using TaxVision.Connectors.Infrastructure;
using TaxVision.Connectors.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseTaxVisionSerilog("connectors-service");

// ---------- MVC + JSON ----------
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks + Infrastructure + Auth + OTEL ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddConnectorsInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "connectors-service");

// Autorización por permiso ([HasPermission("connectors.*")]); los admins pasan siempre.
// BuildingBlocks.ActorTypeAuthorization — Fase 3 del plan de autorización por actor type,
// reemplaza a la copia local que tenía este servicio.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// RBAC Fase 7 (RBAC_Hardening_Plan.md) -- proyeccion local de permisos para enforzar perm_v.
// Flag OFF por default (Authorization:PermissionsSource ausente o "Jwt") preserva el
// comportamiento historico (permisos embebidos en el JWT, sin chequeo de staleness).
builder.Services.AddMemoryCache();
if (builder.Configuration["Authorization:PermissionsSource"] == "Projection")
    builder.Services.AddScoped<IUserPermissionsSource, ProjectionPermissionsSource>();
else
    builder.Services.AddScoped<IUserPermissionsSource, JwtEmbeddedPermissionsSource>();

// M2M interno (Fase 8) — solo otro microservicio backend, nunca un usuario humano. Mismo patrón
// que Subscription (claim actor_type=Service emitido por Auth vía client_credentials).
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

builder.Services.Configure<GmailPushWebhookOptions>(
    builder.Configuration.GetSection(GmailPushWebhookOptions.SectionName)
);
builder.Services.Configure<ConnectorsPortalOptions>(
    builder.Configuration.GetSection(ConnectorsPortalOptions.SectionName)
);

// Webhooks públicos (Fase 7) — 100 req/min por IP, ambos endpoints no tienen sesión/tenant que particionar.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "connectors-webhook",
        context =>
        {
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: client,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            );
        }
    );
});

// RateLimit Fase 2 — mismo piloto que Customer (Fase 6) y Tenant (Fase 2.1). Flag OFF por default
// (fail-open a la cuota base sin escalar, vía NullTenantPlanCodeReader/NullPlanRateLimitReader de
// AddTieredRateLimiting) hasta rollout coordinado. Solo se registra ITenantPlanCodeReader: a
// diferencia de Customer/Tenant, Connectors todavía no tiene un acquirer de token M2M saliente
// (ver el comentario en ConnectorsInfrastructure.DependencyInjection.AddRateLimitTierQuotas), así
// que IPlanRateLimitReader queda sin override — TryAddSingleton de AddTieredRateLimiting() cae en
// NullPlanRateLimitReader (degradado pero seguro) si este flag se llegara a activar sin cerrar
// primero esa brecha.
if (builder.Configuration.GetValue<bool>("RateLimit:EnforceTierQuotas"))
{
    builder.Services.AddSingleton<
        BuildingBlocks.RateLimiting.ITenantPlanCodeReader,
        BuildingBlocks.Infrastructure.RateLimiting.ScopedTenantPlanCodeReader
    >();
}

// Rate limiting por tenant/usuario (Fase 4.8 del plan) — arrancaba en cero salvo el limiter
// nativo "connectors-webhook" de arriba (que se deja intacto, protege los 2 webhooks
// publicos). IConnectionMultiplexer/IRateCounter ya registrados en Connectors.Infrastructure
// (RedisProviderRateLimiter/D3 Fase 5) — no se duplican acá.
builder.Services.AddTieredRateLimiting();

// ---------- Health checks ----------
var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<ConnectorsDbContext>("sql-server", tags: ["ready"])
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
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, ConnectorsDbContext>();
    options.Policies.AutoApplyTransactions();

    // RBAC Fase 5 — restaura BuildingBlocks.Tenancy.TenantContext dentro del scope que Wolverine
    // crea para cada handler (bus.InvokeAsync local o consumer de integration event).
    options
        .Policies.ForMessagesOfType<BuildingBlocks.Messaging.IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Tenancy.LocalCommandTenantMiddleware));

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));

    // Sin estas reglas, Wolverine no enruta el evento a RabbitMQ y bus.PublishAsync lo descarta en
    // silencio — ningun otro servicio llega a recibirlo.
    options
        .PublishMessage<ConnectorsTenantEmailAccountConnectedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options
        .PublishMessage<ConnectorsTenantEmailAccountDisconnectedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options.PublishMessage<ConnectorsOAuthRefreshFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ConnectorsWatchExpiredIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ConnectorsRawMessageReceivedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ConnectorsMessageBodyFetchedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // Cola propia desde el arranque (Fase 1) aunque todavía no haya ningún consumer — mismo
    // patrón que Postmaster/Scribe: el binding queda listo en Rabbit antes de que exista lógica.
    options
        .ListenToRabbitQueue("connectors-events", queue => queue.BindExchange("taxvision-events", string.Empty))
        .UseDurableInbox();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Connectors API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();

// Setea BuildingBlocks.Tenancy.TenantContext desde el JWT para el HasQueryFilter global de
// ConnectorsDbContext. Va ANTES de UseAuthorization() — en modo Projection, [HasPermission]
// necesita el tenant ya poblado durante su propia evaluación, que corre dentro de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

public partial class Program;
