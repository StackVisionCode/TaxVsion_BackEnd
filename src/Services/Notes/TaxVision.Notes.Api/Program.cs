using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.NotesIntegrationEvents;
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
using Serilog;
using StackExchange.Redis;
using TaxVision.Notes.Application;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Infrastructure;
using TaxVision.Notes.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseTaxVisionSerilog("notes-service");

// ---------- MVC + JSON ----------
builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

// ---------- BuildingBlocks + Infrastructure + Auth + OTEL ----------
builder.Services.AddBuildingBlocks();
builder.Services.AddNotesInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "notes-service");

// Autorización por permiso ([HasPermission("notes.read")], Fase 3); los admins pasan siempre.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

// H-05 — fuente de permisos de la Capa 2. Revienta al arrancar si hay endpoints con
// [HasPermission] y la config no pide "Projection": el claim `perm` ya no se emite (Fase
// 7.5.10), así que en modo Jwt esos endpoints darían 403 siempre, en silencio.
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// Notes Fase 9 (03_Plan_De_Fases.md) — resource ownership sobre Note, apagado por default
// (Authorization:ResourceOwnership:Enabled), mismo criterio que Correspondence/Draft. Sin permiso
// "manage" de override: los endpoints de edición de contenido (Update/Visibility/Pin/Color/
// Attach/Detach) son estrictamente del autor (NoteVisibilityPolicy.CanEditContent, Fase 5) — el OR
// "autor o notes.view_all" de Archive/Restore/Delete es un permiso DISTINTO por acción y por eso
// NO puede expresarse con este mismo handler genérico (una sola instancia = un solo "manage
// permission" para TODAS las operaciones de Note); ese OR ya vive, y se queda, en el chequeo
// explícito de NoteVisibilityPolicy.CanManage dentro del handler de Application (Fase 5/6).
builder.Services.AddResourceOwnershipOptions(builder.Configuration);
builder.Services.AddOwnershipAuthorization<Note>();

// Rate limiting por tenant/usuario — mismo [RateLimit]/IRateCounter tiered que corre en el resto
// del monorepo desde Fase 3/4.2 del plan RateLimit. Piloto de tier-aware quotas real: Fase 4B/4
// de este plan (Notes) conecta ITenantPlanCodeReader/IPlanRateLimitReader.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// RateLimit Fase 2 — tier-aware quotas. Flag OFF por default (fail-open a la cuota base sin
// escalar, vía NullTenantPlanCodeReader/NullPlanRateLimitReader de AddTieredRateLimiting) hasta
// que Fase 4 de este plan conecte los lectores reales.
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

// ---------- Health checks ----------
var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<NotesDbContext>("sql-server", tags: ["ready"])
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
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, NotesDbContext>();
    options.Policies.AutoApplyTransactions();

    options.ApplyStandardFailurePolicies();

    // PUBLICADOS (guardrail 13) — Fase 5 conecta la publicación real de los 4 eventos de
    // integración de Note (contratos en BuildingBlocks.Messaging.NotesIntegrationEvents desde
    // Fase 5). Sin esta línea, bus.PublishAsync descarta el evento en silencio.
    options.PublishMessage<NoteCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<NoteUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<NoteDeletedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<NoteAttachmentDetachedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    // RBAC Fase 5 — restaura BuildingBlocks.Web.Tenancy.TenantContext dentro del scope que Wolverine
    // crea para cada handler (bus.InvokeAsync local o consumer de integration event).
    options
        .Policies.ForMessagesOfType<IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

    // Cola propia desde el arranque (Fase 0) aunque todavia no haya ningun consumer — mismo patron
    // que Correspondence/Connectors/Postmaster/Scribe: el binding queda listo en Rabbit antes de
    // que exista logica (Notes empieza a consumir eventos de RBAC/RateLimit/Customer a partir de
    // Fase 3/4/4B).
    options
        .ListenToRabbitQueue("notes-events", queue => queue.BindExchange("taxvision-events", string.Empty))
        .UseDurableInbox();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Notes API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();

// RBAC Fase 5/7 — el tenant se resuelve SOLO del claim tenant_id del JWT verificado. Va ANTES de
// UseAuthorization(): en modo Projection, [HasPermission] necesita el tenant ya poblado durante
// su propia evaluación, que corre dentro de UseAuthorization().
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();

app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

public partial class Program;
