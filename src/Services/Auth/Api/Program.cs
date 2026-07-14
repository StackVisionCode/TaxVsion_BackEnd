using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using TaxVision.Auth.Api.Authorization;
using TaxVision.Auth.Api.Bootstrap;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Api.Jobs;
using TaxVision.Auth.Api.Middleware;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Application.Users.IntegrationEvents;
using TaxVision.Auth.Infrastructure;
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
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddBuildingBlocks();
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.Configure<PlatformBootstrapOptions>(
    builder.Configuration.GetSection(PlatformBootstrapOptions.SectionName)
);
builder.Services.AddHostedService<PlatformAdminBootstrapService>();
builder.Services.AddHostedService<TenantDomainBackfillService>();
builder.Services.AddHostedService<TenantDomainProvisioningPoller>();
builder.Services.AddHostedService<AuthMaintenanceService>();

// Contexto de request (IP/user-agent) para auditoría y sesiones.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestContext, RequestContext>();

// Tenant candidato resuelto desde el Host (Fase A3) — ver TenantHostResolutionMiddleware.
builder.Services.AddScoped<IResolvedTenantContext, ResolvedTenantContext>();

builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);

// Autorización por permisos: [HasPermission("users.invite")] ⇒ claim "perm".
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "auth-service");

// Rate limiting para los endpoints públicos de resolución de tenant (Fase A4) —
// partición por IP real (ya normalizada por ForwardedHeadersMiddleware, que corre
// antes en el pipeline). "tenant-recovery" es más estricto: dispara un envío de
// email real y es el vector más atractivo para enumeración de tenants por email.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

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

    static string PartitionKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
});

var authRabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);
var authRedis = HostPort.Parse(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379", 6379);
builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<AuthDbContext>("sql-server", tags: ["ready"])
    .AddCheck("redis", new TcpEndpointHealthCheck(authRedis.Host, authRedis.Port), tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(authRabbitUri.Host, authRabbitUri.Port), tags: ["ready"]);

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

    // Eventos publicados por Auth
    options.PublishMessage<UserRegisteredIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<InvitationCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<UserDeactivatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<UserRolesChangedIntegrationEvent>().ToRabbitExchange("taxvision-events");
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
app.UseAuthorization();
app.UseRateLimiter();

// Revocación inmediata de access tokens de sesiones denylistadas (Redis).
app.UseMiddleware<SessionDenylistMiddleware>();

// Fase L1.4 — bloquea con 409 a un tenant que no acepto la version vigente del ToS/AUP.
app.UseMiddleware<TermsAcceptanceMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();
