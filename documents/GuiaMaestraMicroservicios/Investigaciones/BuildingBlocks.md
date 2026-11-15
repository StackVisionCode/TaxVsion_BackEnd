# Investigación — BuildingBlocks

> Fecha: 2026-11-15. Solo lectura. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\`.
> Etiquetas: **Código verificado** · **Documentación histórica** · **Recomendación** · **Decisión pendiente**.
> Regla del proyecto (`AGENTS.md`): en `BuildingBlocks` solo se agregan piezas **opcionales y aditivas**; no se cambia el comportamiento de lo existente.

## Estado actual

### Cómo está empaquetado (Código verificado)

`src/BuildingBlocks` tiene **10 proyectos** y **14 carpetas que no son proyecto**:

- `BuildingBlocks.csproj` (núcleo, un solo ensamblado `BuildingBlocks.dll`) compila las carpetas **Results, Domain, Persistence, Tenancy, Permissions, ActorTypeAuthorization, Security, Sessions, Caching, TimeZones, RateLimiting, Platform, PurchaseIntentAmount, Common**. Excluye con `<Compile Remove>` las carpetas que son proyecto propio (`src/BuildingBlocks/BuildingBlocks.csproj:8-31`) y referencia `WolverineFx 6.14.0` por `[SagaIdentity]` (`:36-43`).
- Proyectos propios: `Authorization/BuildingBlocks.Authorization.csproj`, `Messaging/BuildingBlocks.Messaging.csproj`, `BuildingBlocks.Infrastructure/…`, `BuildingBlocks.Web/…`, `EfCore/…`, `Jobs/…`, `TenantTimeZones/…`, `CustomerVisibility/…`, `Imaging/…`.
- Motivos documentados en el propio `.csproj`: Messaging y Authorization separados por volatilidad (recompilar los 18 Domain al cambiar un evento); Jobs, TenantTimeZones, CustomerVisibility y EfCore separados para que Domain no dependa de EF; Imaging separado para no arrastrar SkiaSharp.

Dependencias entre proyectos BB (Código verificado, `.csproj`):

```
BuildingBlocks (núcleo) ── WolverineFx
Messaging ─────────────── núcleo, WolverineFx
Authorization ─────────── (sin referencias)
EfCore ────────────────── Microsoft.EntityFrameworkCore 10.0.9
Jobs ──────────────────── núcleo, EF Relational, WolverineFx.EntityFrameworkCore
TenantTimeZones ───────── núcleo, Messaging, EF Relational, Http, Caching.Memory
CustomerVisibility ────── núcleo, Messaging, EF Relational, Http
Imaging ───────────────── núcleo, SkiaSharp 3.119.0 (+ NativeAssets Linux/Win32)
Infrastructure ────────── núcleo, Authorization, Messaging, Redis 2.8.16, Polly 8.5.0, SqlClient 6.1.1, Pkcs 10.0.13
Web ───────────────────── núcleo, Authorization, Infrastructure, Messaging, ASP.NET Core, JwtBearer, OpenTelemetry, Serilog, WolverineFx.RabbitMQ
```

### Matriz de BuildingBlocks

Columna **Uso** = servicios bajo `src/Services` (y Gateway) que usan el namespace (búsqueda de `using`/nombre calificado; aproximado).

| BuildingBlock | Ensamblado | Responsabilidad (Código verificado) | Cuándo utilizar | Configuración | Prueba | Uso |
|---|---|---|---|---|---|---|
| **Results** | núcleo | `Result`, `Result<T>`, `Error(Code, Message)` con `RetryAfterSeconds`; `ConflictException`, `ModuleUnavailableException` (`src/BuildingBlocks/Results/*.cs`) | Siempre: reglas de negocio devuelven `Result`, nunca excepción | Ninguna. HTTP: `ErrorHttpMapping.ToHttpStatusCode()` en Web | `BuildingBlocks.Tests/Results/{ResultTests,ErrorTests,ErrorHttpMappingTests,RetryAfterHeaderFilterTests}.cs` | 26/26 |
| **Domain** | núcleo | `BaseEntity`, `TenantEntity`, `AggregateRoot` (acumula `IDomainEvent`), `TenantlessAggregateRoot`, `ITenantOwned`, `INullableTenantOwned`, `IHasOwner`, `[UserReference]` | Siempre: agregados tenant-owned heredan `TenantEntity`/`AggregateRoot` | Ninguna; mapear `Ignore(x => x.DomainEvents)` si se usa `AggregateRoot` | `Offboarding/UserReferenceContractTests` (BB.Tests) | 26/26 |
| **Persistence** | núcleo | `IUnitOfWork.SaveChangesAsync` | Siempre: el DbContext lo implementa | `services.AddScoped<IUnitOfWork>(p => p.GetRequiredService<XDbContext>())` + `WithDbContextAbstraction<IUnitOfWork, XDbContext>()` | indirecta (handlers) | 26/26 |
| **Tenancy** | núcleo | `ITenantContext` (`TenantId`, `HasTenant`, `SetTenant`), `PlatformTenant` | Siempre: filtro global de tenant del DbContext | Implementación `TenantContext` registrada por `AddBuildingBlocks()` | `BuildingBlocks.Tests/Tenancy/*` | 26/26 |
| **Authorization** | `BuildingBlocks.Authorization` | Catálogos de permisos por servicio (`*Permissions.cs`, 30+ archivos), `PermissionModuleMap`, scopes M2M, `SlotGating` | Siempre que el servicio expone endpoints con `[HasPermission]`: crear `<Svc>Permissions.cs` (aditivo) | Ninguna; el catálogo de verdad vive en Auth (`src/Services/Auth/Domain/Roles/PermissionCatalog.cs`) | `Authorization/PermissionModuleMapTests` | 26/26 |
| **Permissions** | núcleo | Puertos `IUserPermissionsProjectionReader/Writer`, `IPermissionsSnapshotClient` | Servicio con humanos en `[HasPermission]` (modo Projection) | La Infrastructure del servicio implementa el reader (repo doble interfaz, ver Notes) | `ActorTypeAuthorization/PermissionsSourceTests` | 26/26 |
| **ActorTypeAuthorization** | núcleo | `ActorType`, `AccessSurface`, `ClaimNames`, `ClaimsPrincipalExtensions`, `MfaEnrollmentToken` | Siempre (identidad del JWT) | Filtros/atributos viven en Web (`[AllowActorTypes]`, `[HasPermission]`, `[AllowSurface]`) | `Authorization/ClaimsPrincipalExtensionsTests`, `ActorTypeAuthorization/*` | 26/26 + Gateway |
| **Security** | núcleo (+ impl. en Infrastructure) | `ISecretProtector`, `IRotatingSecretProtector`, `ExpiringValueCache`, `SensitiveNotificationContent` | Cifrar secretos en BD (claves, tokens OAuth); cachear tokens M2M | `AddSecretProtection`, `AddRotatingSecretProtection`, `AddProtectedPayloadProtection` (Infrastructure) | `Security/{AesGcmSecretProtectorTests,ExpiringValueCacheTests,JsonProtectedPayloadCodecTests,SecretProtectionRegistrationTests}` | ExpiringValueCache: 26; protectores: Auth, CloudStorage, Connectors, Notification, PaymentApp, PaymentClient, Postmaster, Signature |
| **Sessions** | núcleo (+ reader en Infrastructure, middleware en Web) | `ISessionDenylistReader`, `SessionDenylistUnavailableException` | Siempre (revocación inmediata de sesiones) | `AddSessionDenylist(config)` + `app.UseMiddleware<SessionDenylistMiddleware>()` entre `JwtTenantContextMiddleware` y `UseAuthorization` | `Session/{SessionDenylistMiddlewareTests,SessionDenylistReaderTests}` | middleware en 26 (namespace directo solo Auth) |
| **Messaging** | `BuildingBlocks.Messaging` | Contratos `IIntegrationEvent`/`IntegrationEvent` (`EventId`, `TenantId`, `OccurredOn`, `CorrelationId`), 28 carpetas `*IntegrationEvents` (≈400 `record`; BB-4 contó 321 contratos el 2026-11-05), `WolverineFailurePolicies.ApplyStandardFailurePolicies`, `WolverineConflictPolicy.UseTaxVisionConflictPolicy`, `DependencyNotReadyException`, handlers compartidos de RateLimiting | Siempre: todo evento entre servicios vive aquí, con `[MessageIdentity]` | `options.ApplyStandardFailurePolicies()`, `options.UseTaxVisionConflictPolicy()` (antes que la anterior) | `Messaging/{MessageIdentityTests,FailurePolicyContractTests,WolverineConflictPolicyTests,EventRoutingContractTests,HandlerNamingContractTests,NodeInteropContractTests}` | 26/26 |
| **Caching** | núcleo (+ `RedisCacheService` en Infrastructure) | `ICacheService` | Caché distribuida (planes, permisos, módulos) | `AddRedisCache(config)` (`ConnectionStrings:Redis`) | `Caching/RedisCacheServiceTests` | 26/26 |
| **TimeZones** | núcleo | `IanaTimeZone`, `CivilDate`, `LocalDay`, `WallTime`, `ResolvedTimeZone`, `MonthlyUsagePeriod`, `ZonedTimeFormatter` | Cualquier cálculo de fecha civil o zona (nada de aritmética de offsets a mano) | Ninguna; arranque con `AddTimeZoneSelfCheck()` (Web) | `TimeZones/{CivilTimeTests,TemporalVectorsTests,MonthlyUsagePeriodTests,ZonedTimeFormatterTests,TimeZoneSelfCheckTests}` | 19 |
| **TenantTimeZones** | `BuildingBlocks.TenantTimeZones` | Proyección local de la zona de cada oficina (`TenantTimeZoneProjection`), consumer compartido, resolver, reconciliación contra Tenant | Servicio que calcula vencimientos o formatea fechas en la zona de la oficina | `AddTenantTimeZoneProjection<TDbContext>()`, `AddTenantTimeZoneReconciliation<TTokenProvider>(config)`, `modelBuilder.ApplyTenantTimeZoneProjection()`, `Discovery.IncludeType(typeof(TenantTimeZoneProjectionConsumer))`, migración propia | `TenantTimeZones/*` (5 archivos) | 12 |
| **CustomerVisibility** | `BuildingBlocks.CustomerVisibility` | Proyección `CustomerAssignmentProjection` (asignaciones cliente↔staff), consumer compartido, reconciliación pull contra Customer | Servicio que filtra datos por cliente asignado | `AddCustomerVisibilityProjection<TDbContext>()`, `AddCustomerVisibilityReconciliation<TTokenProvider>(config)`, `modelBuilder.ApplyCustomerAssignmentProjection()`, `Discovery.IncludeType(typeof(CustomerAssignmentsProjectionConsumer))`, flag del servicio (OFF por defecto) | En servicios: `Billing.Tests/Customers/CustomerAssignmentsProjectionConsumerTests.cs`, CloudStorage `CustomerAccess/*` | 11 |
| **EfCore** | `BuildingBlocks.EfCore` | `UseUtcDateTimes()` (todo `DateTime` se lee como UTC), `IsCivilDateTime()`, `DateTimesNotReadAsUtc(model)` | Siempre (al final de `OnModelCreating`) | `modelBuilder.UseUtcDateTimes();` | `EfCore/UtcDateTimeConventionsTests`; en cada servicio `UtcDateTimesTests` | 23 (faltan Calendar, Signature, Tasks: conversor propio) |
| **Jobs** | `BuildingBlocks.Jobs` | `TransactionalJobUnit.RunAsync/RunEachAsync` — una unidad por ítem con transacción + outbox (`IDbContextOutbox`) | Todo job/hosted service que escribe y publica | Referencia al proyecto; `PersistMessagesWithSqlServer` + `UseEntityFrameworkCoreTransactions`; sin `EnableRetryOnFailure` | `Integration/TransactionalJobUnitIntegrationTests` (BB), `Architecture/JobPublishingTests` (lista `PendingAdoption`) | 12 |
| **RateLimiting** | núcleo (+ Infrastructure, Web) | Catálogo `RateLimitPolicyCatalog.<Svc>.cs` (partial, uno por servicio), `RateLimitPolicyDefinition`, categorías, particiones, resolver de cuotas por plan | Siempre que el servicio expone endpoints | `AddTaxVisionRedis`, `IRateCounter → RedisRateCounter`, lectores de plan bajo `RateLimit:EnforceTierQuotas`, `AddTieredRateLimiting()`; `[RateLimit("<svc>.<cat>.<accion>")]` / `[RateLimitExempt]` | `RateLimit/*` (16 archivos) | 26/26 |
| **Platform** | núcleo (+ `PlatformSettings*` en Web) | `PlatformSettingsSnapshot` (dominios, orígenes web), `OAuthReturnRedirectPolicy` | Servicio que arma URLs públicas, valida orígenes o redirecciones OAuth | `AddPlatformSettings(config)` / `ListenForPlatformSettings` (Web) | `Platform/{OAuthReturnRedirectPolicyTests,PlatformHostRulesTests,PlatformSettingsProviderTests}` | 11 + Gateway |
| **PurchaseIntentAmount** | núcleo | Firma/verificación HMAC del monto de una intención de compra (`HmacPurchaseIntentAmountSigner/Verifier`, `FixedTimeEquals`) | Solo flujos de cobro de la plataforma donde un servicio fija el monto y otro lo cobra | `AddPurchaseIntentAmountSigning` / `AddPurchaseIntentAmountVerification` + opciones de firma (secreto compartido, sin valor aquí) | `PaymentApp.Tests/PurchaseIntentAmount/HmacPurchaseIntentAmountSignerAndVerifierTests.cs` | 3 (PaymentApp, Subscription, Wallet) |
| **Common** | núcleo (+ `CorrelationContext` en Web) | `ICorrelationContext` (`CorrelationId`, `Push`), `PagedResult<T>`, `ReconciliationRetry` | Siempre (correlación en handlers y consumers; paginación) | `AddBuildingBlocks()` registra `CorrelationContext`; `CorrelationIdMiddleware` | `Common/ReconciliationRetryTests`, `Architecture/CorrelationIdFitnessTests` | 26/26 |
| **Web** | `BuildingBlocks.Web` | Hosting (`UseTaxVisionSerilog`, `UseTaxVisionRabbitMq`, `UseTaxVisionCodeGeneration`, `UseTaxVisionDomainEvents`, `UseTaxVisionDurableLocalQueues`, `AddTimeZoneSelfCheck`, client IP), seguridad (`AddTaxVisionJwtAuthentication`, `[RequireRecentAuthentication]`), autorización (`AddActorTypeAuthorization`, `PermissionPolicyProvider`, `AddUserPermissionsSource`, `AddModuleGate`, `AddOwnershipAuthorization<T>`), tenancy (`JwtTenantContextMiddleware`, `IntegrationEventTenantMiddleware`, `LocalCommandTenantMiddleware`), middleware (`CorrelationIdMiddleware`, `ExceptionHandlingMiddleware`), resultados (`ErrorHttpMapping`), rate limit (`[RateLimit]`), sesión, Turnstile, webhooks (`SharedSecretWebhookVerifier`), health (`TcpEndpointHealthCheck`), OpenTelemetry | Siempre (todo Api) | Ver `Investigaciones/Arquitectura.md` (orden en `Program.cs`) | Muchas carpetas de `BuildingBlocks.Tests` (ActorTypeAuthorization, Tenancy, Session, Middleware, Observability, Webhooks, BotProtection…) | 26/26 + Gateway |
| **Infrastructure** | `BuildingBlocks.Infrastructure` | Redis (`AddTaxVisionRedis`), caché, contadores de rate limit, lectores de plan/módulos cacheados (`AddCachedTenantEntitlementModulesReader<T>`), resiliencia HTTP (`HttpResiliencePipelineRegistry`), protectores de secretos, adquisición de tokens M2M (`ServiceTokenHttpAcquisition`), lector de denylist, `DeferredStartupHostedService`, gate de asiento SQL (`AddSqlSeatSlotGate`) | Siempre (vía Web) | Según pieza | `Resilience/*`, `RateLimit/*`, `Security/*`, `Integration/RedisOutageIntegrationTests` | 26/26 + Gateway |
| **Imaging** | `BuildingBlocks.Imaging` | Normalizador de avatares (`IAvatarImageNormalizer`, límite de concurrencia, detección de animación) | Solo si el servicio guarda fotos de perfil | `AddAvatarImageNormalizer(...)` | `Imaging/*` (4 archivos) | 2 (Auth, Customer) |

### Piezas de pruebas reutilizables (`deploy/tests/TaxVision.BuildingBlocks.Testing`)

**Código verificado:** biblioteca sin runner (`Aliases="BbTesting"`): `Architecture/{LayeringRulesBase, PreGeneratedCodeRules, SaveBeforePublishRules, JobPublishingRulesBase, ConstructorBusRules, IntegrationIsolationRules}`, `Messaging/{LocalQueueDurabilityRules, DomainEventHandlerConventionRules, DomainEventEnvelopeRoundTrip, HandlerOutboxProbes}`, `Integration/{TemporaryTestDatabase, MigrationTestDatabase, TemporaryDatabaseTestFramework}`. `HandlerOutboxProbes` no tiene usos fuera de la biblioteca.

## Evidencia

- `src/BuildingBlocks/BuildingBlocks.csproj:1-45`; `.csproj` de cada BB.
- Lista de extensiones públicas obtenida por búsqueda de `public static … Add*/Use*/Apply*` en `src/BuildingBlocks`.
- `DEVS/Guia_BuildingBlocks_Ola2.md` (BB-1, BB-2, BB-3, BB-4, BB-6, BB-7, BB-11, BB-14…).

## Problemas

1. **Documentación histórica superada:** `DEVS/Guia_BuildingBlocks_Ola2.md:73` dice de `BuildingBlocks.Jobs` "Ningún servicio lo referencia todavía" (2026-11-05); hoy lo usan 12. `DomainDrivenDesign/00-Rubrica.md` dice "ningún `Program.cs` activa `UseDurableLocalQueues`"; hoy 19/26 usan `UseTaxVisionDurableLocalQueues`.
2. **Documentación histórica:** `DEVS/Guia_Creacion_Microservicio.md:64` describe `BuildingBlocks.Infrastructure` como "(Redis, HTTP readers, OTel)": OTel vive en `BuildingBlocks.Web/Observability`.
3. El comentario de `src/BuildingBlocks/Tenancy/ITenantContext.cs:3-8` (sin tenant "degradar a sin filtro") contradice el filtro fail-closed de los DbContext.
4. Archivo `BuildingBlocks.Web/Tenancy/ITenantContext.cs` contiene la clase `TenantContext` (nombre de archivo ≠ tipo).
5. `ErrorHttpMapping` es una lista central creciente (518 líneas): cada servicio nuevo debe editarla.
6. `UseUtcDateTimes` no se aplica en Calendar, Signature y Tasks (conversor local equivalente, sin la prueba común).

## Buenas prácticas

- Cada pieza BB nueva es opcional: "solo cambia el servicio que la llama" (patrón explícito en `WolverineCodeGenerationExtensions`, `DurableLocalQueueExtensions`, `WolverineConflictPolicy`, `DomainEventDispatchExtensions`).
- Kits con proyección (TenantTimeZones, CustomerVisibility) siguen el mismo contrato: `Add…Projection<TDbContext>()` + `Add…Reconciliation<TTokenProvider>()` + `modelBuilder.Apply…Projection()` + `Discovery.IncludeType(consumer)` + migración del servicio.
- Orden de políticas de fallo: `UseTaxVisionConflictPolicy()` **antes** de `ApplyStandardFailurePolicies()` (gana la primera regla que coincide, `WolverineConflictPolicy.cs:4-8`).

## Recomendación — cómo elegir los BuildingBlocks de un servicio nuevo

| Necesidad del servicio | BuildingBlocks a referenciar |
|---|---|
| **Siempre** (cualquier servicio .NET con API) | núcleo `BuildingBlocks`, `Messaging`, `Authorization`, `Web`, `Infrastructure`, `EfCore` |
| Escribe y publica desde un job/hosted service | + `Jobs` |
| Calcula fechas en la zona de la oficina (vencimientos, recordatorios, avisos) | + `TenantTimeZones` (y `TimeZones` del núcleo) |
| Muestra datos por cliente y debe respetar la asignación de preparador | + `CustomerVisibility` |
| Guarda fotos de perfil | + `Imaging` |
| Fija montos que cobra PaymentApp | `PurchaseIntentAmount` (núcleo) con `AddPurchaseIntentAmountSigning` |
| Cifra secretos en su base | `AddSecretProtection`/`AddRotatingSecretProtection` (Infrastructure) |
| Emite eventos de dominio | `UseTaxVisionDomainEvents()` (Web) + `AggregateRoot` (núcleo) |

**Decisión pendiente:** si `UseTaxVisionDomainEvents()` y `UseTaxVisionDurableLocalQueues()` pasan de "opcionales" a "obligatorios para servicios nuevos" en la guía (hoy 17 y 19 de 26).

## Riesgos

- Agregar una pieza BB que cambie comportamiento por defecto rompe la regla "solo aditivo" y afecta a 26 servicios a la vez.
- Mover un contrato de carpeta sin conservar `[MessageIdentity]` cambia el nombre en el cable (mensajes en colas/outbox/DLQ quedan sin handler; Node deja de reconocerlos) — `DomainDrivenDesign/00-Rubrica.md`, último hecho técnico.
- `TenantTimeZones` o `CustomerVisibility` sin su migración en el servicio → la tabla de proyección no existe y el consumer falla.

## Pruebas

- Por pieza: las listadas en la matriz (en `deploy/tests/TaxVision.BuildingBlocks.Tests`).
- En el servicio: heredar `LayeringRulesBase`; `UtcDateTimesTests` (usa `UtcDateTimeConventions.DateTimesNotReadAsUtc(model)`); `LocalQueueDurabilityRules`; `DomainEventHandlerConventionRules` si emite eventos de dominio; `JobPublishingRulesBase` si tiene jobs.
- Al tocar un constructor de handler usado por pruebas de integración: correr `--filter "FullyQualifiedName~.Integration.|FullyQualifiedName~.Persistence."` del servicio (`memory/feedback_dependencia_nueva_correr_integration_del_servicio.md`).

## Dependencias

`Investigaciones/Arquitectura.md` (dónde se llama cada extensión), `Investigaciones/Migraciones.md` (EfCore, kits con proyección), `Referencias/Guardrails.md`.

## Material para la guía

### Paso a paso

1. Referenciar los 6 BB obligatorios (tabla de recomendación).
2. En `Program.cs`: `AddBuildingBlocks()`, `AddTimeZoneSelfCheck()`, `AddRedisCache`, `AddSessionDenylist`, `AddTaxVisionJwtAuthentication`, `AddTaxVisionOpenTelemetry`, `PermissionPolicyProvider`, `AddModuleGate` (si sus permisos mapean a un módulo), `AddUserPermissionsSource`, rate limit tiered.
3. En `UseWolverine`: `UseTaxVisionCodeGeneration`, `UseTaxVisionRabbitMq`, `UseTaxVisionConflictPolicy`, `ApplyStandardFailurePolicies`, `UseTaxVisionDurableLocalQueues`, middlewares de tenant.
4. En `OnModelCreating`: `UseUtcDateTimes()` al final (y `Apply…Projection()` de cada kit).
5. Crear `<Svc>Permissions.cs` en `BuildingBlocks/Authorization`, `RateLimitPolicyCatalog.<Svc>.cs` en `BuildingBlocks/RateLimiting/Catalog`, contratos en `BuildingBlocks/Messaging/<Svc>IntegrationEvents` con `[MessageIdentity("<svc>.<entidad>.<evento>.v1")]` y, si hace falta, códigos en `ErrorHttpMapping` (todo aditivo).

### Fragmento real — kit TenantTimeZones en un servicio (Customer)

```csharp
// src/Services/Customer/TaxVision.Customer.Infrastructure/DependencyInjection.cs:174-175
services.AddTenantTimeZoneProjection<CustomerDbContext>();
services.AddTenantTimeZoneReconciliation<Reconciliation.CustomerTenantTimeZoneTokenProvider>(config);

