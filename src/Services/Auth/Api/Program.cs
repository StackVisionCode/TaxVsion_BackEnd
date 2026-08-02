using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Infrastructure.RateLimit;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.RateLimiting;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using TaxVision.Auth.Api.Bootstrap;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Api.Jobs;
using TaxVision.Auth.Api.Middleware;
using TaxVision.Auth.Api.RateLimiting;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Infrastructure;
using TaxVision.Auth.Infrastructure.Onboarding.HttpClients;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Infrastructure.Security;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseTaxVisionSerilog("auth-service");
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddBuildingBlocks();
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddAuthInfrastructure(builder.Configuration);

// RateLimit Fase 2 — quotas tier-aware por PlanCode. Flag OFF por default (fail-open a la cuota
// base sin escalar, vía NullTenantPlanCodeReader/NullPlanRateLimitReader de
// AddTieredRateLimiting) hasta rollout coordinado, mismo criterio que Customer/Tenant. Los
// lectores concretos (EfTenantPlanCodeReader/HttpPlanRateLimitReader) y el consumer que mantiene
// la proyección local al día se registran siempre en AddAuthInfrastructure
// (AddRateLimitTierQuotas); acá solo se decide si RateLimitQuotaResolver los usa.
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

// Rate limiting tiered por tenant/usuario (Fase 4.12 del plan) — Auth ya tenía
// IConnectionMultiplexer/IRateCounter registrados desde Fase 0.1 (LoginThrottler), así que
// solo hace falta conectar el evaluador; mismo [RateLimit]/[RateLimitExempt] que el resto
// del monorepo desde Fase 3/4.2.
builder.Services.AddTieredRateLimiting();

// Auditoría independiente post-Fase-9 (invariante §4, categoría M) — Auth es uno de los 2
// servicios con al menos una política M (auth.m.onboarding_admin_cancel_refund). Debe registrarse
// DESPUÉS de AddTieredRateLimiting() para ganar sobre el NoOp default (last-registration-wins).
builder.Services.AddScoped<IRateLimitAuditSink, AuthAuditLogRateLimitAuditSink>();

// RBAC Fase 6 — flag para SessionDenylistMiddleware (BuildingBlocks.Web.Session); el reader en sí
// (IAccessTokenDenylist/ISessionDenylistReader) ya se registra en AddAuthInfrastructure.
builder.Services.Configure<BuildingBlocks.Web.Session.SessionDenylistOptions>(
    builder.Configuration.GetSection(BuildingBlocks.Web.Session.SessionDenylistOptions.SectionName)
);
builder.Services.Configure<PlatformBootstrapOptions>(
    builder.Configuration.GetSection(PlatformBootstrapOptions.SectionName)
);
builder.Services.AddHostedService<PlatformAdminBootstrapService>();
builder.Services.Configure<PlatformEmergencyAccessOptions>(
    builder.Configuration.GetSection(PlatformEmergencyAccessOptions.SectionName)
);
builder.Services.AddHostedService<PlatformAdminEmergencyAccessService>();
builder.Services.AddHostedService<SystemRolePermissionsSyncService>();
builder.Services.AddHostedService<TenantDomainBackfillService>();
builder.Services.AddHostedService<PermissionsBackfillService>();
builder.Services.AddHostedService<TenantDomainProvisioningPoller>();
builder.Services.AddHostedService<AuthMaintenanceService>();
builder.Services.AddHostedService<OnboardingRetryScheduler>();

// Contexto de request (IP/user-agent) para auditoría y sesiones.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestContext, RequestContext>();

// Tenant candidato resuelto desde el Host (Fase A3) — ver TenantHostResolutionMiddleware.
builder.Services.AddScoped<IResolvedTenantContext, ResolvedTenantContext>();

builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);

// Autorización por permisos: [HasPermission("users.invite")] ⇒ claim "perm" (BuildingBlocks.
// ActorTypeAuthorization — Fase 3 del plan de autorización por actor type, reemplaza a la copia
// local que tenía este servicio).
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// RBAC Fase 7 — perm_v enforcement vía IUserPermissionsSource. Default "Jwt" (comportamiento
// actual, sin cambios) — "Projection" se activa por servicio tras validar performance en
// staging (Authorization:PermissionsSource). Auth es el único de los 14 servicios sin
// proyección eventual: ya es la fuente de verdad de User/Role, así que
// AuthUserPermissionsProjectionReader resuelve en vivo contra sus propias tablas (cero
// staleness posible salvo la del propio JWT).
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IUserPermissionsProjectionReader, AuthUserPermissionsProjectionReader>();
if (builder.Configuration["Authorization:PermissionsSource"] == "Projection")
    builder.Services.AddScoped<IUserPermissionsSource, ProjectionPermissionsSource>();
