using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;
using TaxVision.Notification.Api.Authorization;
using TaxVision.Notification.Api.Common;
using TaxVision.Notification.Api.Jobs;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Infrastructure;
using TaxVision.Notification.Infrastructure.Persistence;
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
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks (correlación + tenant context) ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddNotificationInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);

// Autorización por permiso ([HasPermission("notification.*")]); los admins pasan siempre.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

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
builder.Services.Configure<ServiceAuthClientOptions>(builder.Configuration.GetSection(ServiceAuthClientOptions.SectionName));
builder.Services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>(
    (sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
        client.BaseAddress = new Uri(options.AuthBaseUrl);
    }
);

// Secreto de los webhooks de tracking (delivered/opened/clicked/bounced).
builder.Services.Configure<EmailWebhookOptions>(builder.Configuration.GetSection(EmailWebhookOptions.SectionName));

// Scheduler de campañas: inicia el fan-out cuando llega la hora programada.
builder.Services.AddHostedService<CampaignSchedulerService>();

// Scheduler de sincronización de cuentas de correo (incremental periódico).
builder.Services.AddHostedService<EmailSyncSchedulerService>();
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
    options.PublishMessage<EmailAccountConnectedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailAccountDisconnectedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailFullSyncRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailIncrementalSyncRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailSyncCompletedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<EmailSyncFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");

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
app.UseAuthorization();
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();
