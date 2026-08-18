using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
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
using TaxVision.Tasks.Application;
using TaxVision.Tasks.Infrastructure;
using TaxVision.Tasks.Infrastructure.Observability;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseTaxVisionSerilog("tasks-service");

// ---------- MVC + JSON ----------
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks + Infrastructure + Auth + OTEL ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddTasksInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);

// TaskMetrics.MeterName va como meter adicional: AddTaxVisionOpenTelemetry solo registra
// AddMeter(serviceName), y un Meter propio no declarado acá no exporta nada — los contadores suben
// en memoria y el panel queda vacío sin ningún error.
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "tasks-service", TaskMetrics.MeterName);

// Autorización por permiso: [HasPermission("tasks.read")]. Los admins pasan siempre.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// Revienta al arrancar si hay endpoints con [HasPermission] y la config no pide "Projection".
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// ---------- Rate limiting ----------
// AddTieredRateLimiting necesita el multiplexer crudo; AddRedisCache solo aporta IDistributedCache.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);

// Con EnforceTierQuotas apagado se cae a los Null*Reader y todo aplica la cuota base sin escalar
// (fail-open). Los lectores reales son Scoped* porque leen el DbContext y el HttpClient por request,
// mientras que el resolver que los consume es Singleton.
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
    .AddDbContextCheck<TasksDbContext>("sql-server", tags: ["ready"])
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
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, TasksDbContext>();
    options.Policies.AutoApplyTransactions();

    options.ApplyStandardFailurePolicies();

    // Restaura el TenantContext dentro del scope que Wolverine crea para cada handler.
    options
        .Policies.ForMessagesOfType<IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

    // Un PublishMessage<T>() por cada PublishAsync. Falta uno y ese evento nunca sale del outbox.
    options.PublishMessage<TaskCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskAssignedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskCompletedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskReopenedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskCancelledIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskDueChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskUnblockedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskWaitingOnClientIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskAttachmentAddedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskAttachmentDetachedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskAttachmentRejectedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TaskOverdueIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ClientRequestCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ClientRequestFulfilledIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ClientRequestDocumentRejectedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // Contratos de Reminder, no de Task: los define Reminder y Task sólo los publica.
    options.PublishMessage<ReminderRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ReminderTargetMovedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ReminderTargetClosedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // Cola propia: por acá entran los eventos de Auth (RBAC), Subscription (plan del tenant),
    // Customer y CloudStorage.
    options
        .ListenToRabbitQueue("tasks-events", queue => queue.BindExchange("taxvision-events", string.Empty))
        .UseDurableInbox();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Tasks API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();

// El tenant sale solo del claim tenant_id del JWT verificado. Va antes de UseAuthorization():
// [HasPermission] necesita el tenant poblado durante su propia evaluación.
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

public partial class Program;
