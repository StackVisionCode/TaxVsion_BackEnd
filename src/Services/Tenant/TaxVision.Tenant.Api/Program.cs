using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
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
using Serilog;
using StackExchange.Redis;
using TaxVision.Tenant.Api.Common;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Infrastructure;
using TaxVision.Tenant.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseTaxVisionSerilog("tenant-service");

// 1) Structured loggin with Serilog
// builder.Host.UseSerilog((context, logger) => logger
//     .ReadFrom.Configuration(context.Configuration)
//     .Enrich.FromLogContext()
//     .Enrich.WithProperty("service", "tenant-service")
//     .WriteTo.Console()
//     .WriteTo.File(
//         Path.Combine(AppContext.BaseDirectory, "Logs", "tenant-.log"),
//         rollingInterval: RollingInterval.Day,
//         retainedFileCountLimit: 30));
builder.Services.AddSwaggerGen();
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();
builder.Services.AddOpenApi();

// 2) Services Shared plus Services's Infrastructure
builder.Services.AddBuildingBlocks();

//  Added Cache's Services
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTenantInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "tenant-service");

// Autorización por permiso ([HasPermission(...)], ver TenantBrandingController) — mismo mecanismo
// que Postmaster/Signature/Notification/Customer. Coexiste con las policies nombradas de abajo:
// PermissionPolicyProvider solo intercepta el prefijo "perm:", el resto cae al provider default.
// BuildingBlocks.ActorTypeAuthorization — Fase 3 del plan de autorización por actor type,
// reemplaza a la copia local que tenía este servicio. TenantController.Create es un gap conocido
// y deliberadamente diferido: el ticket de registro firmado (ver EffectiveTenantRegistrationResolver)
// no lleva claim actor_type por diseño (es un "capability token" de un solo uso, no una identidad
// persistente — mismo patrón que Auth0 Tickets API / OAuth authorization code), así que hoy solo
// PlatformAdmin pasa el nuevo filtro; el flujo de self-registration vía ticket queda bloqueado hasta
// que se agregue un mecanismo de opt-out explícito para este tipo de token (decisión pendiente,
// post Fase 3).
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// H-05 — fuente de permisos de la Capa 2. Revienta al arrancar si hay endpoints con
// [HasPermission] y la config no pide "Projection": el claim `perm` ya no se emite (Fase
// 7.5.10), así que en modo Jwt esos endpoints darían 403 siempre, en silencio.
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// Acepta el ticket firmado por Auth (ReserveSubdomainHandler, claim reg_slug) o un
// PlatformAdmin creando un tenant directamente — ver TenantController.Create.
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy(
        "TenantRegistration",
        policy =>
            policy.RequireAssertion(context =>
                context.User.HasClaim("purpose", "tenant-registration") || context.User.IsInRole("PlatformAdmin")
            )
    )
    // PayFlow (Fase 14) — M2M desde Auth para chequear disponibilidad de subdominio
    // (GET internal/tenants/subdomain-available) durante el registro post-pago.
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

// Rate limiting por tenant/usuario (Fase 4.2 del plan) — reemplaza el AddRateLimiter nativo de
// ASP.NET Core que tenía este servicio (una sola policy, "tenant-logo-upload") por el mismo
// [RateLimit]/IRateCounter tiered que ya corre en Customer desde Fase 3. La policy
// "tenant-registration" (IP, 5/min sobre POST /tenants) sigue sin existir acá — Fase 0.5 la dejó
// solo en el Gateway (RateLimitingRegistration.cs), ver el [RateLimitExempt] de
// TenantController.Create.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// RateLimit Fase 2 — piloto Customer (Fase 6) extendido a Tenant. Flag OFF por default (fail-open
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

var tenantRabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);
var tenantRedis = HostPort.Parse(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", 6379);
builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<TenantDbContext>("sql-server", tags: ["ready"])
    .AddCheck("redis", new TcpEndpointHealthCheck(tenantRedis.Host, tenantRedis.Port), tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(tenantRabbitUri.Host, tenantRabbitUri.Port), tags: ["ready"]);

// 3)
builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(CreateTenantHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var rabbitUri =
        builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.");

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(new Uri(rabbitUri)).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, TenantDbContext>();
    options.Policies.AutoApplyTransactions();

    options.PublishMessage<TenantCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantStatusChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SaveFileRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantLogoUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantLogoRemovedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // PayFlow (Fase 16) — publicado por InternalTenantProvisioningController.
    options.PublishMessage<TenantCreatedForOnboardingIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // Sin una cola de entrada bindeada al fanout "taxvision-events", Wolverine descubre el
    // handler en el assembly (Discovery.IncludeAssembly de arriba) pero no tiene de donde
    // recibir el mensaje — TenantBrandingFileScanResultConsumer nunca correría.
    options
        .ListenToRabbitQueue(
            "tenant-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    options.ApplyStandardFailurePolicies();
});
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(a =>
    {
        a.SwaggerEndpoint("/openapi/v1.json", "API v1");
    });
}

//4) Middleware's Pipe Line (the order of this, it's important)
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();

// Tenant no tiene entidades ITenantOwned (ES el registro de tenants), así que hoy nada consume
// el TenantContext que llena este middleware. Se mantiene por consistencia con los demás
// servicios y para que el orden ya sea correcto si aparece un consumer de Projection.
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();
app.Run();

public partial class Program;
