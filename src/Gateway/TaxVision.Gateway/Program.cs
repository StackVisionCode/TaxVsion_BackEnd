using BuildingBlocks.Common;
using BuildingBlocks.Health;
using BuildingBlocks.Middleware;
using BuildingBlocks.Observability;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using TaxVision.Gateway.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseTaxVisionSerilog("gateway");
builder.Services.AddBuildingBlocks();

// CORS explícito para la SPA (orígenes en Cors:Origins).
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(
        "spa",
        policy => policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
    )
);

builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionGatewayRateLimiting();
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "gateway");
builder.Services.AddHttpClient("taxvision-health", client => client.Timeout = TimeSpan.FromSeconds(5));

var authHealth = new Uri(
    new Uri(builder.Configuration["ReverseProxy:Clusters:auth:Destinations:auth1:Address"]!),
    "health/ready"
).ToString();
var tenantHealth = new Uri(
    new Uri(builder.Configuration["ReverseProxy:Clusters:tenant:Destinations:tenant1:Address"]!),
    "health/ready"
).ToString();
var customerHealth = new Uri(
    new Uri(builder.Configuration["ReverseProxy:Clusters:customer:Destinations:customer1:Address"]!),
    "health/ready"
).ToString();
var cloudStorageHealth = new Uri(
    new Uri(builder.Configuration["ReverseProxy:Clusters:cloudstorage:Destinations:cloudstorage1:Address"]!),
    "health/ready"
).ToString();

builder
    .Services.AddHealthChecks()
    .AddTypeActivatedCheck<HttpEndpointHealthCheck>(
        "auth-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        args: [authHealth]
    )
    .AddTypeActivatedCheck<HttpEndpointHealthCheck>(
        "tenant-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        args: [tenantHealth]
    )
    .AddTypeActivatedCheck<HttpEndpointHealthCheck>(
        "customer-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        args: [customerHealth]
    )
    .AddTypeActivatedCheck<HttpEndpointHealthCheck>(
        "cloudstorage-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        args: [cloudStorageHealth]
    );

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Headers de seguridad para todas las respuestas.
app.Use(
    async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!app.Environment.IsDevelopment())
        {
            context.Response.Headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains";
        }
        await next();
    }
);

app.UseCors("spa");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<TenantPropagationMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapReverseProxy();

app.Run();
