using System.Net;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.Authorization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Health;
using BuildingBlocks.Web.Hosting;
using BuildingBlocks.Web.Middleware;
using BuildingBlocks.Web.Observability;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.ResourceAuthorization;
using BuildingBlocks.Web.Security;
using BuildingBlocks.Web.Session;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Serilog;
using StackExchange.Redis;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Application.Settings.IntegrationEvents;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Infrastructure;
using TaxVision.Signature.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging estructurado (Serilog → OTLP/Loki) ----------
builder.Host.UseTaxVisionSerilog("signature-service");

builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks (correlación + tenant context) ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddSignatureInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);

// Autorización por permiso ([HasPermission("signature.*")]); los admins pasan siempre.
// BuildingBlocks.ActorTypeAuthorization — Fase 3 del plan de autorización por actor type,
// reemplaza a la copia local que tenía este servicio.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// Gate de módulo Fase 1 (LOG-ONLY, piloto) — opt-in de Signature: el hook de PermissionPolicyProvider
// resuelve esta fuente por request y loguea allow/deny por módulo SIN bloquear (nunca 403 en esta
// fase). Solo Signature la registra por ahora; los demás servicios se suman en el fan-out. El lector
// concreto (ITenantEntitlementModulesReader) lo registra la Infrastructure.
builder.Services.AddScoped<ITenantModuleEntitlementsSource, TenantModuleEntitlementsSource>();

// H-05 — fuente de permisos de la Capa 2. Revienta al arrancar si hay endpoints con
// [HasPermission] y la config no pide "Projection": el claim `perm` ya no se emite (Fase
// 7.5.10), así que en modo Jwt esos endpoints darían 403 siempre, en silencio.
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// RBAC Fase 4 (RBAC_Hardening_Plan.md) — resource ownership sobre SignatureRequest, apagado por
// default (Authorization:ResourceOwnership:Enabled). Override: signature.request.manage.
builder.Services.AddResourceOwnershipOptions(builder.Configuration);
builder.Services.AddOwnershipAuthorization<SignatureRequest>(SignaturePermissions.RequestManage);

