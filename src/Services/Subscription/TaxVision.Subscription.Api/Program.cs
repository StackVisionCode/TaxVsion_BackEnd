using System.Text.Json.Serialization;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using BuildingBlocks.Web.Session;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using TaxVision.Subscription.Infrastructure;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Scheduling;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging estructurado (Serilog → OTLP/Loki) ----------
builder.Host.UseTaxVisionSerilog("subscription-service");

builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks (correlación + tenant context) ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddSubscriptionInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "subscription-service");
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);

// Jobs de renovacion/expiracion/grace (Fase 4). Cada uno es independiente: renovar la
// suscripcion base no renueva seats ni add-ons, y viceversa (ver diseno §34).
builder.Services.AddHostedService<TenantSubscriptionRenewalJob>();
builder.Services.AddHostedService<SeatRenewalJob>();
builder.Services.AddHostedService<AddOnRenewalJob>();
builder.Services.AddHostedService<TrialExpirationJob>();
builder.Services.AddHostedService<GracePeriodExpirationJob>();
builder.Services.AddHostedService<SubscriptionExpirationJob>();
builder.Services.AddHostedService<SeatExpirationJob>();
builder.Services.AddHostedService<AddOnExpirationJob>();
builder.Services.AddHostedService<RenewalNotificationJob>();

// Los downgrades agendados (PendingDowngrade) los aplica TenantSubscriptionRenewalJob mismo,
// justo antes de facturar la renovación — no hay un job separado.

// Solo llamadas service-to-service (Auth consultando /internal/users/{id}/access) pasan
// esta policy. No se expone vía gateway público.
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

// RBAC Fase 8 (RBAC_Hardening_Plan.md) — primera vez que Subscription usa [HasPermission]. La
// tabla UserPermissionsProjection + su repo/consumers ya existían desde RBAC Fase 7 (sembrada
// pero sin wiring de enforcement, ver README §41.10) — solo faltaba este bloque, idéntico al de
// los otros 13 servicios (mismo criterio que CloudStorage/Program.cs).
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddMemoryCache();
if (builder.Configuration["Authorization:PermissionsSource"] == "Projection")
    builder.Services.AddScoped<IUserPermissionsSource, ProjectionPermissionsSource>();
else
    builder.Services.AddScoped<IUserPermissionsSource, JwtEmbeddedPermissionsSource>();

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

var redisEndpoint = (builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379").Split(':');

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<SubscriptionDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"])
    .AddCheck("redis", new TcpEndpointHealthCheck(redisEndpoint[0], int.Parse(redisEndpoint[1])), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(ChangePlanHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, SubscriptionDbContext>();
    options.Policies.AutoApplyTransactions();

    // Eventos publicados hacia Auth (límites), CloudStorage, Communication y demás
    // servicios. TenantEntitlementsChangedIntegrationEvent es el único evento de "algo
    // cambió en la suscripción" — reemplaza a los antiguos Activated/PlanChanged/
    // Suspended/SeatsPurchased (retirados en la fase de cleanup).
    options.PublishMessage<TenantEntitlementsChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatAssignedToUserIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatReleasedFromUserIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<AddOnActivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<AddOnCancelledIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionRenewalDueIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionPlanChangeDueIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatRenewalDueIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<AddOnRenewalDueIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionRenewalUpcomingIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatRenewalUpcomingIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // Consume TenantCreated (alta de suscripción trial).
    options
        .ListenToRabbitQueue(
            "subscription-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

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

await using (var seedScope = app.Services.CreateAsyncScope())
{
    var seedDb = seedScope.ServiceProvider.GetRequiredService<SubscriptionDbContext>();
    await SubscriptionPlanCatalogSeeder.SeedAsync(seedDb, CancellationToken.None);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Subscription API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
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
