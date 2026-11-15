# Investigación — Arquitectura de un microservicio TaxVision

> Fecha: 2026-11-15. Solo lectura. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\` salvo indicación.
> Etiquetas: **Código verificado** · **Documentación histórica** · **Recomendación** · **Decisión pendiente**.
> Servicio de referencia principal: **Notes**; complementario para eventos de dominio: **Customer** (justificación en `03_INVENTARIO_ARQUITECTURA.md` §5).

## Estado actual

### Capas reales

**Código verificado** (referencias de `src/Services/Notes/*/*.csproj`):

| Capa | SDK | Referencias de proyecto | Paquetes | Contiene |
|---|---|---|---|---|
| `TaxVision.<Svc>.Domain` | `Microsoft.NET.Sdk` | `BuildingBlocks.csproj` | ninguno | Agregados, entidades hijas, VOs, enums, `<Agg>Errors`, eventos de dominio, proyecciones locales (read models) |
| `TaxVision.<Svc>.Application` | `Microsoft.NET.Sdk` | Domain, `BuildingBlocks.csproj`, `BuildingBlocks.Messaging` | `WolverineFx` | Commands/queries + handlers estáticos, consumers, puertos (`Abstractions/`), DTOs, `AssemblyMarker` |
| `TaxVision.<Svc>.Infrastructure` | `Microsoft.NET.Sdk` | Application, `BuildingBlocks.Infrastructure`, `BuildingBlocks.EfCore` (+ kits opcionales: `CustomerVisibility`, `TenantTimeZones`, `Jobs`, `Imaging`) | `Microsoft.EntityFrameworkCore.SqlServer`, `.Design`, `Microsoft.Extensions.Http`, `System.Security.Cryptography.Pkcs` (+ propios) | DbContext, configuraciones EF, migraciones, repositorios, clientes HTTP M2M, jobs, `DependencyInjection.Add<Svc>Infrastructure` |
| `TaxVision.<Svc>.Api` | `Microsoft.NET.Sdk.Web` (+ `UserSecretsId`) | Infrastructure, `BuildingBlocks.csproj`, `BuildingBlocks.Web`, `BuildingBlocks.Messaging`, `BuildingBlocks.Authorization` | JwtBearer, OpenApi, EF Design, HealthChecks.EF, Swashbuckle, `WolverineFx*` (5) | `Program.cs`, controladores, requests, autorización propia, `Internal/Generated` (código Wolverine pregenerado), `Dockerfile` |

Variantes reales: Auth añade un subcontexto `Onboarding/{Application,Domain,Infrastructure}`; Growth tiene dos contextos (`Codes`, `Referrals`) con un solo Api/Infrastructure (`TaxVision.slnx` comentario "modular deployment, bounded contexts remain independently extractable"); Tasks añade `ClientForms.{Application,Domain}` y `DataCollection.Engine`; Connectors añade el worker `MailboxListener`; ContentScanner es un único proyecto sin base ni mensajería (desvío aceptado por `DomainDrivenDesign/ContentScanner.md`).

### Referencias permitidas y prohibidas (fitness tests)

**Código verificado** — `deploy/tests/TaxVision.BuildingBlocks.Testing/Architecture/LayeringRulesBase.cs:35-53` (heredada por los 26 servicios, más `ClientFormsLayeringTests`, `OnboardingLayeringTests`, `CodesLayeringTests`, `ReferralsLayeringTests`):

| Regla | Prohibido |
|---|---|
| `Domain_ShouldNotDependOn_EntityFramework` | `Microsoft.EntityFrameworkCore` en Domain |
| `Domain_ShouldNotDependOn_AspNetCore` | `Microsoft.AspNetCore` en Domain |
| `Domain_ShouldNotDependOn_Wolverine` | `Wolverine` en Domain |
| `Application_ShouldNotDependOn_Infrastructure` | `<Svc>.Infrastructure` en Application |
| `Application_ShouldNotDependOn_EntityFramework` | `Microsoft.EntityFrameworkCore` en Application |

Reglas adicionales por servicio (ejemplos verificados):
- `NotesArchitectureTests`: cada acción declara `[AllowActorTypes]`; cada acción declara `[RateLimit]` o `[RateLimitExempt]`; los controladores no dependen de Infrastructure (`deploy/tests/TaxVision.Notes.Tests/Architecture/NotesArchitectureTests.cs:24,76,129`).
- Cero LINQ en Domain: `CalendarArchitectureTests.cs:47-49`, `TasksArchitectureTests.cs:162`.
- `PreGeneratedHandlerCodeTests` (26 servicios): todo handler carga desde código pregenerado en Production y el código coincide con los handlers actuales.
- Transversales en `deploy/tests/TaxVision.BuildingBlocks.Tests/Architecture/`: `SaveBeforePublishTests`, `LocalQueueDurabilityTests`, `DomainEventHandlerConventionTests`, `CorrelationIdFitnessTests`, `InternalRouteConventionFitnessTests`, `NamespaceAssemblyContractTests`, `RepositoryInterfaceTests`, `ConstructorBusTests`, `JobPublishingTests`, `ClientIpForwardingFitnessTests`, `TimeZoneInfraFitnessTests`, `NoPlanNameLogicFitnessTests`, `IntegrationIsolationTests`.

**Matiz importante:** `BuildingBlocks.csproj` referencia `WolverineFx` (por `[SagaIdentity]` en `AggregateRoot`, `src/BuildingBlocks/BuildingBlocks.csproj:36-43`), así que **el ensamblado** Domain arrastra Wolverine de forma transitiva; la fitness controla **tipos** (`HaveDependencyOn`), no referencias de ensamblado.

### Árbol de carpetas real (Notes, sin `bin/obj/Migrations/Generated`)

```
src/Services/Notes/
├── TaxVision.Notes.Api/
│   ├── Authorization/          # handlers de autorización propios (p. ej. OffboardedAuthorNoteEditHandler)
│   ├── Controllers/            # NotesController.cs, PortalNotesController.cs
│   ├── Internal/Generated/WolverineHandlers/   # código pregenerado (codegen Static)
│   ├── Properties/
│   ├── Requests/
│   ├── Dockerfile
│   └── Program.cs
├── TaxVision.Notes.Application/
│   ├── AssemblyMarker.cs
│   ├── Backfill/{Abstractions}
│   ├── Customers/{Abstractions}
│   ├── Notes/{Abstractions,Commands,Consumers,Queries}
│   ├── Permissions/{Abstractions,Consumers}
│   ├── Projections/{Abstractions,AuthEvents,CustomerEvents}
│   └── RateLimiting/{Abstractions,Consumers}
├── TaxVision.Notes.Domain/
│   ├── Backfill/  Notes/  Permissions/  Projections/  RateLimiting/
│   └── ValueObjects/           # NoteColor.cs, NoteContent.cs, NoteReference.cs
└── TaxVision.Notes.Infrastructure/
    ├── CloudStorage/  Customers/  Jobs/  Notes/  Permissions/  RateLimiting/  Reconciliation/
    ├── Persistence/
    │   ├── Configurations/     # un IEntityTypeConfiguration<T> por entidad
    │   ├── Migrations/
    │   ├── Repositories/
    │   ├── NotesDbContext.cs
    │   └── NotesDbContextFactory.cs
    └── DependencyInjection.cs
```

Customer usa además una carpeta por caso de uso (`Customers/Commands/Create/`, `Commands/Archive/`, …) y `Customers/DomainEvents/` para los handlers de eventos de dominio.

### Alta en `TaxVision.slnx`

**Código verificado** (`TaxVision.slnx`): formato XML `<Solution>` con `<Folder Name="/src/Services/<Svc>/">` y un `<Project Path=…/>` por capa (p. ej. `Calendar` en `TaxVision.slnx:143-149`); las pruebas en `<Folder Name="/deploy/tests/">` (`:218-250`). Los proyectos Node **no** están en la solución; el contenedor `migrations` compila la solución completa (`deploy/docker/migrations/Dockerfile`: `dotnet build TaxVision.slnx -c Release`), así que estar en `.slnx` es requisito para que sus migraciones se apliquen.

Observación: los BuildingBlocks "Results, Domain, Persistence, Tenancy, Permissions, ActorTypeAuthorization, Security, Sessions, Caching, TimeZones, RateLimiting, Platform, PurchaseIntentAmount, Common" **no** son proyectos: son carpetas compiladas dentro de `BuildingBlocks.csproj` (los demás folders se excluyen con `<Compile Remove=…>`, `src/BuildingBlocks/BuildingBlocks.csproj:8-31`).

### `Program.cs` típico — orden real (Notes)

**Código verificado** (`src/Services/Notes/TaxVision.Notes.Api/Program.cs`):

| # | Paso | Línea |
|---|---|---|
| 1 | `builder.Host.UseTaxVisionSerilog("notes-service")` | 40 |
| 2 | `AddControllers().AddJsonOptions(JsonStringEnumConverter).AddActorTypeAuthorization()` | 43-46 |
| 3 | `AddOpenApi()`, `AddSwaggerGen()` | 48-49 |
| 4 | `AddBuildingBlocks()` (TenantContext, CorrelationContext, `RetryAfterHeaderFilter`, `SlotGatingOptions`) | 52 |
| 5 | `AddTimeZoneSelfCheck()` | 53 |
| 6 | `AddNotesInfrastructure(builder.Configuration)` | 54 |
| 7 | `AddRedisCache`, `AddSessionDenylist`, `AddTaxVisionJwtAuthentication`, `AddTaxVisionOpenTelemetry(…, "notes-service")` | 55-58 |
| 8 | `IAuthorizationPolicyProvider → PermissionPolicyProvider`; `ModuleGateRegistration.AddModuleGate`; `AddUserPermissionsSource(config, Assembly.GetExecutingAssembly())` | 61-72 |
| 9 | Autorización por recurso (`AddResourceOwnershipOptions`, `AddOwnershipAuthorization<Note>()`) | 80-82 |
| 10 | Rate limit: `AddTaxVisionRedis`, `IRateCounter → RedisRateCounter`, lectores de plan bajo `RateLimit:EnforceTierQuotas`, `AddTieredRateLimiting()` | 87-107 |
| 11 | Health checks: `AddDbContextCheck<NotesDbContext>("sql-server", tags: ["ready"])` + `TcpEndpointHealthCheck` de RabbitMQ | 110-117 |
| 12 | `UseWolverine`: codegen, discovery, `UseTaxVisionRabbitMq`, `PersistMessagesWithSqlServer`, outbox durable, EF transactions + `AutoApplyTransactions`, `UseTaxVisionConflictPolicy`, `ApplyStandardFailurePolicies`, `UseTaxVisionDurableLocalQueues`, `PublishMessage<T>` por evento, middlewares de tenant, cola `notes-events` | 119-172 |
| 13 | `AddTaxVisionClientIpForwarding` | 174 |
| 14 | Pipeline: `UseTaxVisionClientIp` → (Dev) OpenAPI/Swagger → `CorrelationIdMiddleware` → `UseSerilogRequestLogging` → `ExceptionHandlingMiddleware` → (no Dev) `UseHttpsRedirection` → `UseAuthentication` → `JwtTenantContextMiddleware` → `SessionDenylistMiddleware` → `UseAuthorization` | 179-205 |
| 15 | `/health/live` (sin checks) y `/health/ready` (tag `ready`), `MapControllers()`, `app.Run()`, `public partial class Program;` | 207-214 |

Customer añade `UseTaxVisionDomainEvents()` + `LocalQueue(DomainEventsQueue).Sequential()` (`Customer/Program.cs:164-165`), `/health/key-versions` y colas punto a punto.

JWT (**Código verificado**, `src/BuildingBlocks/BuildingBlocks.Web/Security/JwtAuthenticationRegistration.cs:16-111`): RS256 si hay `Jwt:PublicKeyPem` o `Jwt:PublicKeyPath`; si no, HS256 con `Jwt:Secret` (≥32 bytes). Exige `Jwt:Issuer` y `Jwt:Audience`. En compose: `Jwt__PublicKeyPath: /run/secrets/jwt-public.pem`, `Jwt__Issuer: TaxVision.Auth`, `Jwt__Audience: TaxVision.Services`.

Codegen (**Código verificado**, `src/BuildingBlocks/BuildingBlocks.Web/Hosting/WolverineCodeGenerationExtensions.cs:20-44`): `Static` en Production, `Dynamic` en el resto; se fuerza con `Wolverine:CodeGeneration:TypeLoadMode` (variable `Wolverine__CodeGeneration__TypeLoadMode`, doble guion bajo). El código vive en `Internal/Generated` del Api y se regenera con `scripts/codegen/regenerate-wolverine-code.ps1`.

### Result / Error

**Código verificado:**
- `src/BuildingBlocks/Results/Result.cs`: `Result` / `Result<T>` con `IsSuccess`, `IsFailure`, `Error`; acceder a `Value` de un fallo lanza `InvalidOperationException`.
- `src/BuildingBlocks/Results/Error.cs`: `sealed record Error(string Code, string Message)` + `Error.None` + `RetryAfterSeconds` opcional (`WithRetryAfter`).
- `ConflictException` (`Persistence.ConcurrencyConflict`, `Persistence.UniqueConstraint`) la lanza el DbContext (`NotesDbContext.cs:88-110`) y `ExceptionHandlingMiddleware` la traduce a 409 (`src/BuildingBlocks/BuildingBlocks.Web/Middleware/ExceptionHandlingMiddleware.cs:34-35`); `ModuleUnavailableException` → 403.
- HTTP: `Error.ToHttpStatusCode()` en `src/BuildingBlocks/BuildingBlocks.Web/Results/ErrorHttpMapping.cs` (518 líneas): **lista explícita de códigos** + algunas reglas por prefijo/sufijo solo para `Codes.*`/`Referral*`; **default 400** (`:516`). Un `<Agg>.NotFound` nuevo que no se registre ahí responde 400.

### ¿Hace falta otro microservicio?

Criterios usados por el proyecto (**Documentación histórica vigente**, ADRs):
- ADR-018 (Notes): servicio propio si **no tiene invariantes de consistencia inmediata con otro contexto** y **ningún otro servicio lo llama en su write path** (`documents/architecture/ADR_018_Notes_Bounded_Context.md:25-41`).
- ADR-021 (Reminder): un **mecanismo compartido** (scheduler) es una **biblioteca**; un subdominio con **máquina de estados, invariantes y ciclo de vida propios** es un bounded context (`ADR_021_Reminder_Bounded_Context.md:15-48`).
- ADR-022 (Tasks): el criterio no es "cuántas tablas" sino **de quién es el invariante** (`ADR_022_Task_Bounded_Context.md:16-27`).
- Growth: dos bounded contexts en **un** despliegue (módulos con pruebas que impiden referencias cruzadas; `DomainDrivenDesign/Growth.md`).
- Libro, cap. 10.3: "un monolito modular con estas mismas capas y eventos suele ser un mejor punto de partida" si un solo equipo mantiene todo (`DomainDrivenDesign/Guia-DDD-EDA-…pdf`, p. 65).
- Workers sin dominio (ContentScanner, CommunicationTranscriptWorker, Connectors.MailboxListener): proceso aparte cuando el recurso de cómputo o el ciclo de vida difiere (ONNX/ffmpeg, whisper, IMAP IDLE), no por dominio.

### Principios del libro adaptados a TaxVision

| Regla del libro (`Guia-DDD-EDA-NET10-…pdf`) | En TaxVision | Estado |
|---|---|---|
| Dependencias apuntan al dominio; Domain no referencia paquetes técnicos (p. 6) | Igual, vigilado por `LayeringRulesBase` | Igual |
| Application **no** referencia Wolverine (p. 8, p. 20) | Application referencia `WolverineFx` e inyecta `IMessageBus` | **Desvío aceptado** (`DomainDrivenDesign/00-Rubrica.md`, "IMessageBus en Application") |
| `IUnitOfWork` en Application, `IOrderRepository` en Domain (p. 7, 21) | `IUnitOfWork` en `BuildingBlocks.Persistence`, implementado por el propio DbContext; repositorios declarados en `Application/<Feature>/Abstractions` | Desvío (shared kernel) |
| Excepciones de negocio + `GlobalExceptionHandler` (p. 42-43) | `Result`/`Error` + `ErrorHttpMapping` | **Desvío aceptado** (rúbrica) |
| Wolverine abre la transacción antes del handler; `SaveChangesAsync` no confirma solo (p. 23) | Igual (`UseEntityFrameworkCoreTransactions` + `AutoApplyTransactions`); un `try/catch` dentro del handler no cubre la caída de la base (`memory/feedback_wolverine_transaccion_antes_del_handler.md`) | Igual |
| Evento de dominio → handler devuelve evento de integración en cascada (p. 30) | `UseTaxVisionDomainEvents()` (BB-1) con `AddDbContext` normal; **no** `AddDbContextWithWolverineIntegration` (no guarda en el inbox durable con 6.14) | Adaptado (`src/BuildingBlocks/BuildingBlocks.Web/Hosting/DomainEventDispatchExtensions.cs:4-23`) |
| `UseDurableLocalQueues` + outbox durable (p. 37) | `UseTaxVisionDurableLocalQueues()` + `UseDurableOutboxOnAllSendingEndpoints()` | Igual (19/26 servicios) |
| `UseRabbitMq(...).AutoProvision()` (p. 37) | `UseTaxVisionRabbitMq(uri)` (añade confirmaciones de publicación) | Mejorado |
| Exchange por servicio (`orders.events`) | Un solo fanout `taxvision-events` | Desvío deliberado |
| Contratos en proyecto `Contracts` | `src/BuildingBlocks/Messaging` | Equivalente (rúbrica) |
| Migraciones: paso del despliegue, no al arrancar (p. 65) | Igual (contenedor `migrations`) | Igual |
| `TypeLoadMode.Static` antes de producción (p. 65) | `UseTaxVisionCodeGeneration` | Igual |
| `TenantId` en eventos y validado en cada consumidor (p. 65) | `IntegrationEvent.TenantId` + guarda post-fetch en consumers | Igual |
| Cadena de conexión con contraseña en `appsettings.Development.json` (p. 43) | **No**: user-secrets en local, `.env` generado por `deploy.yml` en producción | TaxVision es más estricto; no copiar el ejemplo del libro |

## Evidencia

Rutas citadas arriba; principales: `src/Services/Notes/TaxVision.Notes.Api/Program.cs`, `src/Services/Customer/TaxVision.Customer.Api/Program.cs`, `deploy/tests/TaxVision.BuildingBlocks.Testing/Architecture/LayeringRulesBase.cs`, `src/BuildingBlocks/BuildingBlocks.Web/Results/ErrorHttpMapping.cs`, `DomainDrivenDesign/00-Rubrica.md`, libro pp. 6-8, 20-23, 30, 37-38, 42-43, 64-65.

## Problemas

1. **Documentación histórica:** `DEVS/Guia_Creacion_Microservicio.md:293-311` muestra un `Program.cs` "real de Notes" al que le faltan `UseTaxVisionCodeGeneration`, `AddTimeZoneSelfCheck`, `UseTaxVisionRabbitMq`, `ApplyStandardFailurePolicies`, `UseTaxVisionConflictPolicy`, `UseTaxVisionDurableLocalQueues`, `PublishMessage`, client IP forwarding y el orden de middleware completo.
2. `ErrorHttpMapping` central obliga a tocar BuildingBlocks por cada código `NotFound`/`Conflict` nuevo (aditivo, pero fácil de olvidar → 400).
3. El ensamblado Domain arrastra `WolverineFx` vía `BuildingBlocks.csproj`.
4. Comentarios con fases y narrativa en el servicio de referencia (guardrail #36).

## Buenas prácticas

- Un `Program.cs` sin lógica propia: todo lo transversal vía extensiones de BuildingBlocks; lo del servicio vía `Add<Svc>Infrastructure`.
- Handler estático `Handle(cmd, deps…, CancellationToken ct)`; persistir con `IUnitOfWork.SaveChangesAsync` y **después** `bus.PublishAsync` (fitness `SaveBeforePublishTests`).
- Controladores finos: identidad del JWT (`this.TryGetTenantAndUser`), `bus.InvokeAsync<Result<T>>`, `ToHttpStatusCode()`.
- `public partial class Program;` al final para `WebApplicationFactory`.

## Recomendación

- La guía maestra toma el `Program.cs` de Notes **actual** como plantilla (ver Material) y añade el bloque de eventos de dominio de Customer solo si el servicio emite eventos de dominio.
- Exigir en el servicio nuevo: `LayeringRulesBase`, `PreGeneratedHandlerCodeTests`, `UtcDateTimesTests`, `LocalQueueDurabilityRules`, prueba de controladores (`AllowActorTypes`/`RateLimit`) y, si publica eventos, una prueba de rutas como la de Calendar.
- **Decisión pendiente:** si los códigos de error nuevos se registran en `ErrorHttpMapping` (central) o se adopta una convención por sufijo para todo el repo (cambiaría comportamiento: requiere decisión del dueño por la regla "BuildingBlocks solo aditivo").

## Riesgos

- Olvidar `PublishMessage<T>` (descarte silencioso) o la cola enlazada (consumer que nunca corre).
- Olvidar regenerar `Internal/Generated` → en Production (Static) el handler nuevo no carga; `PreGeneratedHandlerCodeTests` lo detecta en el gate local.
- Copiar `UseRabbitMq(...).AutoProvision()` de la guía antigua o del libro pierde las confirmaciones de publicación.

## Pruebas

- Heredar `LayeringRulesBase` (con `extern alias BbTesting`).
- Copiar de Notes `NotesArchitectureTests`, `PreGeneratedHandlerCodeTests`, `UtcDateTimesTests` (`deploy/tests/TaxVision.Notes.Tests/Architecture/`).
- Gate exacto: `dotnet test TaxVision.slnx --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"` (`DomainDrivenDesign/01-Convenciones-Pruebas.md`).

## Dependencias

`Investigaciones/BuildingBlocks.md` (qué pieza aporta cada extensión), `Investigaciones/Migraciones.md` (DbContext, factory), `Referencias/Guardrails.md` (#13-#17, #36).

## Material para la guía

### Paso a paso (estructura)

1. Crear 4 proyectos `TaxVision.<Svc>.{Domain,Application,Infrastructure,Api}` con `net10.0`, `ImplicitUsings`, `Nullable`; solo Api con `Sdk.Web` y `UserSecretsId`.
2. Referencias exactamente como la tabla "Capas reales".
3. Añadir `<Folder Name="/src/Services/<Svc>/">` con los 4 proyectos en `TaxVision.slnx`, y el proyecto de pruebas en `/deploy/tests/`.
4. Crear `AssemblyMarker` en Application; `Add<Svc>Infrastructure` en Infrastructure.
5. Copiar el `Program.cs` de referencia y cambiar nombre del servicio, DbContext, eventos y cola.
6. Registrar los códigos `NotFound`/`Conflict` nuevos en `ErrorHttpMapping` (cambio aditivo en BuildingBlocks).
7. Generar el código Wolverine (`scripts/codegen/regenerate-wolverine-code.ps1 -Service <Svc>`).

### Fragmento real — `Application.csproj` (Notes)

```xml
<!-- src/Services/Notes/TaxVision.Notes.Application/TaxVision.Notes.Application.csproj (referencias) -->
<ProjectReference Include="..\TaxVision.Notes.Domain\TaxVision.Notes.Domain.csproj" />
<ProjectReference Include="..\..\..\BuildingBlocks\BuildingBlocks.csproj" />
<ProjectReference Include="..\..\..\BuildingBlocks\Messaging\BuildingBlocks.Messaging.csproj" />
<PackageReference Include="WolverineFx" Version="6.14.0" />
```

### Fragmento real — bloque Wolverine y pipeline (Notes, `Program.cs:119-214`, comentarios recortados)

```csharp
builder.Host.UseWolverine(options =>
{
    options.UseTaxVisionCodeGeneration(builder.Environment, builder.Configuration, typeof(Program).Assembly);
    options.Discovery.IncludeAssembly(typeof(AssemblyMarker).Assembly);
    options.Discovery.IncludeType(typeof(BuildingBlocks.CustomerVisibility.CustomerAssignmentsProjectionConsumer));

    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var sqlConn =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseTaxVisionRabbitMq(rabbitUri);
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, NotesDbContext>();
    options.Policies.AutoApplyTransactions();

    options.UseTaxVisionConflictPolicy();
    options.ApplyStandardFailurePolicies();

    options.UseTaxVisionDurableLocalQueues();
    options.LocalQueueFor<RunTenantCustomerBackfillCommand>().UseDurableInbox().Sequential();

    options.PublishMessage<NoteCreatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<NoteUpdatedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<NoteDeletedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    options.PublishMessage<NoteAttachmentDetachedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options
        .Policies.ForMessagesOfType<IIntegrationEvent>()
        .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

    options
        .ListenToRabbitQueue("notes-events", queue => queue.BindExchange("taxvision-events", string.Empty))
        .UseDurableInbox();
});

builder.Services.AddTaxVisionClientIpForwarding(builder.Configuration);

var app = builder.Build();

app.UseTaxVisionClientIp();
// (Development: MapOpenApi, UseSwagger, UseSwaggerUI)
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
// (no Development: UseHttpsRedirection)
app.UseAuthentication();
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();
app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

app.Run();

public partial class Program;
```

### Fragmento real — eventos de dominio (Customer, `Program.cs:164-168`)

```csharp
options.UseTaxVisionDomainEvents();
options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();

options.UseTaxVisionDurableLocalQueues();
```

```csharp
// src/Services/Customer/TaxVision.Customer.Application/Customers/DomainEvents/CustomerActivatedDomainEventHandler.cs
public static class CustomerActivatedDomainEventHandler
{
    public static CustomerActivatedIntegrationEvent Handle(
        CustomerActivatedDomainEvent activated,
        ICorrelationContext correlation
    ) =>
        new()
        {
            TenantId = activated.TenantId,
            CorrelationId = correlation.CorrelationId,
            OccurredOn = activated.OccurredAtUtc,
            CustomerId = activated.CustomerId,
            ActivatedByUserId = activated.ActivatedByUserId,
            ActivatedAtUtc = activated.OccurredAtUtc,
        };
}
```

### Fragmento real — handler con `Result` (Notes, `CreateNoteCommand.cs:31-91`, extracto)

```csharp
public static class CreateNoteHandler
{
    public static async Task<Result<NoteResponse>> Handle(
        CreateNoteCommand command,
        INoteRepository notes,
        ICustomerDirectoryRepository customerDirectory,
        IHtmlSanitizer sanitizer,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<Note> logger,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        await SoftVerifyCustomerReferenceAsync(command, customerDirectory, logger, ct);

        var referenceResult = NoteReference.Create(command.TargetType, command.TargetId);
        if (referenceResult.IsFailure)
            return Result.Failure<NoteResponse>(referenceResult.Error);

        var contentResult = BuildContent(command, sanitizer);
        if (contentResult.IsFailure)
            return Result.Failure<NoteResponse>(contentResult.Error);

        var colorResult = BuildColor(command);
        if (colorResult.IsFailure)
            return Result.Failure<NoteResponse>(colorResult.Error);

        var noteResult = Note.Create(
            command.TenantId,
            command.AuthorUserId,
            contentResult.Value,
            referenceResult.Value,
            command.Visibility,
            colorResult.Value,
            clock.GetUtcNow().UtcDateTime
        );
        if (noteResult.IsFailure)
            return Result.Failure<NoteResponse>(noteResult.Error);

        var note = noteResult.Value;
        await notes.AddAsync(note, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new NoteCreatedIntegrationEvent
            {
                TenantId = note.TenantId,
                CorrelationId = correlation.CorrelationId,
                NoteId = note.Id,
                AuthorUserId = note.CreatedByUserId,
                TargetType = note.Reference.TargetType.ToString(),
                TargetId = note.Reference.TargetId,
                Visibility = note.Visibility.ToString(),
                CreatedAtUtc = note.CreatedAtUtc,
            }
        );

        return Result.Success(NoteResponse.From(note));
    }
}
```

### Fragmento real — acción de controlador (Notes, `NotesController.cs:90-114`)

```csharp
[HttpPost]
[HasPermission(NotesPermissions.Manage)]
[RateLimit("notes.g.create")]
[ProducesResponseType<NoteResponse>(StatusCodes.Status201Created)]
public async Task<IActionResult> Create(CreateNoteRequest request, CancellationToken ct)
{
    if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
        return Unauthorized();

    var result = await bus.InvokeAsync<Result<NoteResponse>>(
        new CreateNoteCommand(
            tenantId,
            userId,
            request.Html,
            request.TargetType,
            request.TargetId,
            request.Visibility,
            request.ColorKind
        ),
        ct
    );
    return result.IsSuccess
        ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
        : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
}
```

### Fragmento real — prueba de capas (Notes)

```csharp
// deploy/tests/TaxVision.Notes.Tests/Architecture/NotesLayeringTests.cs
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Notes.Application;
using TaxVision.Notes.Domain.Backfill;

namespace TaxVision.Notes.Tests.Architecture;

public sealed class NotesLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(TenantBackfillState).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;
}
```
