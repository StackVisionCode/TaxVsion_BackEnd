using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Redis;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Health;
using BuildingBlocks.Web.Hosting;
using BuildingBlocks.Web.Middleware;
using BuildingBlocks.Web.Observability;
using BuildingBlocks.Web.Platform;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Security;
using BuildingBlocks.Web.Session;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;
using StackExchange.Redis;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Infrastructure;
using TaxVision.Notification.Infrastructure.Onboarding;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Push;
using TaxVision.Notification.Infrastructure.Scribe;
using TaxVision.Notification.Infrastructure.Storage;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging estructurado (Serilog → OTLP/Loki) ----------
builder.Host.UseTaxVisionSerilog("notification-service");

builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks (correlación + tenant context) ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddTimeZoneSelfCheck();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);

// Marca y dominio de la plataforma (Tenant). Va junto a ListenForPlatformSettings() en Wolverine.
builder.Services.AddPlatformSettings(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);

// Autorización por permiso ([HasPermission("notification.*")]); los admins pasan siempre.
// BuildingBlocks.ActorTypeAuthorization — Fase 3 del plan de autorización por actor type,
// reemplaza a la copia local que tenía este servicio.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// M2M-only (AJ-08): estado de entrega por evento que consulta Auth (InternalNotificationLogsController).
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

// H-05 — fuente de permisos de la Capa 2. Revienta al arrancar si hay endpoints con
// [HasPermission] y la config no pide "Projection": el claim `perm` ya no se emite (Fase
// 7.5.10), así que en modo Jwt esos endpoints darían 403 siempre, en silencio.
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// Rate limiting por tenant/usuario (Fase 4.3 del plan) — arrancaba en cero (sin AddRateLimiter ni
// ninguna política previa), mismo [RateLimit]/IRateCounter tiered que ya corre en
// Customer/Tenant desde Fase 3/4.2.
builder.Services.AddTaxVisionRedis(
    builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// RateLimit Fase 2 — piloto Customer (Fase 6) extendido a Notification. Flag OFF por default
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
builder.Services.AddTieredRateLimiting();

// Cliente HTTP a CloudStorage (plantillas/layouts). El token del usuario se reenvía en contexto request;
// en background (sync) se usa un token de servicio M2M del Auth.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICloudStorageTokenProvider, CloudStorageTokenProvider>();
builder.Services.AddHttpClient<ICloudStorageClient, CloudStorageClient>(
    (sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    }
);

// Grant M2M: obtiene tokens de servicio del Auth para autenticar al worker contra CloudStorage.
builder.Services.Configure<ServiceAuthClientOptions>(
    builder.Configuration.GetSection(ServiceAuthClientOptions.SectionName)
);
builder.Services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
    (sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
        client.BaseAddress = new Uri(options.AuthBaseUrl);
    }
);

// RateLimit Fase 2 — HttpPlanRateLimitReader (BuildingBlocks.Infrastructure.RateLimiting) depende
// del contrato compartido; ServiceTokenAcquirer ya lo implementa, solo falta el forwarding.
builder.Services.AddTransient<BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer>(sp =>
    (BuildingBlocks.Infrastructure.Security.IServiceTokenAcquirer)sp.GetRequiredService<IServiceTokenAcquirer>()
);

// Fase 8: cliente HTTP a Scribe (render de emails) — reusa el mismo IServiceTokenAcquirer M2M ya
// registrado arriba para CloudStorage (no está atado a un downstream específico).
builder.Services.AddScribeRenderClient(builder.Configuration);

