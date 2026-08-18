using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
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
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using StackExchange.Redis;
using TaxVision.Sms.Application;
using TaxVision.Sms.Infrastructure;
using TaxVision.Sms.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseTaxVisionSerilog("sms-service");

builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddBuildingBlocks();
builder.Services.AddSmsInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);

// RBAC — [HasPermission] en MessagesController. PermissionPolicyProvider construye la policy
// "perm:sms.send" contra IUserPermissionsSource; AddUserPermissionsSource elige ProjectionPermissionsSource
// (Authorization:PermissionsSource="Projection") — un token M2M satisface el permiso vía su claim "perm"
// (bypass de actor Service), un usuario humano vía la proyección local mantenida por los eventos de Auth.
builder.Services.AddSingleton<
    Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
    PermissionPolicyProvider
>();
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// Rate limiting tiered — [RateLimit("sms.h.send")] en MessagesController (cuota por tenant/usuario).
// AddTieredRateLimiting auto-registra el contador Redis desde IConnectionMultiplexer, que hay que
// registrar explícito (AddRedisCache solo aporta IDistributedCache). La escala por plan (tier-aware,
// TenantPlanCodeProjection) llega en la Fase 2; hoy hace fail-open a la cuota base. Los webhooks
// (WebhooksController) quedan fuera: son anónimos y los cubre el gate por-IP del Gateway.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);

// RateLimit Fase 2 — escala por plan. Flag OFF por default (fail-open a la cuota base sin escalar,
// vía Null*Reader del TryAdd de AddTieredRateLimiting) hasta que un entorno provisione las credenciales
// M2M (ServiceAuthClient) + la URL del catálogo de Subscription y lo encienda. Debe ir ANTES de
// AddTieredRateLimiting para ganar el registro sobre los defaults Null.
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

builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "sms-service");

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<SmsDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(AssemblyMarker).Assembly);

    // RBAC Fase 7 — los consumers de proyección de permisos son `static class` con `Handle`
    // estático. La discovery convencional por assembly NO siempre los levanta (bug real visto en
    // PaymentApp: "No known handler for UserRolesChangedIntegrationEvent" pese a ser byte-idéntico
    // al de otro servicio que sí funcionaba). Registro explícito por tipo — IncludeType(typeof(...)),
    // no el genérico IncludeType<T>() que no compila para clases estáticas (CS0718).
    options.Discovery.IncludeType(
        typeof(TaxVision.Sms.Application.Permissions.Consumers.UserRolesChangedPermissionsProjectionConsumer)
    );
    options.Discovery.IncludeType(
        typeof(TaxVision.Sms.Application.Permissions.Consumers.RolePermissionsChangedPermissionsProjectionConsumer)
    );

    // RateLimit Fase 2 — consumer que mantiene la proyección de plan-code al día desde Subscription.
    options.Discovery.IncludeType(
        typeof(TaxVision.Sms.Application.RateLimiting.Consumers.TenantPlanCodeProjectionConsumer)
    );

    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, SmsDbContext>();
    options.Policies.AutoApplyTransactions();
    options.ApplyStandardFailurePolicies();

    // Eventos de resultado publicados (agnósticos). Sin esto, bus.PublishAsync los descarta en silencio.
    options.PublishMessage<SmsMessageAcceptedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SmsMessageDeliveredIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SmsMessageFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SmsMessageSuppressedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options
        .Policies.ForMessagesOfType<IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

    options
        .ListenToRabbitQueue("sms-events", queue => queue.BindExchange("taxvision-events", string.Empty))
        .UseDurableInbox();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "SMS API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();
app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

public partial class Program;
