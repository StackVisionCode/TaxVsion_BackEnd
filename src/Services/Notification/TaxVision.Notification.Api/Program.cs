using System.Text.Json.Serialization;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using BuildingBlocks.Web.Session;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Api.Jobs;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Infrastructure;
using TaxVision.Notification.Infrastructure.Persistence;
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
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);

// Autorización por permiso ([HasPermission("notification.*")]); los admins pasan siempre.
// BuildingBlocks.ActorTypeAuthorization — Fase 3 del plan de autorización por actor type,
// reemplaza a la copia local que tenía este servicio.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// RBAC Fase 7 (RBAC_Hardening_Plan.md) -- proyeccion local de permisos para enforzar perm_v.
// Flag OFF por default (Authorization:PermissionsSource ausente o "Jwt") preserva el
// comportamiento historico (permisos embebidos en el JWT, sin chequeo de staleness). Cierra un
// gap real: Notification ya usaba [HasPermission] en ~28 acciones de controller sin registrar
// nunca ningun IUserPermissionsSource en DI — cualquier endpoint asi decorado tiraba
// InvalidOperationException al resolver la policy. Mismo bloque de 5 lineas que CloudStorage/
// Customer/Connectors/Correspondence/PaymentApp/PaymentClient/Postmaster/Scribe/Tenant.
builder.Services.AddMemoryCache();
if (builder.Configuration["Authorization:PermissionsSource"] == "Projection")
    builder.Services.AddScoped<IUserPermissionsSource, ProjectionPermissionsSource>();
else
    builder.Services.AddScoped<IUserPermissionsSource, JwtEmbeddedPermissionsSource>();

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

// Fase 8: cliente HTTP a Scribe (render de emails) — reusa el mismo IServiceTokenAcquirer M2M ya
// registrado arriba para CloudStorage (no está atado a un downstream específico).
builder.Services.Configure<ScribeClientOptions>(builder.Configuration.GetSection(ScribeClientOptions.SectionName));
builder.Services.AddHttpClient<IScribeRenderClient, ScribeRenderClient>(
    (sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ScribeClientOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    }
);

// EmailWebhooksController/EmailWebhookOptions (webhooks de tracking delivered/opened/clicked/bounced de
// proveedores SMTP tipo SendGrid/Mailgun) retirados en la Fase 19 del plan de hardening (Notification,
// 2026-07-18): nunca tuvo un secreto real configurado en ningún appsettings/.env del repo (el endpoint
// devolvía 401 siempre — cero llamadas reales posibles) y su propio comentario XML ya admitía que era
// scaffolding especulativo ("un adaptador por proveedor traduciría su formato a este payload", tiempo
// condicional — nunca se construyó ningún adaptador). Postmaster es ahora la única fuente de verdad de
// tracking de entrega/bounce/suppression para los correos que routea (ver Fase 19, MarkBounced pasa a
// alimentarse de PostmasterEmailDeliveryBouncedIntegrationEvent en vez de este webhook muerto).

// Scheduler de campañas: inicia el fan-out cuando llega la hora programada.
builder.Services.AddHostedService<CampaignSchedulerService>();
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "notification-service");

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<NotificationDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(InvitationCreatedConsumer).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, NotificationDbContext>();
    options.Policies.AutoApplyTransactions();

    // Eventos salientes del módulo email (entrega asíncrona propia + notificación a otros servicios).
    options.PublishMessage<EmailSendRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailDeliverySucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailDeliveryFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailCampaignScheduledIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailCampaignStartedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailCampaignBatchIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailCampaignCompletedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // Notifications Fase 4 (Auth/Signature/Communication) + Fase 19 de hardening (EmailDeliveryService,
    // 2026-07-18) — evento hacia Postmaster. Dos productores comparten el mismo mensaje y el mismo flag
    // Notification:UsePostmasterDispatch: EventBasedEmailDispatchGateway (path IEmailDispatchGateway) y
    // PostmasterEmailDeliveryService (path IEmailDeliveryService, el que había atrás de /notifications/
    // email/send y de EmailCampaigns). El PublishMessage se declara siempre para no romper el binding
    // aun cuando el flag esté OFF; el runtime simplemente no genera envíos hasta que alguno lo invoque.
    options
        .PublishMessage<NotificationsEmailSendRequestedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");

    // Consume los eventos de Auth (invitaciones, resets, OTP, alertas).
    options
        .ListenToRabbitQueue(
            "notification-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        // Los eventos de entrega actualizan contadores compartidos de campaña.
        // Mantener el orden evita carreras y contadores perdidos.
        .Sequential()
        .UseDurableInbox();

    // RBAC Fase 5 — restaura BuildingBlocks.Tenancy.TenantContext dentro del scope que Wolverine
    // crea para cada handler (bus.InvokeAsync local o consumer de integration event).
    options
        .Policies.ForMessagesOfType<BuildingBlocks.Messaging.IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Tenancy.LocalCommandTenantMiddleware));

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
});

var app = builder.Build();

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

// RBAC Fase 5 — setea BuildingBlocks.Tenancy.TenantContext desde el JWT para el HasQueryFilter
// global de NotificationDbContext. Reemplaza al TenantResolutionMiddleware anterior (leía
// X-Tenant-Id sin nunca sellar IMessageBus.TenantId). RBAC Fase 7 hotfix (2026-07-22): va ANTES
// de UseAuthorization() — en modo Projection, [HasPermission] necesita el tenant ya poblado
// durante su propia evaluación, que corre dentro de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();
