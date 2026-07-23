using System.Text.Json.Serialization;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using BuildingBlocks.Web.Session;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using TaxVision.Postmaster.Api.Jobs;
using TaxVision.Postmaster.Application;
using TaxVision.Postmaster.Infrastructure;
using TaxVision.Postmaster.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseTaxVisionSerilog("postmaster-service");

// ---------- MVC + JSON ----------
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks + Infrastructure + Auth + OTEL ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddPostmasterInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "postmaster-service");

// Autorización por permiso ([HasPermission("postmaster.*")]); los admins pasan siempre.
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

// M2M interno (D3 Compose Fase 5) — solo Correspondence (u otro microservicio backend), nunca un
// usuario humano. Mismo patrón que Connectors (claim actor_type=Service emitido por Auth vía
// client_credentials).
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

// Fase 4 — limpieza horaria de reservas de idempotencia expiradas.
builder.Services.AddHostedService<IdempotencyCleanupJob>();

// ---------- Health checks ----------
var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<PostmasterDbContext>("sql-server", tags: ["ready"])
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
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, PostmasterDbContext>();
    options.Policies.AutoApplyTransactions();

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));

    // RBAC Fase 5 — restaura BuildingBlocks.Tenancy.TenantContext dentro del scope que Wolverine
    // crea para cada handler (bus.InvokeAsync local o consumer de integration event).
    options
        .Policies.ForMessagesOfType<BuildingBlocks.Messaging.IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Tenancy.LocalCommandTenantMiddleware));

    // Fase 3.5 — ningún handler la publica todavía (llega en Fase 5 vía el consumer), pero se
    // declara ya para que el binding exista en Rabbit desde el arranque.
    options
        .PublishMessage<PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");

    // El consumer de notifications.email_send_requested.v1 se agrega en Fase 5.
    // La cola/binding se declara ya en Fase 1 para que exista en Rabbit desde el arranque.
    options
        .ListenToRabbitQueue(
            "postmaster-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Postmaster API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();

// RBAC Fase 5 — reemplaza TenantResolutionMiddleware (leía el tenant de un header
// X-Tenant-Id sin validar, confiando en el caller — inseguro) por el middleware compartido
// que resuelve el tenant SOLO del claim tenant_id del JWT verificado. RBAC Fase 7 hotfix
// (2026-07-22): va ANTES de UseAuthorization() — en modo Projection, [HasPermission] necesita
// el tenant ya poblado durante su propia evaluación, que corre dentro de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();
