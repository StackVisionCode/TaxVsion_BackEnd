using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.ScribeIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using TaxVision.Scribe.Api.Authorization;
using TaxVision.Scribe.Application;
using TaxVision.Scribe.Infrastructure;
using TaxVision.Scribe.Infrastructure.Persistence;
using TaxVision.Scribe.Infrastructure.Retention;
using TaxVision.Scribe.Infrastructure.Seed;
using TaxVision.Scribe.Infrastructure.Startup;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseTaxVisionSerilog("scribe-service");

// ---------- MVC + JSON ----------
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks + Infrastructure + Auth + OTEL ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddScribeInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "scribe-service");

// Autorización por permiso ([HasPermission("scribe.*")]); los admins pasan siempre.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// Sube y publica los layouts base system-base/tenant-base (Fase 4.6) si todavía no existen.
builder.Services.AddHostedService<ScribeBaseLayoutSeeder>();

// Fase 8: siembra los 13 templates migrados desde Notification + sus EventTemplateMapping.
// Corre después del seeder de layouts — IHostedService.StartAsync se ejecuta en orden de registro.
builder.Services.AddHostedService<ScribeNotificationTemplateSeeder>();

// Precarga en cache (L1+L2) todo template Published (Fase 6) — corre después del seeder.
builder.Services.AddHostedService<TemplateWarmupService>();

// Fase 10: retention job — purga versiones Archived viejas. Deshabilitado por default
// (Scribe:Retention:Enabled) hasta que se autorice explícitamente.
builder.Services.Configure<ScribeRetentionOptions>(
    builder.Configuration.GetSection(ScribeRetentionOptions.SectionName)
);
builder.Services.AddHostedService<ScribeRetentionScheduler>();

// ---------- Health checks ----------
var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<ScribeDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(AssemblyMarker).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(rabbitUri).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, ScribeDbContext>();
    options.Policies.AutoApplyTransactions();

    // Consume TenantLogoUpdated/Removed (Fase 4.5 — logo pipeline).
    options
        .ListenToRabbitQueue(
            "scribe-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    // Publica los eventos propios del microservicio al exchange fan-out del ecosistema.
    options.PublishMessage<ScribeTenantLogoMissingDetectedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Scribe API v1"));
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
app.UseMiddleware<TenantResolutionMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();
