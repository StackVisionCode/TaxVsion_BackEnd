using System.Text.Json.Serialization;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Infrastructure.RateLimit;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
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
using Serilog;
using StackExchange.Redis;
using TaxVision.Customer.Application.Customers.Commands.Create;
using TaxVision.Customer.Infrastructure;
using TaxVision.Customer.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseTaxVisionSerilog("customer-service");

// ---------- MVC + JSON ----------
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks + Infrastructure + Auth + OTEL ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddCustomerInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "customer-service");

// Autorización por permiso ([HasPermission("customers.*")]); los admins pasan siempre.
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

// M2M interno (Correspondence Fase 2) — solo otros microservicios backend, nunca un usuario
// humano. Mismo patrón que Postmaster/Connectors/Subscription (claim actor_type=Service emitido
// por Auth vía client_credentials). Ver InternalCustomersController.
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

// Rate limiting por tenant/usuario (Plan_Implementacion_Fases.md Fase 3) — reemplaza el
// FixedWindowRateLimiter local de "fiscal-reveal" (ver CustomerController.RevealTaxIdentifier,
// ahora con [RateLimit("customer.n.fiscal_reveal")]) y agrega piloto para Create/GetById.
// Requiere IRateCounter (F26) registrado por este mismo servicio — la conexión Redis a usar es
// decisión de cada microservicio.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// RateLimit Fase 6 (piloto Customer) — piloto de cuotas dinámicas por tier, mismo criterio de
// piloto-primero de Fase 3. Flag OFF por default (fail-open a la cuota base sin escalar, vía
// NullTenantPlanCodeReader/NullPlanRateLimitReader de AddTieredRateLimiting) hasta confirmar
// el comportamiento en real con el catálogo de Subscription.
if (builder.Configuration.GetValue<bool>("RateLimit:EnforceTierQuotas"))
{
    builder.Services.AddSingleton<BuildingBlocks.RateLimiting.ITenantPlanCodeReader, ScopedTenantPlanCodeReader>();
    builder.Services.AddSingleton<BuildingBlocks.RateLimiting.IPlanRateLimitReader, ScopedPlanRateLimitReader>();
}
builder.Services.AddTieredRateLimiting();

// ---------- Health checks ----------
var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<CustomerDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(CreateCustomerHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CustomerDbContext>();
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

    // Consume File* de CloudStorage (resultado del escaneo del archivo de import subido
    // directo a MinIO, ver ImportFileScanResultConsumer — Fase D para Customer).
    options
        .ListenToRabbitQueue(
            "customer-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    options.PublishMessage<CustomerArchivedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerPortalInvitationRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomersBulkImportedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerImportFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerReactivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerActivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerDeactivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerPreparerAssignedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerPreparerUnassignedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Fase D — reemplaza la tabla CustomerImportFiles: el import sube directo a MinIO y
    // publica esto para que CloudStorage lo registre/escanee de forma asincrona.
    options.PublishMessage<SaveFileRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Customer API v1"));
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
// CustomerDbContext. Va ANTES de UseAuthorization() — en modo Projection, [HasPermission]
// necesita el tenant ya poblado durante su propia evaluación, que corre dentro de
// UseAuthorization().
app.UseMiddleware<BuildingBlocks.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

// Requerido para que WebApplicationFactory<Program> (tests de integración, Fase 3 del plan de
// rate limiting) pueda referenciar este entry point desde TaxVision.Customer.Tests — Program.cs
// usa top-level statements, que generan una clase Program interna al assembly por default.
public partial class Program;
