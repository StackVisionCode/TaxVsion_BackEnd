using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Health;
using BuildingBlocks.Web.Middleware;
using BuildingBlocks.Web.Observability;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using TaxVision.Gateway.Health;
using TaxVision.Gateway.LoadShedding;
using TaxVision.Gateway.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseTaxVisionSerilog("gateway");
builder.Services.AddBuildingBlocks();

// GW-10 — sin override: se deja el default de Kestrel (~28,6 MB). El endpoint con el cuerpo más
// grande que atraviesa el Gateway es POST signature/documents (25 MB), así que el default cubre
// todo con margen. El límite fino por ruta va en ReverseProxy:Routes:*:MaxRequestBodySize.
// CORS explícito para la SPA (orígenes en Cors:Origins).
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(
        "spa",
        policy => policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
    )
);

builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionGatewayRateLimiting(builder.Configuration);
builder.Services.AddLoadShedding(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "gateway");

// GW-06 — el readiness del Gateway es *self*: los 4 HttpEndpointHealthCheck manuales que
// consultaban auth/tenant/customer/cloudstorage se eliminaron. Si /health/ready fallara porque 1 de
// 18 servicios esta caido, el orquestador sacaria el Gateway del balanceador y convertiria una
// degradacion parcial en una caida total. El estado de los upstreams vive en /health/dependencies,
// leido de IProxyStateLookup y devolviendo Degraded, no Unhealthy.
builder
    .Services.AddHealthChecks()
    .AddCheck<ClusterDependenciesHealthCheck>(
        "upstream-clusters",
        failureStatus: HealthStatus.Degraded,
        tags: ["dependencies"]
    );

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Tarea 3 (senior) — validación Host↔tenant en el Gateway (TenantHostGuardMiddleware). El resolver
// llama al `by-host` de Auth reusando la MISMA dirección del cluster YARP "auth" (dev localhost,
// docker auth-api), así no se duplica la config del destino. BaseAddress normalizada con "/" final.
builder.Services.Configure<TenantHostGuardOptions>(
    builder.Configuration.GetSection(TenantHostGuardOptions.SectionName)
);
var authClusterAddress =
    builder
        .Configuration.GetSection("ReverseProxy:Clusters:auth:Destinations")
        .GetChildren()
        .FirstOrDefault()
        ?["Address"]
    ?? "http://localhost:5124/";
builder.Services.AddHttpClient<IHostTenantResolver, HostTenantResolver>(client =>
{
    client.BaseAddress = new Uri(authClusterAddress.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(3);
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseMiddleware<SecurityHeadersMiddleware>();

// GW-01 — antes de CORS y de la autenticación: no tiene sentido gastar validación de token en una
// petición que se va a rechazar, y el 404 debe salir igual con o sin credenciales.
app.UseMiddleware<InternalSurfaceGuardMiddleware>();

app.UseCors("spa");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Capa 1 (Fase 5 del plan de rate limiting) — después de auth para poder leer tenant_id del JWT
// ya validado; antes de la Capa 3 (TenantPropagationMiddleware) y del ruteo a health checks/YARP.
// La propia excluye /health/* de la medición y del shedding.
app.UseMiddleware<LoadSheddingMiddleware>();
app.UseMiddleware<TenantPropagationMiddleware>();

// Después de UseAuthentication (necesita el tenant_id del JWT ya parseado) y antes del ruteo:
// subdominio no registrado → 404 plano; tenant del JWT ≠ tenant del Host → 403.
app.UseMiddleware<TenantHostGuardMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapHealthChecks(
    "/health/dependencies",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("dependencies") }
);
// YARP solo proxya upgrades de WebSocket si el middleware de WebSockets está en el
// pipeline; sin esto el gateway rechaza el handshake `Upgrade: websocket` con 400 y
// socket.io cae a long-polling (que detrás de Cloudflare se rompe por buffering →
// ping timeout). Debe ir antes de MapReverseProxy.
app.UseWebSockets();

app.MapReverseProxy();

app.Run();