else
    builder.Services.AddScoped<IUserPermissionsSource, JwtEmbeddedPermissionsSource>();

// PayFlow (Fase 9) — primer policy M2M-only de Auth, mismo patrón "ServiceOnly" que PaymentApp
// (Fase 8): gatea InternalOnboardingTokensController, invocado por otro servicio (no por Auth).
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

builder.Services.AddTaxVisionOpenTelemetry(
    builder.Configuration,
    "auth-service",
    TaxVision.Auth.Infrastructure.Onboarding.Observability.OnboardingMetrics.MeterName
);

// Rate limiting para los endpoints públicos de resolución de tenant (Fase A4) —
// partición por IP real (ya normalizada por ForwardedHeadersMiddleware, que corre
// antes en el pipeline). "tenant-recovery" es más estricto: dispara un envío de
// email real y es el vector más atractivo para enumeración de tenants por email.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Auditoría post-Fase-9 (hallazgo #13a) — el limiter nativo de ASP.NET Core no emite
    // ningún header en el 429 a menos que se lo pida explícito, a diferencia del evaluador
    // tiered (RateLimitAttribute.WriteRateLimitResponseAsync, §6.3 del plan). Solo se agrega
    // Retry-After acá (universalmente respetado por HTTP clients/proxies) — el resto de headers
    // X-RateLimit-* del path tiered están atados a policy/tenant/capa, conceptos que estos
    // limiters pre-auth por-IP no tienen; forzar esos headers acá sería inventar semántica.
    options.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        await ValueTask.CompletedTask;
    };

    options.AddPolicy(
        "tenant-lookup",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    options.AddPolicy(
        "tenant-recovery",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    // PayFlow (Fase 11) — endpoint anónimo mediador de descarga del recibo (GetOnboardingReceiptDownloadRedirectQuery).
    // El FileId ya funciona como capability opaca; esto sólo acota fuerza bruta contra ese espacio de GUIDs.
    options.AddPolicy(
        "onboarding-receipt-download",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    // Auditoría (gap MinIO/legal-docs) — mismo patrón que onboarding-receipt-download: el Id de
    // TermsVersion ya funciona como capability opaca, esto solo acota fuerza bruta contra ese
    // espacio de GUIDs para el frontend público de onboarding que renderiza ToS/Privacy inline.
    options.AddPolicy(
        "terms-content-download",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    // PayFlow (Fase 13) — endpoints públicos del form de registro (preview/complete/status), límites
    // exactos del plan: preview 30/min, complete 10/min (más estricto, deriva la Saga de provisioning),
    // status 60/min (polling legítimo del frontend mientras la Saga corre).
    options.AddPolicy(
        "onboarding-registration-preview",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    options.AddPolicy(
        "onboarding-registration-complete",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    options.AddPolicy(
        "onboarding-status",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 60,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    // PayFlow (Fase 9) — endpoints anónimos que crean estado servidor pesado: POST /onboarding
    // inserta un TenantOnboarding + emisión de OTP challenge, POST /onboarding/checkout dispara
    // una llamada M2M a PaymentApp y una Stripe Checkout Session real (costo en el dashboard de
    // Stripe). 5/min por IP corta la creación masiva sin bloquear a usuarios reales que reintentan
    // por error. Los demás endpoints públicos ya tenían política, este era el gap real reportado
    // por el audit F02.
    options.AddPolicy(
        "onboarding-checkout-create",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    // PayFlow (Fase 14) — check-y-reserva de subdominio; mismo límite que onboarding-registration-preview
    // (30/min), el frontend puede llamarlo varias veces mientras el usuario tipea.
    options.AddPolicy(
        "onboarding-subdomain-check",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    // Auditoría F22 — OnboardingChallengesController (create/verify/resend del OTP de email) era el
    // único controller anónimo del módulo sin rate-limit HTTP por IP, contradiciendo la propia
    // premisa de F02. Ya tiene throttle de negocio vía ILoginThrottler (contador Redis por
    // email/challenge), pero eso no evita floods baratos a nivel HTTP antes de tocar Redis/SQL.
    // Mismo límite que onboarding-registration-preview: acota sin bloquear reintentos legítimos.
    options.AddPolicy(
        "onboarding-email-challenge",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    static string PartitionKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
});

var authRabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);
var authRedis = HostPort.Parse(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", 6379);

// PayFlow (auditoría F19) — checks HTTP reales contra los 4 downstream M2M de Onboarding, aparte de
// "ready" para no acoplar el liveness/readiness de Auth (k8s probes) a la disponibilidad de otros
// servicios: si Documents está caído, Auth sigue "ready" (puede servir el resto de su API), pero
// /health/downstream lo refleja para alertar sin tumbar el pod. Mismo HttpEndpointHealthCheck que ya
// usa el Gateway contra sus clusters — apunta a /health/ready de cada servicio (valida sus propias
// dependencias, no solo que el proceso responda).
builder.Services.AddHttpClient("taxvision-health", client => client.Timeout = TimeSpan.FromSeconds(5));
var documentsHealthUrl = new Uri(
    new Uri(builder.Configuration[$"{DocumentsClientOptions.SectionName}:BaseUrl"] ?? "http://localhost:5450"),
    "health/ready"
).ToString();
var paymentAppHealthUrl = new Uri(
    new Uri(builder.Configuration[$"{PaymentAppClientOptions.SectionName}:BaseUrl"] ?? "http://localhost:5430"),
    "health/ready"
).ToString();
var tenantHealthUrl = new Uri(
    new Uri(builder.Configuration[$"{TenantClientOptions.SectionName}:BaseUrl"] ?? "http://localhost:5217"),
    "health/ready"
).ToString();
var subscriptionHealthUrl = new Uri(
    new Uri(builder.Configuration[$"{SubscriptionClientOptions.SectionName}:BaseUrl"] ?? "http://localhost:5360"),
    "health/ready"
).ToString();

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<AuthDbContext>("sql-server", tags: ["ready"])
    .AddCheck("redis", new TcpEndpointHealthCheck(authRedis.Host, authRedis.Port), tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(authRabbitUri.Host, authRabbitUri.Port), tags: ["ready"])
    .AddTypeActivatedCheck<HttpEndpointHealthCheck>(
        "documents-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["payflow-downstream"],
        args: [documentsHealthUrl]
    )
    .AddTypeActivatedCheck<HttpEndpointHealthCheck>(
        "paymentapp-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["payflow-downstream"],
        args: [paymentAppHealthUrl]
    )
    .AddTypeActivatedCheck<HttpEndpointHealthCheck>(
        "tenant-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["payflow-downstream"],
        args: [tenantHealthUrl]
    )
    .AddTypeActivatedCheck<HttpEndpointHealthCheck>(
        "subscription-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["payflow-downstream"],
        args: [subscriptionHealthUrl]
    );

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(LoginHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var rabbitUri =
        builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.");

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(new Uri(rabbitUri)).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, AuthDbContext>();
    options.Policies.AutoApplyTransactions();

    // RBAC Fase 5 — restaura BuildingBlocks.Tenancy.TenantContext dentro del scope que Wolverine
    // crea para cada handler (bus.InvokeAsync local o consumer de integration event), ver
    // JwtTenantContextMiddleware/LocalCommandTenantMiddleware para el porqué.
    options.Policies.ForMessagesOfType<IIntegrationEvent>().AddMiddleware(typeof(IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(LocalCommandTenantMiddleware));

    // Eventos publicados por Auth
    options.PublishMessage<UserRegisteredIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<InvitationCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<UserDeactivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<UserReactivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<UserRolesChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<RolePermissionsChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<PasswordResetRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<MfaChallengeRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SecurityAlertIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailChangeRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<UserProfileUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantRecoveryRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantDomainCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantDomainVerifiedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantDomainActivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantDomainDisabledIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantDomainProvisioningFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantTermsAcceptedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Fase A7 — faltaba: se publicaba via TenantSubdomainChangedHandler pero nunca se
    // registro aca, asi que Wolverine nunca lo mandaba a RabbitMQ (se perdia en silencio).
    options.PublishMessage<TenantSubdomainChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantDomainReservedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantResolutionFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<OnboardingOtpRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<OnboardingRegistrationReadyIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<OnboardingReceiptReadyIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<OnboardingProvisioningStartedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<OnboardingProvisioningStepFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantOnboardingCompletedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // PayFlow (Fase 16) — publicado por InternalTenantOwnersController, no por la Saga.
    options.PublishMessage<TenantOwnerCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // PayFlow (Fase 17) — publicados por CancelAndRefundOnboardingAdminHandler; sin estos registros
    // Wolverine nunca los mandaba a RabbitMQ y toda la compensación (refund Stripe, close tenant,
    // deactivate user, cancel subscription) quedaba silenciosamente muerta en producción — mismo
    // antipatrón que documenta el comentario de TenantSubdomainChangedIntegrationEvent arriba.
    options.PublishMessage<OnboardingRefundRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<OnboardingCancelRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Auditoría (gap MinIO/legal-docs) — pedido de guardado a CloudStorage para el documento legal
    // (ToS/Privacy) que PlatformAdmin sube (TermsContentStorageClient). Sin este registro, igual que
    // TenantSubdomainChangedIntegrationEvent/OnboardingRefundRequestedIntegrationEvent arriba, Wolverine
    // no tiene ninguna ruta para el mensaje y lo descarta en silencio — mismo patrón exacto que
    // Documents.Api Program.cs usa para el mismo evento (cola dedicada point-to-point).
    options.PublishMessage<SaveFileRequestedIntegrationEvent>().ToRabbitQueue("cloudstorage-external-uploads");

    // Eventos consumidos (Tenant, Customer, Subscription) — misma cola durable.
    options
        .ListenToRabbitQueue(
            "auth-tenant-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
});

// Red de confianza para X-Forwarded-Proto/Host y la IP real del cliente (Fase A3).
// Vacío por defecto — el deploy debe fijar la red Docker interna / rango de
// Cloudflare real antes de exponer el servicio detrás de un proxy en producción.
var reverseProxyTrust =
    builder.Configuration.GetSection(ReverseProxyTrustOptions.SectionName).Get<ReverseProxyTrustOptions>()
    ?? new ReverseProxyTrustOptions();
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    ForwardedForHeaderName = reverseProxyTrust.RealIpHeaderName,
};
foreach (var proxy in reverseProxyTrust.KnownProxies)
{
    if (IPAddress.TryParse(proxy, out var proxyIp))
        forwardedHeadersOptions.KnownProxies.Add(proxyIp);
}
foreach (var network in reverseProxyTrust.KnownNetworks)
{
    if (System.Net.IPNetwork.TryParse(network, out var parsedNetwork))
        forwardedHeadersOptions.KnownIPNetworks.Add(parsedNetwork);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Auth API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Resuelve el tenant candidato desde el Host (Fase A3). Antes de auth: cubre también
// flujos anónimos (login). Nunca decide autorización — eso lo sigue haciendo el JWT.
app.UseMiddleware<TenantHostResolutionMiddleware>();

app.UseAuthentication();

// RBAC Fase 5 — setea BuildingBlocks.Tenancy.TenantContext desde el JWT, para que el
// HasQueryFilter global de AuthDbContext tenga tenant listo. Distinto de
// TenantHostResolutionMiddleware (arriba, pre-auth, resuelve por Host). Va ANTES de
// UseAuthorization(): RBAC Fase 7 (Authorization:PermissionsSource=Projection) resuelve el
// permiso con una consulta tenant-scoped DURANTE la evaluación de [HasPermission], que corre
// dentro del propio middleware de UseAuthorization() — si el tenant context se poblara después,
// esa consulta vería EffectiveTenantId=Guid.Empty y fallaría cerrado (403) para todo el mundo en
// modo Projection. En modo Jwt (default) esto nunca importó porque JwtEmbeddedPermissionsSource
// no toca la base de datos.
app.UseMiddleware<BuildingBlocks.Tenancy.JwtTenantContextMiddleware>();

app.UseAuthorization();
app.UseRateLimiter();

// Revocación inmediata de access tokens de sesiones denylistadas (Redis). RBAC Fase 6 — middleware
// compartido (BuildingBlocks.Web.Session), reemplaza la copia local que tenía Auth.
app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();

// Fase L1.4 — bloquea con 409 a un tenant que no acepto la version vigente del ToS/AUP.
app.UseMiddleware<TermsAcceptanceMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// PayFlow (Fase 17) — desglose por dependencia (sql-server/redis/rabbitmq, las mismas registradas
// arriba para "ready") en vez de solo Healthy/Unhealthy.
app.MapHealthChecks(
    "/health/detailed",
    new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var payload = new
            {
                status = report.Status.ToString(),
                totalDurationMs = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    durationMs = entry.Value.Duration.TotalMilliseconds,
                    description = entry.Value.Description,
                    error = entry.Value.Exception?.Message,
                }),
            };
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(payload));
        },
    }
);

// PayFlow (auditoría F19) — Documents/PaymentApp/Tenant/Subscription en un path SEPARADO de
// /health/ready a propósito: si alguno de los 4 está caído, Auth sigue "ready" para todo lo que no
// depende de ellos (k8s no debería tumbar el pod por esto), pero un operador/dashboard que consulte
// /health/downstream ve el desglose real de qué M2M de Onboarding está fallando.
app.MapHealthChecks(
    "/health/downstream",
    new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("payflow-downstream"),
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var payload = new
            {
                status = report.Status.ToString(),
                totalDurationMs = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    durationMs = entry.Value.Duration.TotalMilliseconds,
                    description = entry.Value.Description,
                    error = entry.Value.Exception?.Message,
                }),
            };
            await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(payload));
        },
    }
);

app.MapControllers();

app.Run();

public partial class Program;
