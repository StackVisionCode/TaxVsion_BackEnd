using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using TaxVision.Signature.Application.Settings.IntegrationEvents;
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
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);

// Autorización por permiso ([HasPermission("signature.*")]); los admins pasan siempre.
// BuildingBlocks.ActorTypeAuthorization — Fase 3 del plan de autorización por actor type,
// reemplaza a la copia local que tenía este servicio.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// Rate limiter para endpoints públicos: 15 req/min por IP+ruta para desanimar
// enumeración/fuerza bruta de tokens.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "public-signature",
        context =>
        {
            var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"{client}:{path}",
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
});

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
    options.PublishMessage<SignatureSettingsUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SignaturePlanConstraintsUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
});

var app = builder.Build();

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
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<TaxVision.Signature.Api.Common.JwtTenantContextMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();
