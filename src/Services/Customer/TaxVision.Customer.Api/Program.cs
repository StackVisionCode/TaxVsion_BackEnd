using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using TaxVision.Customer.Api.Authorization;
using TaxVision.Customer.Application.Customers.Commands.Create;
using TaxVision.Customer.Infrastructure;
using TaxVision.Customer.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseTaxVisionSerilog("customer-service");

// ---------- MVC + JSON ----------
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks + Infrastructure + Auth + OTEL ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddCustomerInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "customer-service");

// Autorización por permiso ([HasPermission("customers.*")]); los admins pasan siempre.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// M2M interno (Correspondence Fase 2) — solo otros microservicios backend, nunca un usuario
// humano. Mismo patrón que Postmaster/Connectors/Subscription (claim actor_type=Service emitido
// por Auth vía client_credentials). Ver InternalCustomersController.
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy("ServiceOnly", policy => policy.RequireClaim("actor_type", "Service"));

// Rate limiter dedicado para revelar tax identifiers en claro: 5 req/min por
// usuario+ruta. Desanima el scraping de SSN/EIN aunque el actor tenga el
// permiso — un preparador legitimo no necesita revelar mas de un puñado por
// minuto, un script si.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "fiscal-reveal",
        context =>
        {
            var userId =
                context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: $"{userId}:{path}",
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

// ---------- Health checks ----------
var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<CustomerDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(CreateCustomerHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CustomerDbContext>();
    options.Policies.AutoApplyTransactions();

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));

    // Consume File* de CloudStorage (resultado del escaneo del archivo de import subido
    // directo a MinIO, ver ImportFileScanResultConsumer — Fase D para Customer).
    options
        .ListenToRabbitQueue(
            "customer-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    options.PublishMessage<CustomerArchivedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerPortalInvitationRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomersBulkImportedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerImportFailedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerReactivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerActivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerDeactivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerPreparerAssignedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<CustomerPreparerUnassignedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Fase D — reemplaza la tabla CustomerImportFiles: el import sube directo a MinIO y
    // publica esto para que CloudStorage lo registre/escanee de forma asincrona.
    options.PublishMessage<SaveFileRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Customer API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();
