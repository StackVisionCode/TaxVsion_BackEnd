using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.Authorization;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using TaxVision.Tenant.Api.Authorization;
using TaxVision.Tenant.Api.Common;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Infrastructure;
using TaxVision.Tenant.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseTaxVisionSerilog("tenant-service");

// 1) Structured loggin with Serilog
// builder.Host.UseSerilog((context, logger) => logger
//     .ReadFrom.Configuration(context.Configuration)
//     .Enrich.FromLogContext()
//     .Enrich.WithProperty("service", "tenant-service")
//     .WriteTo.Console()
//     .WriteTo.File(
//         Path.Combine(AppContext.BaseDirectory, "Logs", "tenant-.log"),
//         rollingInterval: RollingInterval.Day,
//         retainedFileCountLimit: 30));
builder.Services.AddSwaggerGen();
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// 2) Services Shared plus Services's Infrastructure
builder.Services.AddBuildingBlocks();

//  Added Cache's Services
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddTenantInfrastructure(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "tenant-service");

// Autorización por permiso ([HasPermission(...)], ver TenantBrandingController) — mismo mecanismo
// que Postmaster/Signature/Notification/Customer. Coexiste con las policies nombradas de abajo:
// PermissionPolicyProvider solo intercepta el prefijo "perm:", el resto cae al provider default.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// Acepta el ticket firmado por Auth (ReserveSubdomainHandler, claim reg_slug) o un
// PlatformAdmin creando un tenant directamente — ver TenantController.Create.
builder
    .Services.AddAuthorizationBuilder()
    .AddPolicy(
        "TenantRegistration",
        policy =>
            policy.RequireAssertion(context =>
                context.User.HasClaim("purpose", "tenant-registration") || context.User.IsInRole("PlatformAdmin")
            )
    );

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(
        "tenant-registration",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    // Tenant_Service_LogoSupport_Plan.md §10 — 10 uploads/hora, particionado por tenant (no por IP,
    // a diferencia de tenant-registration) para que un tenant ruidoso no consuma el cupo de otro
    // detrás del mismo NAT/proxy corporativo.
    options.AddPolicy(
        "tenant-logo-upload",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.TryGetTenantId(out var tid) ? tid.ToString() : "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                }
            )
    );
});

var tenantRabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);
var tenantRedis = HostPort.Parse(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", 6379);
builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<TenantDbContext>("sql-server", tags: ["ready"])
    .AddCheck("redis", new TcpEndpointHealthCheck(tenantRedis.Host, tenantRedis.Port), tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(tenantRabbitUri.Host, tenantRabbitUri.Port), tags: ["ready"]);

// 3)
builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(CreateTenantHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var rabbitUri =
        builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.");

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(new Uri(rabbitUri)).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, TenantDbContext>();
    options.Policies.AutoApplyTransactions();

    options.PublishMessage<TenantCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantStatusChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<SaveFileRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantLogoUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<TenantLogoRemovedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options
        .Policies.OnException<Exception>()
        .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
});
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(a =>
    {
        a.SwaggerEndpoint("/openapi/v1.json", "API v1");
    });
}

//4) Middleware's Pipe Line (the order of this, it's important)
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();

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
