using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Health;
using BuildingBlocks.Web.Middleware;
using BuildingBlocks.Web.Observability;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.ResourceAuthorization;
using BuildingBlocks.Web.Security;
using BuildingBlocks.Web.Session;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Serilog;
using StackExchange.Redis;
using TaxVision.CloudStorage.Application.Files.Commands;
using TaxVision.CloudStorage.Domain.Sharing;
using TaxVision.CloudStorage.Infrastructure;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Security;
using TaxVision.CloudStorage.Infrastructure.Storage;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseTaxVisionSerilog("cloudstorage-service");
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddBuildingBlocks();
builder.Services.AddCloudStorageInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "cloudstorage-service");

// Migrado de las 13 policies enumeradas a mano (RequireClaim("perm", ...), sin bypass de
// PlatformAdmin) al mecanismo compartido de BuildingBlocks.Web — ActorType F4 del plan de
// autorización. Mismo criterio que los 11 servicios "estándar": PermissionPolicyProvider ya
// incluye el bypass de PlatformAdmin (ClaimsPrincipalExtensions.HasPermission), alineando
// CloudStorage con el resto del monorepo.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// H-05 — fuente de permisos de la Capa 2. Revienta al arrancar si hay endpoints con
// [HasPermission] y la config no pide "Projection": el claim `perm` ya no se emite (Fase
// 7.5.10), así que en modo Jwt esos endpoints darían 403 siempre, en silencio.
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// RBAC Fase 4 (RBAC_Hardening_Plan.md) — resource ownership sobre ShareLink, apagado por
// default (Authorization:ResourceOwnership:Enabled). Reusa CloudStorageShareManage, permiso ya
// existente en el catalogo ("otorgar permisos elevados en links y gestionar su expiracion de
// cualquier link del tenant") como override de ownership — no hizo falta un permiso nuevo.
builder.Services.AddResourceOwnershipOptions(builder.Configuration);
builder.Services.AddOwnershipAuthorization<ShareLink>(CloudStoragePermissions.ShareManage);

// Fase C3 — 20 req/min por IP+ruta en el endpoint publico de resolucion de
// tokens: desanima enumeracion por fuerza bruta sin bloquear un uso legitimo
// (varios accesos al mismo link compartido desde la misma red).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "share-public",
        context =>
        {
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            // Auditoría independiente post-Fase-9: la ruta CRUDA (context.Request.Path) incluye el
            // token del ShareLink — cada valor distinto abre un bucket nuevo, así que un atacante
            // enumerando tokens nunca reutiliza el mismo bucket y el límite de 20/min jamás se
            // dispara. El patrón de ruta (ej. "/storage/shares/{token}") sí es estable por endpoint
            // — mismo criterio recomendado por Microsoft para rate limiting por-endpoint. Fallback
            // a la ruta cruda solo si el endpoint no resolvió (no debería pasar acá —
            // UseRateLimiter corre después del routing implícito).
            var routeKey =
                (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                ?? context.Request.Path.Value?.ToLowerInvariant()
                ?? string.Empty;
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"{client}:{routeKey}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            );
        }
    );
});

// Rate limiting por tenant/usuario (Fase 4.6 del plan) — la politica nativa "zip-download" de
// arriba migro a [RateLimit("cloudstorage.i.zip_download")] via el evaluador tiered (mismo
// costo de 5/min, ver RateLimitPolicyCatalog); "share-public" queda intacta arriba porque
// protege un endpoint [AllowAnonymous] sin JWT, algo que el evaluador tiered no puede cubrir
// (ver RateLimitExempt en PublicShareController).
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// Auditoria RateLimit hallazgo #2 — CloudStorage ganó un IServiceTokenAcquirer M2M dedicado
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
var minio = builder.Configuration.GetSection(MinioOptions.SectionName).Get<MinioOptions>() ?? new MinioOptions();
var clamAv = builder.Configuration.GetSection(ClamAvOptions.SectionName).Get<ClamAvOptions>() ?? new ClamAvOptions();
var minioEndpoint = HostPort.Parse(minio.Endpoint, minio.UseTls ? 443 : 9000);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<CloudStorageDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"])
    .AddCheck("minio", new TcpEndpointHealthCheck(minioEndpoint.Host, minioEndpoint.Port), tags: ["ready"])
    .AddCheck("clamav", new TcpEndpointHealthCheck(clamAv.Host, clamAv.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(InitiateUploadHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConnection =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConnection);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CloudStorageDbContext>();
    options.Policies.AutoApplyTransactions();

    // Cada escaneo actualiza la proyección de cuota del tenant. Se procesan en serie para
    // evitar que dos archivos compitan por el mismo RowVersion después de mover el objeto.
    options.LocalQueueFor<ScanFileCommand>().Sequential();

    options.PublishMessage<FileAvailableIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FileInfectedDetectedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FileBlockedByPolicyIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FilePendingReviewIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FileBlockedByDmcaTakedownIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FileReinstatedFromTakedownIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FileDeletedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FileRestoredIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<StorageLimitExceededIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<FileAccessAuditedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkExternalRecipientInvitedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkRevokedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkFolderItemAddedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkAccessedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkAccessDeniedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkExpiredIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkPermissionChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<LegalHoldPlacedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<LegalHoldLiftedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<DmcaCounterNoticeSubmittedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options
        .ListenToRabbitQueue(
            "cloudstorage-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    // Fase D2/D3 — cola dedicada para SaveFileRequestedIntegrationEvent publicado por
    // servicios NO-Wolverine (Node: CommunicationTranscriptWorker, luego Notification).
    // Deliberadamente separada del fanout "taxvision-events" de arriba: DefaultIncomingMessage
    // fuerza a deserializar TODO lo que llega a este listener como ese unico tipo — mezclarlo
    // con el fanout compartido rompería cada otro evento que tambien pasa por ahi. Los
    // productores Node publican directo a esta cola via el exchange default de RabbitMQ
    // (routingKey = nombre de cola), sin declarar ningun exchange propio.
    options
        .ListenToRabbitQueue("cloudstorage-external-uploads")
        .UseDurableInbox()
        .DefaultIncomingMessage<SaveFileRequestedIntegrationEvent>();

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
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "CloudStorage API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();

// Setea BuildingBlocks.Web.Tenancy.TenantContext desde el JWT para el HasQueryFilter global de
// CloudStorageDbContext. Va ANTES de UseAuthorization() — en modo
// Authorization:PermissionsSource=Projection, [HasPermission] resuelve el permiso con una
// consulta tenant-scoped DURANTE la evaluación de UseAuthorization();
// si el tenant se poblara después, esa consulta vería EffectiveTenantId=Guid.Empty y fallaría
// cerrado (403) para todo el mundo.
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();
app.Run();

public partial class Program;
