using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using TaxVision.CloudStorage.Application.Files.Commands;
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
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddBuildingBlocks();
builder.Services.AddCloudStorageInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "cloudstorage-service");
builder.Services.AddAuthorization(options =>
{
    foreach (
        var permission in new[]
        {
            CloudStoragePermissions.FileView,
            CloudStoragePermissions.FileUpload,
            CloudStoragePermissions.FileDownload,
            CloudStoragePermissions.FileDelete,
            CloudStoragePermissions.SettingsManage,
            CloudStoragePermissions.AuditView,
            CloudStoragePermissions.RecycleBinManage,
            CloudStoragePermissions.FolderManage,
            CloudStoragePermissions.ShareCreate,
            CloudStoragePermissions.ShareRevoke,
            CloudStoragePermissions.ShareManage,
            CloudStoragePermissions.LegalManage,
            CloudStoragePermissions.DmcaCounterNotice,
        }
    )
        options.AddPolicy(permission, policy => policy.RequireClaim("perm", permission));
});

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
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"{client}:{path}",
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
    // Fase B2 — 5 req/min por usuario: un ZIP puede agregar hasta 500 archivos/500MB
    // (ver CloudStorageOptions), asi que el costo por request es mucho mayor que un
    // download de un solo archivo — el limite es deliberadamente mas estricto.
    options.AddPolicy(
        "zip-download",
        context =>
        {
            var actorId =
                context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"zip:{actorId}",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            );
        }
    );
});

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
    options.PublishMessage<ShareLinkRevokedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkFolderItemAddedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkAccessedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkAccessDeniedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkExpiredIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<ShareLinkPermissionChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Auditoría 2026-07-21: estos 3 quedaron fuera de este whitelist explícito desde que se
    // agregaron (Fase L1.2/L1.3 legal hold + DMCA) — bus.PublishAsync(...) los publica sin
    // error (Wolverine no lanza si no hay ruta), pero nunca llegaban a taxvision-events, así
    // que los consumers de Communication nunca los recibían pese a estar bien implementados.
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

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
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
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();
app.Run();

public partial class Program;