// Reminder Fase 10: recuperación pull del correo de un usuario contra el endpoint interno de Auth,
// para los usuarios que ya existían cuando se creó el directorio userId → email. Reusa el mismo
// IServiceTokenAcquirer M2M (ya apunta a Auth) — un acquirer por servicio, un HttpClient por destino.
builder.Services.AddHttpClient<IUserContactSnapshotClient, UserContactSnapshotClient>(
    (sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
        client.BaseAddress = new Uri(options.AuthBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(15);
    }
);

// F6: adjunto .ics de las reuniones (invitación, reprogramación, cancelación). Sin estado: singleton.
builder.Services.AddSingleton<
    ICalendarInviteBuilder,
    TaxVision.Notification.Application.Common.CalendarInviteBuilder
>();

// Portal /portal: host primario del tenant (subdominio de plataforma) para armar los links
// per-tenant de los correos (staff en {host}, cliente en {host}/portal). Mismo patrón M2M y el
// mismo IServiceTokenAcquirer que el pull de user-contact de arriba.
builder.Services.AddSingleton<TaxVision.Notification.Application.Abstractions.TenantHostCache>();
builder.Services.AddHttpClient<ITenantHostResolver, TenantHostResolver>(
    (sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
        client.BaseAddress = new Uri(options.AuthBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(15);
    }
);

// PayFlow (Fase 12): cliente HTTP al endpoint one-shot de Auth que resuelve un TokenReference a la
// URL real de registro — reusa el mismo IServiceTokenAcquirer M2M (apunta a Auth, mismo host que
// ServiceAuthClientOptions.AuthBaseUrl ya configurado arriba).
builder.Services.AddHttpClient<IOnboardingTokenClient, OnboardingTokenClient>(
    (sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
        client.BaseAddress = new Uri(options.AuthBaseUrl);
    }
);

// EmailWebhooksController/EmailWebhookOptions (webhooks de tracking delivered/opened/clicked/bounced de
// proveedores SMTP tipo SendGrid/Mailgun) retirados: nunca tuvo un secreto real configurado en ningún
// appsettings/.env del repo (el endpoint devolvía 401 siempre — cero llamadas reales posibles) y era
// scaffolding especulativo, nunca se construyó ningún adaptador de proveedor. Postmaster es ahora la
// única fuente de verdad de tracking de entrega/bounce/suppression para los correos que routea
// (MarkBounced se alimenta de PostmasterEmailDeliveryBouncedIntegrationEvent en vez de este webhook muerto).

builder.Services.AddTaxVisionOpenTelemetry(
    builder.Configuration,
    "notification-service",
    TaxVision.Notification.Infrastructure.Observability.SubscriptionEmailMetrics.MeterName,
    TaxVision.Notification.Infrastructure.Observability.NotificationDispatchMetrics.MeterName,
    TaxVision.Notification.Infrastructure.Observability.ProviderWebhookMetrics.MeterName
);

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<NotificationDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"])
    .AddCheck<FcmPushHealthCheck>("fcm-push", failureStatus: HealthStatus.Degraded, tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.UseTaxVisionCodeGeneration(builder.Environment, builder.Configuration, typeof(Program).Assembly);
    options.Discovery.IncludeAssembly(typeof(InvitationCreatedConsumer).Assembly);
    options.Discovery.IncludeType(typeof(BuildingBlocks.CustomerVisibility.CustomerAssignmentsProjectionConsumer));
    // Zona de cada oficina (TenantTimeZones): consumer compartido, vive fuera de Application.
    options.Discovery.IncludeType(typeof(BuildingBlocks.TenantTimeZones.TenantTimeZoneProjectionConsumer));
    options.ListenForPlatformSettings();
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseTaxVisionRabbitMq(rabbitUri);
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, NotificationDbContext>();
    options.Policies.AutoApplyTransactions();
    options.UseTaxVisionDurableLocalQueues();

    // Eventos salientes del módulo email (entrega asíncrona propia + notificación a otros servicios).
    options.PublishMessage<EmailSendRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailDeliverySucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailDeliveryFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Canal Email del orquestador Campaigns (ADR-CAMP-001 D3) — el consumer publica el result del dispatch.
    options
        .PublishMessage<BuildingBlocks.Messaging.CampaignsIntegrationEvents.CampaignDispatchResultIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");

    // El correo renderizado (con códigos y enlaces) va directo a la cola de Postmaster, nunca al fanout.
    options.PublishMessage<NotificationsEmailSendRequestedIntegrationEvent>().ToRabbitQueue("postmaster-events");

    // Avisos del webhook único de SMTP2GO que son de correos del sistema: los traduce Postmaster.
    options.PublishMessage<EmailProviderFeedbackReceivedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // El SMS (teléfono y texto final) va directo a la cola del servicio Sms, nunca al fanout.
    options.PublishMessage<SmsSendRequestedIntegrationEvent>().ToRabbitQueue("sms-events");
    options.PublishMessage<InAppNotificationRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options.ListenToRabbitQueue("notification-client-form-commands").UseDurableInbox();

    // Reintento programado del push de Client Forms: cola local durable, sobrevive a un reinicio (P-21.3.1).
    options.LocalQueueFor<TaxVision.Notification.Application.Common.RetryClientFormPushRequested>().UseDurableInbox();

    // D-N44 (opción A): chat y campañas en su carril; notification-events queda para lo crítico y lo normal.
    options.PublishMessage<DispatchChatPush>().ToLocalQueue(NotificationLanes.Bulk);
    options.PublishMessage<DispatchCampaignMessage>().ToLocalQueue(NotificationLanes.Bulk);
    options.LocalQueue(NotificationLanes.Bulk).MaximumParallelMessages(2).UseDurableInbox();

    // N8-04: la llamada a FCM, sin transacción y con un solo tope de concurrencia.
    options.PublishMessage<DeliverPush>().ToLocalQueue(NotificationLanes.Push);
    options.LocalQueue(NotificationLanes.Push).MaximumParallelMessages(8).UseDurableInbox();

    // Consume los eventos de Auth (invitaciones, resets, OTP, alertas).
    options
        .ListenToRabbitQueue(
            "notification-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        // Paralelismo acotado: cada consumer transiciona su propia fila con validación de estado y el
        // inbox durable deduplica por MessageId, así que el desorden es tolerable y el orden no es un invariante.
        .MaximumParallelMessages(5)
        .UseDurableInbox();

    // RBAC Fase 5 — restaura BuildingBlocks.Web.Tenancy.TenantContext dentro del scope que Wolverine
    // crea para cada handler (bus.InvokeAsync local o consumer de integration event).
    options
        .Policies.ForMessagesOfType<BuildingBlocks.Messaging.IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

    // NOT-22: lo publicado por los colaboradores entra en el outbox del handler.
    options.Policies.AddMiddleware(typeof(TaxVision.Notification.Infrastructure.Messaging.HandlerBusMiddleware));

    options.UseTaxVisionConflictPolicy();

    // IA-02: sin subdominio de la oficina se reintenta como siempre (1/5/15 s) y, al agotarse, va a la cola de
    // errores dejando una fila Failed por el EventId (estado de entrega del panel, 04 §4.3).
    options
        .Policies.OnException<TenantHostUnavailableException>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15))
        .Then.MoveToErrorQueue()
        .And(
            async (runtime, lifecycle, ex) =>
            {
                using var scope = runtime.Services.CreateScope();
                await UnsentAccountEmailTrail.RecordAsync(
                    lifecycle.Envelope?.Message,
                    ex,
                    scope.ServiceProvider.GetRequiredService<INotificationLogRepository>(),
                    scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
                    CancellationToken.None
                );
            },
            "Leave a Failed trail for an account email without the office subdomain"
        );
    options.ApplyStandardFailurePolicies();
});

builder.Services.AddTaxVisionClientIpForwarding(builder.Configuration);

var app = builder.Build();

// IP real del cliente detras de Cloudflare/Caddy/Gateway (compartido) — primer middleware.
app.UseTaxVisionClientIp();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Notification API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();

// Puebla TenantContext desde el JWT para el HasQueryFilter global de NotificationDbContext.
// Va antes de UseAuthorization: en modo Projection, [HasPermission] consulta una proyección
// tenant-scoped durante su propia evaluación, que corre dentro de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();

public partial class Program;