// Rate limiter para endpoints públicos: 15 req/min por IP+ruta para desanimar
// enumeración/fuerza bruta de tokens.
builder.Services.AddRateLimiter(options =>
{
    // Mismo 429 que el evaluador tiered: Retry-After + body con retryAfterSeconds. Los headers
    // X-RateLimit-* quedan solo en el tiered (atados a tenant/capa, que este limiter por IP no tiene).
    options.UseTaxVisionRejectionResponse();

    options.AddPolicy(
        "public-signature",
        context =>
        {
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            // Auditoría independiente post-Fase-9: la ruta CRUDA (context.Request.Path) incluye el
            // token/id del firmante — cada valor distinto abre un bucket nuevo, así que un
            // atacante enumerando tokens nunca reutiliza el mismo bucket y el límite de 15/min
            // jamás se dispara. El patrón de ruta (ej. "/public/signature/{token}") sí es estable
            // por endpoint — mismo criterio recomendado por Microsoft para rate limiting
            // por-endpoint. Fallback a la ruta cruda solo si el endpoint no resolvió (no debería
            // pasar acá — UseRateLimiter corre después del routing implícito).
            var routeKey =
                (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                ?? context.Request.Path.Value?.ToLowerInvariant()
                ?? string.Empty;
            return TaxVisionRateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"{client}:{routeKey}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 15,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            );
        }
    );

    // Subida de imagen de firma (multipart, endpoint anónimo por token): más cara que un POST JSON
    // porque recibe bytes y los sube a MinIO, así que lleva su propio limiter, más estricto que el
    // genérico "public-signature". Misma partición IP+patrón-de-ruta (el token va en el path pero el
    // patrón "/{token}/signature-image" es estable, no enumerable). Un firmante legítimo sube su firma
    // una o dos veces; 8/min deja margen para reintentos sin permitir flooding de objetos.
    options.AddPolicy(
        "public-signature-upload",
        context =>
        {
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var routeKey =
                (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                ?? context.Request.Path.Value?.ToLowerInvariant()
                ?? string.Empty;
            return TaxVisionRateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"{client}:{routeKey}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 8,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            );
        }
    );
});

// Rate limiting por tenant/usuario (Fase 4.7 del plan) — arrancaba en cero salvo el limiter
// nativo "public-signature" de arriba (que se deja intacto, protege el unico endpoint
// genuinamente anonimo del servicio). Mismo [RateLimit]/IRateCounter tiered que ya corre en
// el resto del monorepo desde Fase 3/4.2.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// RateLimit Fase 2 — piloto Customer (Fase 6) extendido a Signature. Flag OFF por default
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

builder.Services.AddHttpContextAccessor();
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "signature-service");

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<SignatureDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    // Descubre consumers y handlers en el assembly Application.
    options.Discovery.IncludeAssembly(typeof(TenantCreatedConsumer).Assembly);
    // P2 — consumer COMPARTIDO del kit (vive en BuildingBlocks.CustomerVisibility, fuera de .Application).
    options.Discovery.IncludeType(typeof(BuildingBlocks.CustomerVisibility.CustomerAssignmentsProjectionConsumer));
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, SignatureDbContext>();
    options.Policies.AutoApplyTransactions();

    // Consume TenantCreated (inicializa settings), Customer* (proyección de clientes)
    // y File* de CloudStorage (proyección de archivos + promoción Draft → Ready).
    options
        .ListenToRabbitQueue(
            "signature-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    // Publica los eventos propios del microservicio al exchange fan-out del ecosistema.
    options.PublishMessage<SignatureRequestCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureRequestReadyForSendingIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureRequestSentIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureRequestCanceledIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureRequestExpirationExtendedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignerInvitedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignerConsentAcceptedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<DocumentSignedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignerRejectedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureRequestCompletedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureRequestSealedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureReadyForDownloadIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureCertificateReadyForDownloadIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureRequestSealingFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignerPinVerifiedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignerPinFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignerVerificationChallengeIssuedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignerVerificationSucceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignerVerificationFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<PreparerSignedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureRequestExpiredIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureRequestReminderDueIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Fase D1 — reemplaza el HttpClient a CloudStorage para subir el sellado/certificate.
    options.PublishMessage<SaveFileRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ReassignFileOwnerRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignatureSettingsUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignaturePlanConstraintsUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // El sellado puede adelantarse al scan de ClamAV de las imágenes de firma (el firmante las sube
    // segundos antes de completar). SignatureImageNotReadyException es transitoria: se reintenta con
    // cooldowns más largos que los estándar (hasta ~2 min) para dar tiempo a que llegue FileAvailable
    // y sellar con la firma real, en vez de degradar al sello tipográfico. Debe registrarse ANTES de
    // ApplyStandardFailurePolicies: gana la primera regla que matchea y la estándar captura Exception.
    options
        .Policies.OnException<SignatureImageNotReadyException>()
        .RetryWithCooldown(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60)
        );

    options.ApplyStandardFailurePolicies();
});

// IP real del firmante detrás de Cloudflare/Caddy/Gateway — resolución uniforme compartida.
builder.Services.AddTaxVisionClientIpForwarding(builder.Configuration);

var app = builder.Build();

// Reescribe RemoteIpAddress con la IP real del cliente (CF-Connecting-IP) ANTES de todo lo que la lea
// (logging, rate limiter, host-guard, controllers, certificado de firma).
app.UseTaxVisionClientIp();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Signature API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseAuthentication();

// Middleware compartido de tenant: sella IMessageBus.TenantId para que un handler invocado vía
// bus.InvokeAsync también herede el tenant de la petición HTTP. Va ANTES de UseAuthorization() —
// en modo Projection, [HasPermission] necesita el tenant ya poblado durante su propia evaluación,
// que corre dentro de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

// Anti-phishing: rechaza la firma pública si el subdominio no es el dueño del token (Host↔token).
app.UseMiddleware<TaxVision.Signature.Api.Middleware.PublicSignatureHostGuardMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();

public partial class Program;
