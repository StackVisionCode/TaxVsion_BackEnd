using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
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
using TaxVision.Documents.Api.Common;
using TaxVision.Documents.Infrastructure;
using TaxVision.Documents.Infrastructure.Observability;
using TaxVision.Documents.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseTaxVisionSerilog("documents-service");

builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddBuildingBlocks();
builder.Services.AddDocumentsInfrastructure(builder.Configuration);

// BB-09 — Documents usaba IConnectionMultiplexer a pelo para IRateCounter y se quedaba sin
// ICacheService, así que registrar aquí CachedTenantPlanCodeReader o HttpPlanRateLimitReader
// reventaba en runtime. Reutiliza la misma ConnectionStrings:Redis que ya exige el rate limiting.
builder.Services.AddRedisCache(builder.Configuration);

// H-02 — el reader de la denylist necesita el ICacheService de la línea de arriba.
builder.Services.AddSessionDenylist(builder.Configuration);

builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "documents-service", DocumentsMetrics.MeterName);

builder.Services.Configure<AuthorizationOptions>(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// Autorización por permiso humano ([HasPermission("documents.*")]). Resuelve las políticas perm:* .
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// Endpoints M2M internos: solo tokens de servicio (actor_type=Service). Mismo patrón que Customer.
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

// H-05 — fuente de permisos de la Capa 2. Revienta al arrancar si hay endpoints con
// [HasPermission] y la config no pide "Projection": el claim `perm` ya no se emite (Fase
// 7.5.10), así que en modo Jwt esos endpoints darían 403 siempre, en silencio.
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// Rate limiting tiered (Fase 4.16 del plan). Documents no tenía ningún rate limiting propio antes
// de esta fase — se agrega IConnectionMultiplexer/IRateCounter desde cero, mismo patrón que
// Growth/PaymentClient/Tenant/etc.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// Auditoria RateLimit hallazgo #2 — Documents ganó un IServiceTokenAcquirer M2M dedicado
// (ver Infrastructure/RateLimiting/ServiceTokenAcquirer.cs) solo para que
// HttpPlanRateLimitReader pueda leer el catálogo de Subscription; la cuota ahora sí escala
// por plan en vez de caer siempre a NullPlanRateLimitReader/BaseQuota.
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
    .AddDbContextCheck<DocumentsDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(Assembly.GetExecutingAssembly());
    options.Discovery.IncludeAssembly(Assembly.Load("TaxVision.Documents.Application"));
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConnection =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConnection, DocumentsSchemas.Integration);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, DocumentsDbContext>();
    options.Policies.AutoApplyTransactions();

    options
        .ListenToRabbitQueue("documents-events", queue => queue.BindExchange("taxvision-events", string.Empty))
        .UseDurableInbox();

    // Eventos que Documents publica al bus compartido (guardrail #13: routing explícito por tipo).
    options.PublishMessage<DocumentGenerationStartedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<DocumentGenerationCompletedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<DocumentGenerationFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<DocumentStoredIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Pedido de guardado a CloudStorage (sube al bucket temporal y publica esto para que lo almacene).
    // Se publica a la cola DEDICADA "cloudstorage-external-uploads" (default exchange, routingKey = nombre de
    // cola), que CloudStorage escucha con DefaultIncomingMessage<SaveFileRequestedIntegrationEvent> — es la ruta
    // purpose-built para este único evento. Enviarlo al fanout "taxvision-events" también funciona (el handler
    // SaveFileFromSourceHandler es global), pero la cola dedicada es point-to-point y evita ruido en el fanout.
    // Verificado E2E: PDF factura → CloudStorage File(Available) → Billing PdfFileId. Ver CloudStorage.Api
    // Program.cs líneas 186-196.
    options.PublishMessage<SaveFileRequestedIntegrationEvent>().ToRabbitQueue("cloudstorage-external-uploads");

    options.ApplyStandardFailurePolicies();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Documents API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<JwtTenantContextMiddleware>();
app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program { }
