using System.Text.Json.Serialization;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.ScribeIntegrationEvents;
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
using TaxVision.Scribe.Application;
using TaxVision.Scribe.Infrastructure;
using TaxVision.Scribe.Infrastructure.Persistence;
using TaxVision.Scribe.Infrastructure.Retention;
using TaxVision.Scribe.Infrastructure.Seed;
using TaxVision.Scribe.Infrastructure.Startup;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseTaxVisionSerilog("scribe-service");

// ---------- MVC + JSON ----------
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks + Infrastructure + Auth + OTEL ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddScribeInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "scribe-service");

// Autorización por permiso ([HasPermission("scribe.*")]); los admins pasan siempre.
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

// Sube y publica los layouts base system-base/tenant-base (Fase 4.6) si todavía no existen.
builder.Services.AddHostedService<ScribeBaseLayoutSeeder>();

// Fase 8: siembra los 13 templates migrados desde Notification + sus EventTemplateMapping.
// Corre después del seeder de layouts — IHostedService.StartAsync se ejecuta en orden de registro.
builder.Services.AddHostedService<ScribeNotificationTemplateSeeder>();

// Sube el logo de header de plataforma (Assets/SystemLogo/deploy.png) y persiste el FileId en
// SystemAssetRef — reemplaza la config estática Scribe:SystemAssets.
builder.Services.AddHostedService<ScribeSystemAssetSeeder>();

// Precarga en cache (L1+L2) todo template Published (Fase 6) — igual que los 3 seeders de
// arriba, difiere su ejecución a ApplicationStarted (ver DeferredStartupHostedService) porque pide
// un token M2M a auth-api y templates a cloudstorage-api; corriendo antes se puede perder la
// carrera de arranque de contenedores contra esos dos servicios.
builder.Services.AddHostedService<TemplateWarmupService>();

// Fase 10: retention job — purga versiones Archived viejas. Deshabilitado por default
// (Scribe:Retention:Enabled) hasta que se autorice explícitamente.
builder.Services.Configure<ScribeRetentionOptions>(
    builder.Configuration.GetSection(ScribeRetentionOptions.SectionName)
);
builder.Services.AddHostedService<ScribeRetentionScheduler>();

// ---------- Health checks ----------
var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<ScribeDbContext>("sql-server", tags: ["ready"])
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
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, ScribeDbContext>();
    options.Policies.AutoApplyTransactions();

    // Consume TenantLogoUpdated/Removed (Fase 4.5 — logo pipeline).
    options
        .ListenToRabbitQueue(
            "scribe-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    // Publica los eventos propios del microservicio al exchange fan-out del ecosistema.
    options.PublishMessage<ScribeTenantLogoMissingDetectedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Las subidas de templates/layouts se hacen primero a MinIO y CloudStorage debe
    // catalogarlas antes de que Scribe publique la version. Sin esta ruta Wolverine
    // no envia SaveFileRequestedIntegrationEvent y el archivo nunca existe en Files.
    options.PublishMessage<SaveFileRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));

    // RBAC Fase 5 — restaura BuildingBlocks.Tenancy.TenantContext dentro del scope que Wolverine
    // crea para cada handler (bus.InvokeAsync local o consumer de integration event).
    options
        .Policies.ForMessagesOfType<BuildingBlocks.Messaging.IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Tenancy.LocalCommandTenantMiddleware));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Scribe API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();

// RBAC Fase 5 — reemplaza TenantResolutionMiddleware (leía el tenant de un header X-Tenant-Id
// sin validar, confiando en el caller — inseguro) por el middleware compartido que resuelve el
// tenant SOLO del claim tenant_id del JWT verificado. Los tokens M2M (RenderController,
// ActorType.Service) no llevan ese claim y pasan sin tenant seteado — el render pipeline resuelve
// el tenantId explícito por parámetro, ver comentarios de IgnoreQueryFilters en los repos.
// RBAC Fase 7 hotfix (2026-07-22): va ANTES de UseAuthorization() — en modo Projection,
// [HasPermission] necesita el tenant ya poblado durante su propia evaluación, que corre dentro
// de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();
