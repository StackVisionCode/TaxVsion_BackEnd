using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Permissions;
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

// H-05 — fuente de permisos de la Capa 2. Revienta al arrancar si hay endpoints con
// [HasPermission] y la config no pide "Projection": el claim `perm` ya no se emite (Fase
// 7.5.10), así que en modo Jwt esos endpoints darían 403 siempre, en silencio.
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

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
// AddTieredRateLimiting) hasta rollout coordinado.
//
// Auditoria RateLimit hallazgo #2 — Connectors ganó un acquirer de token M2M saliente (ver
// ConnectorsInfrastructure.DependencyInjection.AddRateLimitTierQuotas), así que ahora también
// registra IPlanRateLimitReader — la cuota escala por plan en vez de caer siempre a
// NullPlanRateLimitReader.
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

    // RBAC Fase 5 — restaura BuildingBlocks.Web.Tenancy.TenantContext dentro del scope que Wolverine
    // crea para cada handler (bus.InvokeAsync local o consumer de integration event).
    options
        .Policies.ForMessagesOfType<BuildingBlocks.Messaging.IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

    options.ApplyStandardFailurePolicies();

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

// Setea BuildingBlocks.Web.Tenancy.TenantContext desde el JWT para el HasQueryFilter global de
// ConnectorsDbContext. Va ANTES de UseAuthorization() — en modo Projection, [HasPermission]
// necesita el tenant ya poblado durante su propia evaluación, que corre dentro de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

public partial class Program;