// src/Services/Customer/TaxVision.Customer.Infrastructure/Persistence/CustomerDbContext.cs:99,106
modelBuilder.ApplyTenantTimeZoneProjection();
// …
modelBuilder.UseUtcDateTimes();

// src/Services/Customer/TaxVision.Customer.Api/Program.cs (UseWolverine)
options.Discovery.IncludeType(typeof(BuildingBlocks.TenantTimeZones.TenantTimeZoneProjectionConsumer));
```

### Fragmento real — kit CustomerVisibility (Notes)

```csharp
// src/Services/Notes/TaxVision.Notes.Infrastructure/DependencyInjection.cs:57-61
services.AddCustomerVisibilityProjection<NotesDbContext>();
services.AddCustomerVisibilityReconciliation<Reconciliation.NotesPlatformTokenProvider>(configuration);
services
    .AddOptions<Application.Notes.NotesVisibilityOptions>()
    .Bind(configuration.GetSection(Application.Notes.NotesVisibilityOptions.SectionName));

// src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/NotesDbContext.cs:48-53
modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
modelBuilder.ApplyCustomerAssignmentProjection();
ApplyFailClosedTenantFilter(modelBuilder);
base.OnModelCreating(modelBuilder);
modelBuilder.UseUtcDateTimes();
```

### Fragmento real — unidad transaccional de job (API documentada)

```csharp
// DEVS/Guia_BuildingBlocks_Ola2.md:90-95 (API de src/BuildingBlocks/Jobs/TransactionalJobUnit.cs)
JobUnitResult r = await TransactionalJobUnit.RunAsync<TasksDbContext>(scopeFactory, async (unit, ct) => { ... return true; }, logger, ct);
JobBatchSummary s = await TransactionalJobUnit.RunEachAsync<Guid, TasksDbContext>(scopeFactory, ids, async (id, unit, ct) => { ... }, logger, ct);
```

### Fragmento real — `AddBuildingBlocks()`

```csharp
// src/BuildingBlocks/BuildingBlocks.Web/Common/DependencyInjection.cs
public static IServiceCollection AddBuildingBlocks(this IServiceCollection services)
{
    services.AddScoped<TenantContext>();
    services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

    services.AddScoped<CorrelationContext>();
    services.AddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

    services.Configure<MvcOptions>(options => options.Filters.Add<RetryAfterHeaderFilter>());
    services.AddOptions<SlotGatingOptions>().BindConfiguration(SlotGatingOptions.SectionName);

    return services;
}
```
