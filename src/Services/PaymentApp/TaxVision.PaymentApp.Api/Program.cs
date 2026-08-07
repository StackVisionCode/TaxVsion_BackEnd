using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.PaymentIntegrationEvents;
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
using TaxVision.PaymentApp.Api.Common;
using TaxVision.PaymentApp.Api.RateLimiting;
using TaxVision.PaymentApp.Application.Consumers;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Infrastructure;
using TaxVision.PaymentApp.Infrastructure.Observability;
using TaxVision.PaymentApp.Infrastructure.Persistence;
using TaxVision.PaymentApp.Infrastructure.Scheduling;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging estructurado (Serilog → OTLP/Loki) ----------
builder.Host.UseTaxVisionSerilog("payment-app-service");

builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks (correlación + tenant context) ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddPaymentAppInfrastructure(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "payment-app-service", PaymentAppMetrics.MeterName);
builder.Services.AddRedisCache(builder.Configuration);

// Autorización por permiso ([HasPermission("payment_app.*")]); los admins pasan siempre.
// BuildingBlocks.ActorTypeAuthorization — Fase 3 del plan de autorización por actor type,
// reemplaza a la copia local que tenía este servicio.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// PayFlow (Fase 8) — M2M-only: Auth's onboarding Saga (Fase 15) llama al endpoint de checkout
// inicial con un token de servicio, nunca con un JWT de tenant/usuario.
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

// H-05 — fuente de permisos de la Capa 2. Revienta al arrancar si hay endpoints con
// [HasPermission] y la config no pide "Projection": el claim `perm` ya no se emite (Fase
// 7.5.10), así que en modo Jwt esos endpoints darían 403 siempre, en silencio.
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// Rate limiter para /webhooks/*: 1000 req/min por IP (§28.4/§K.1 del diseño) — deja pasar
// reintentos legítimos del provider sin abrir la puerta a un flood.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "webhooks",
        context =>
        {
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: client,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1000,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            );
        }
    );
});

// RateLimit Fase 2 — piloto Customer (Fase 6) extendido a PaymentApp. Flag OFF por default
// (fail-open a la cuota base sin escalar, vía NullTenantPlanCodeReader/NullPlanRateLimitReader de
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

// Rate limiting tiered por tenant/usuario (Fase 4.13 del plan) — IConnectionMultiplexer/IRateCounter
// ya estaban registrados desde F26.4 (PaymentAttemptThrottle), solo hace falta conectar el
// evaluador; mismo [RateLimit]/[RateLimitExempt] que el resto del monorepo desde Fase 3/4.2. No
// reemplaza el limiter nativo "webhooks" de arriba (categoría D/E, StripeWebhookController queda
// [RateLimitExempt]).
builder.Services.AddTieredRateLimiting();

// Auditoría independiente post-Fase-9 (invariante §4, categoría M) — PaymentApp es el otro de los
// 2 servicios con al menos una política M (payment_app.m.refund). Debe registrarse DESPUÉS de
// AddTieredRateLimiting() para ganar sobre el NoOp default (last-registration-wins).
builder.Services.AddScoped<IRateLimitAuditSink, PaymentAuditLogRateLimitAuditSink>();

// Resuelve pagos atascados en Processing tras una caída a mitad de cobro (§B.6).
builder.Services.AddHostedService<PendingChargeReconciliationJob>();

// Reintenta cobros Failed con backoff hasta agotar el retry (§C.1).
builder.Services.AddHostedService<DunningJob>();

// Avisa 30 días antes de que un método de pago guardado venza (§D.5).
builder.Services.AddHostedService<ExpiringPaymentMethodsJob>();

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

var redisEndpoint = (builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379").Split(':');

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<PaymentAppDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"])
    .AddCheck("redis", new TcpEndpointHealthCheck(redisEndpoint[0], int.Parse(redisEndpoint[1])), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(ChargeSaaSPaymentCommand).Assembly);
    // Registro explícito de los consumers de la proyección RBAC: la discovery convencional no los
    // engancha en este servicio (Wolverine loguea "No known handler para UserRolesChangedIntegrationEvent"),
    // así que la proyección UserPermissionsProjections quedaba vacía y [HasPermission] daba 403. Idénticos
    // a los de Subscription, que sí discovery-ean solos — este include fuerza su registro.
    options.Discovery.IncludeType(typeof(UserRolesChangedPermissionsProjectionConsumer));
    options.Discovery.IncludeType(typeof(RolePermissionsChangedPermissionsProjectionConsumer));
    // RateLimit Fase 2 — mismo motivo que los 2 consumers de arriba: fuerza el registro explícito
    // del consumer de TenantEntitlementsChangedIntegrationEvent por si la discovery convencional
    // tampoco lo engancha en este servicio.
    options.Discovery.IncludeType(
        typeof(TaxVision.PaymentApp.Application.RateLimiting.Consumers.TenantPlanCodeProjectionConsumer)
    );
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, PaymentAppDbContext>();
    options.Policies.AutoApplyTransactions();

    // Eventos propios: resultado de un cobro de renovación (suscripción base, seat, add-on).
    options.PublishMessage<SubscriptionRenewalPaymentSucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionRenewalPaymentFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatRenewalPaymentSucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SeatRenewalPaymentFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<AddOnRenewalPaymentSucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<AddOnRenewalPaymentFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options
        .PublishMessage<SubscriptionPlanChangePaymentSucceededIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");
    options.PublishMessage<SubscriptionPlanChangePaymentFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SaaSPaymentMethodExpiringSoonIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // PayFlow (Fase 8) — resultado del pago inicial de un onboarding pago-primero.
    options.PublishMessage<OnboardingPaymentSucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<OnboardingPaymentFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Bug real encontrado en auditoría: SaaSPaymentChargeOutcome ya publicaba este evento para
    // liquidar beneficios de referidos en Growth, pero nunca tuvo ruta registrada -- Wolverine
    // lo descartaba silenciosamente y los descuentos de referidos nunca se liquidaban.
    options.PublishMessage<PaymentSucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // Consume TenantCreated/TenantStatusChanged (proyección local) y
    // SubscriptionRenewalDue/SeatRenewalDue/AddOnRenewalDue/SubscriptionPlanChangeDue
    // (intents de cobro) del exchange fan-out.
    options
        .ListenToRabbitQueue(
            "payment-app-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    // RBAC Fase 5 — restaura BuildingBlocks.Web.Tenancy.TenantContext dentro del scope que Wolverine
    // crea para cada handler (bus.InvokeAsync local o consumer de integration event).
    options
        .Policies.ForMessagesOfType<BuildingBlocks.Messaging.IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

    options.ApplyStandardFailurePolicies();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "PaymentApp API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();

// RBAC Fase 5 — reemplaza la copia local (no sellaba IMessageBus.TenantId, así que un handler
// invocado vía bus.InvokeAsync nunca heredaba el tenant de la petición HTTP). RBAC Fase 7 hotfix
// (2026-07-22): va ANTES de UseAuthorization() — en modo Projection, [HasPermission] necesita el
// tenant ya poblado durante su propia evaluación, que corre dentro de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<TenantStatusGateMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();

public partial class Program;
