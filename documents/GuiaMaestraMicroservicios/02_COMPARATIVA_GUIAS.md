# 02 — Comparativa de guías antiguas contra el código actual

> Fecha de corte: 2026-11-15. Solo lectura. Código en `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\` (rutas relativas a esa raíz). Guías en `C:\Users\devcacg\Desktop\Implementaciones\` (`DEVS/`, `DomainDrivenDesign/`, `Reminder/`, `Signature/`) o en `documents/`/`docs/` del backend.
> **Estados:** Vigente · Parcialmente vigente · Obsoleta · Incorrecta · Insegura · Reemplazada · Pendiente.
> **Tratamiento:** qué hace la guía maestra con la instrucción (Adoptar · Adoptar con corrección · Reemplazar por … · Descartar · Marcar como histórica · Decisión pendiente).

## Abreviaturas de documentos

| Código | Documento |
|---|---|
| GCM | `DEVS/Guia_Creacion_Microservicio.md` (1338 líneas, fecha de archivo 2026-11-03) |
| BB2 | `DEVS/Guia_BuildingBlocks_Ola2.md` |
| EXT | `DEVS/Guia_Envio_Externo_Fuera_De_Transaccion.md` (BB-8) |
| GCS | `documents/Guia_Conectar_Microservicio_a_CloudStorage.md` |
| GNE | `documents/Guia_Conectar_Microservicio_a_Notificaciones_Email.md` |
| IQF | `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md` |
| RBAC | `documents/RBAC_Endurecimiento_Defensa_Senior.md` |
| CMP | `documents/architecture/Guia_Cablear_Modulo_Plan_Microservicio.md` |
| AUD | `docs/audit/*.md` (corte 2026-08-07) |
| RUB | `DomainDrivenDesign/00-Rubrica.md` · CONV `DomainDrivenDesign/01-Convenciones-Pruebas.md` |
| LIB | `DomainDrivenDesign/Guia-DDD-EDA-NET10-Wolverine-RabbitMQ-Docker.pdf` |
| G36 | `Signature/AUDITORIA_SIGNATURE_FASE0.md` Apéndice A · REM `Reminder/04_Guardrails_Checklist_Y_Verificacion.md` |
| MEM | memoria del proyecto (`memory/*.md`) |

## Matriz

| # | Documento | Instrucción | Evidencia actual | Estado | Tratamiento |
|---|---|---|---|---|---|
| 1 | GCM §1 (:32) | "~20+ microservicios .NET + 2 servicios Node" | 26 servicios .NET con base + 2 workers .NET + **3** Node (`src/Services/*/package.json`) | Obsoleta | Reemplazar por el inventario de `03_INVENTARIO_ARQUITECTURA.md` |
| 2 | GCM §1 (:36-43) | Camino del request Cloudflare → túnel → Caddy → YARP (:5047→8080) → `<svc>-api:8080` (expose, nunca ports) | `gateway` publica `127.0.0.1:5047:8080`; servicios con `expose: "8080"` (`deploy/docker/docker-compose.yml`) | Vigente | Adoptar |
| 3 | GCM §1 (:49) | Un único exchange fanout `taxvision-events`; cola propia `<servicio>-events` | Todos los `Program.cs` publican a `taxvision-events`; colas `<svc>-events`; además colas punto a punto de comandos (`customer-client-form-commands`, `cloudstorage-file-deletions`) | Parcialmente vigente | Adoptar con corrección: "eventos al fanout; comandos punto a punto con `ToRabbitQueue` cuando el destino es uno" |
| 4 | GCM §1 (:60) | Observabilidad "OTel Collector + Prometheus/Tempo/Loki/Grafana" | Collector siempre; Prometheus/Grafana/cadvisor/node-exporter en perfil `observability` apagado; Loki/Tempo comentados (`docker-compose.yml:188-218`) | Parcialmente vigente | Adoptar con corrección |
| 5 | GCM §1 (:64) | `BuildingBlocks.Infrastructure` = "Redis, HTTP readers, OTel" | OTel está en `BuildingBlocks.Web/Observability/OpenTelemetryRegistration.cs` | Incorrecta (detalle) | Reemplazar por `Investigaciones/BuildingBlocks.md` |
| 6 | GCM §2.1 (:80-96) | 4 proyectos por servicio; Domain→BB; Application→Domain+BB+Messaging+WolverineFx; Infra→App+BB.Infrastructure+EF; Api→Infra+Web+Authorization+Messaging; solo Api `Sdk.Web` | Coincide con `src/Services/Notes/*/*.csproj`; Infra además referencia `BuildingBlocks.EfCore` y kits opcionales | Vigente | Adoptar (añadir EfCore y kits) |
| 7 | GCM §2.1 (:98-127) | Carpetas por concepto de negocio con `Abstractions/Commands/Queries/Consumers` | Árbol real de Notes (`Investigaciones/Arquitectura.md`) | Vigente | Adoptar |
| 8 | GCM §2.2 (:131-151) | Agregado `sealed`, ctor privado, backing fields, setters privados, `Create()`→`Result<T>`, transición explícita, sin LINQ-a-datos | `src/Services/Notes/TaxVision.Notes.Domain/Notes/Note.cs:27-51` | Vigente | Adoptar |
| 9 | GCM §2.2 (:155-160) | `BaseEntity { Id = Guid.NewGuid() }` y `TenantEntity` | Tipos en `src/BuildingBlocks/Domain/` | Vigente | Adoptar + recordar `ValueGeneratedNever` para hijas (guardrail #10) |
| 10 | GCM §2.2 (:184) | Domain events in-process, `AddDomainEvent`, nunca cruzan el servicio | `AggregateRoot` (`src/BuildingBlocks/Domain/AggregateRoot.cs`); despacho con `UseTaxVisionDomainEvents()` en 17 servicios | Parcialmente vigente | Adoptar con corrección: añadir el despacho BB-1 (no estaba en la guía) |
| 11 | GCM §2.3 (:188-226) | Handler `static`, dependencias por parámetro, `CancellationToken` último, devuelve `Result<T>`, patrón load→validate→persist→publish | `CreateNoteCommand.cs:31-91` | Vigente | Adoptar |
| 12 | GCM §2.4 (:230-244) | DbContext `sealed : DbContext, IUnitOfWork` con `ApplyConfigurationsFromAssembly` + filtro fail-closed | `NotesDbContext.cs:29-86`; falta en la guía `UseUtcDateTimes()` y la traducción a `ConflictException` | Parcialmente vigente | Adoptar con corrección (`Investigaciones/Migraciones.md`) |
| 13 | GCM §2.4 (:246) | `IDesignTimeDbContextFactory` con `ITenantContext` dummy | 26 factories (`NotesDbContextFactory.cs`) | Vigente | Adoptar |
| 14 | GCM §2.4 (:262-264) | Repositorio con `IgnoreQueryFilters()` + `TenantId` explícito | `NoteRepository.cs:11-16` | Vigente | Adoptar |
| 15 | GCM §2.5 (:278-291) | Controlador fino: `this.TryGetTenantAndUser`, `bus.InvokeAsync<Result<T>>`, `ToHttpStatusCode()` | `NotesController.cs:90-114` | Vigente | Adoptar |
| 16 | GCM §2.5 (:293-311) | `Program.cs` "orden real de Notes" (Serilog, BB, Infra, JWT, OTel, health, Wolverine mínimo) | El `Program.cs` actual añade codegen Static, `AddTimeZoneSelfCheck`, `UseTaxVisionRabbitMq`, políticas de fallo/conflicto, colas durables, client IP, gate de módulo (`src/Services/Notes/TaxVision.Notes.Api/Program.cs:40-214`) | Parcialmente vigente | Reemplazar por el `Program.cs` completo de `Investigaciones/Arquitectura.md` |
| 17 | GCM §2.6 (:319-321) | SRP/DIP/ISP; un `Handle` >30 líneas se parte | `HandlerSizeFitnessTests` (PaymentApp, Auth.Onboarding) | Vigente | Adoptar |
| 18 | GCM §2.7 (:325-337) | Alta en `.slnx` con `<Folder>`; test project con xunit, InMemory, NetArchTest, Mvc.Testing, `FrameworkReference` | Coincide (`TaxVision.slnx`; `deploy/tests/TaxVision.Notes.Tests/TaxVision.Notes.Tests.csproj`) + referencia a `TaxVision.BuildingBlocks.Testing` con `Aliases="BbTesting"` | Parcialmente vigente | Adoptar con corrección (añadir BbTesting y `System.IdentityModel.Tokens.Jwt`) |
| 19 | GCM §2.7 (:339) y §12 #32 | "La suite completa solo pasa verde en serie (`-m:1`)" | El gate excluye `.Integration.`/`.Persistence.`; las T-INT usan bases temporales propias (`TemporaryTestDatabase`, BB-10) | Parcialmente vigente | Marcar como histórica; regla vigente = filtro del CI + namespaces `.Integration.` |
| 20 | GCM §3.1 (:380) | `options.UseRabbitMq(rabbitUri).AutoProvision()` | Todos usan `UseTaxVisionRabbitMq(uri)` (añade confirmaciones de publicación, `RabbitMqTransportExtensions.cs:33-45`) | Reemplazada | Reemplazar por `UseTaxVisionRabbitMq` |
| 21 | GCM §3.1 (:386) | `ApplyStandardFailurePolicies()` | 26/26 (`WolverineFailurePolicies.cs`) | Vigente | Adoptar + `UseTaxVisionConflictPolicy()` antes |
| 22 | GCM §3.1 (:395-406), §3.6 | Un `PublishMessage<T>().ToRabbitExchange("taxvision-events")` por tipo; cola bindeada; `Discovery.IncludeAssembly` | `Notes/Program.cs:122,153-171` | Vigente | Adoptar (guardrails #13-#14) |
| 23 | GCM §3.2 (:457-461) | Alias `.v1` **en el docstring** del contrato | BB-4: `[MessageIdentity]` obligatorio; contratos nuevos con alias `servicio.entidad.evento.v1`; viejos con nombre CLR (`BB2:5-27`; `NotesIntegrationEvents.cs`) | Reemplazada | Reemplazar por la regla BB-4 |
| 24 | GCM §3.3 (:465-481) | Publisher de Tasks: `PublishAsync` **antes** de `SaveChangesAsync` ("se confirman en la misma transacción") | BB-14 / memoria: guardar **antes** de publicar (reintento en línea filtra lo publicado); fitness `SaveBeforePublishTests` | Incorrecta | Reemplazar por "guardar antes de publicar" (`CreateNoteCommand.cs:73-88`) |
| 25 | GCM §3.4 (:498-534) | Consumer estático, `correlation.Push` primero, `IgnoreQueryFilters` + guarda de tenant, idempotencia en dos capas | `NotesFileScanResultConsumer.cs:21-56` | Vigente | Adoptar |
| 26 | GCM §3.5 (:536-559) | Node mapea tipo CLR → alias con `CLR_TYPE_TO_EVENT_TYPE` | BB-4: eventos con alias ya llegan con el alias en `type`; solo los legacy con nombre CLR necesitan la tabla (`BB2:21`) | Parcialmente vigente | Adoptar con corrección |
| 27 | GCM §4.1-4.8 | RBAC de 5 capas, `PermissionCatalog`, `[AllowActorTypes]`+`[HasPermission]`, modo Projection, orden de middleware, sync de Auth, ABAC, denylist, fail-fast | Coincide (`Notes/Program.cs:61-82,197-205`; `ProjectionPermissionsSource.cs`) | Vigente | Adoptar |
| 28 | GCM §4.5 (:679), checklist (:700) | `AddPermissionsPullRecovery` "solo en el servicio que lo necesita"; Notes único | 16 servicios registran `IPermissionsSnapshotClient` (búsqueda en `DependencyInjection.cs`); el comentario de Notes "Registrado SOLO en Notes" (`Notes/…/DependencyInjection.cs:108-113`) quedó viejo | Parcialmente vigente | Adoptar como práctica por defecto; **Decisión pendiente**: hacerlo obligatorio |
| 29 | GCM §5 / IQF §4.2-§7 | `IgnoreQueryFilters()` solo con tenant explícito o una de 4 condiciones seguras | `NoteRepository.cs`; IQF `:143-201` | Vigente | Adoptar |
| 30 | IQF §7.3 (:303-315) | "Estilo de comentario obligatorio": citar el bug, listar callers, referenciar otro archivo | Choca con guardrail #36 y con `AGENTS.md` (comentarios cortos, sin historia) | Incorrecta (para la regla vigente) | Reemplazar por: "comentario breve que diga cuál de las 4 condiciones aplica" |
| 31 | IQF encabezado (:3-6) | Alcance "7 microservicios", "1963/1963 tests" | Hoy 26 servicios | Obsoleta (cifras) | Marcar como histórica |
| 32 | GCM §6.1 (:776-788) | Redis con `AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(...))` | `builder.Services.AddTaxVisionRedis(...)` (`Notes/Program.cs:87-90`) | Reemplazada | Reemplazar por `AddTaxVisionRedis` |
| 33 | GCM §6.1-6.5 | `IRateCounter`, lectores tier bajo `RateLimit:EnforceTierQuotas`, `AddTieredRateLimiting()`, `[RateLimit("svc.cat.accion")]`, catálogo partial, partición Token explícita | `Notes/Program.cs:91-107`; `src/BuildingBlocks/RateLimiting/Catalog/RateLimitPolicyCatalog.*.cs` | Vigente | Adoptar |
| 34 | GCM §6.6 (:904) y CMP (:332,344) | Subir el contador `The_thirteen_gated_services_are_still_wired` | La prueba se llama `The_fourteen_gated_services_are_still_wired` (`ModuleGateWiringFitnessTests.cs:109`); 15 `Program.cs` con `AddModuleGate` | Obsoleta (número) | Adoptar el paso, sin número fijo; **Decisión pendiente**: aclarar 14 vs 15 |
| 35 | GCM §6.6 (:877-885) | El gate cuelga de `[HasPermission]`; `null ≠ []`; escalón por módulo con lista literal en compose | `docker-compose.yml` (`notes-api`: `Authorization__ModuleGate__EnforcedModules__0: planner`) | Vigente | Adoptar |
| 36 | GCM §7 (:916-920) | Enums de CloudStorage (`OwnerType`/`FolderType` sin `SaaSPayment`, `FormParticipant`, `VoiceNotes`, `SaaSReceipts`, `ClientForms`, `RecordingTracks`) "corrigiendo la guía vieja" | `FileEnums.cs` tiene esos valores; **GCS** (`:593-608`) sí está al día | Obsoleta (GCM) / Vigente (GCS) | Reemplazar por GCS §5 |
| 37 | GCM §7 Formas 1-4 / GCS Casos A-B | Subida mediada (MinIO scoped + `SaveFileRequested`), attach-by-fileId + consumer, M2M `scan-status` | Coincide con Notes/Tasks/Customer (`Customer/Program.cs` publica `SaveFileRequestedIntegrationEvent`) | Vigente | Adoptar (remitir a GCS) |
| 38 | GCM §8 / GNE | Notificación: tu servicio solo publica; consumer en Notification con `EnsureRendered`; plantilla Scribe con `event_key` idéntico a `[MessageIdentity]` | 54 usos de `EnsureRendered` en Notification | Vigente | Adoptar (remitir a GNE) |
| 39 | GCM §8 (:1015), GNE (:324) | Política global `RetryWithCooldown(1s, 5s, 15s)` | `ApplyStandardFailurePolicies`: excepciones permanentes (JSON, argumento, formato, no soportado) van directo a la DLQ; el resto 1/5/15 s (`RUB` hechos técnicos; `WolverineFailurePolicies.cs`) | Parcialmente vigente | Adoptar con corrección |
| 40 | GCM §9.1 (:1064-1081) | Job único `deploy`, environment `production`, gate de tests .NET y Node en el CI, Node 22 | Dos jobs (`stage`/`deploy`), environments `production` y `production-extra`; **sin gate de pruebas desde 2026-11-13** (`MEM project_ci_sin_gate_de_pruebas.md`); Node 24.22.2 | Obsoleta | Reemplazar: gate exacto **en local** (`CONV`), CI solo despliega |
| 41 | GCM §9.1 pasos 9-16 | Build en tandas de 3 con `--profile tools`, reconciliar RabbitMQ, provisionar MinIO, migraciones EF y Prisma, `up -d`, limpieza, healthcheck | `.github/workflows/deploy.yml:668-800` | Vigente | Adoptar |
| 42 | GCM §9.2 (:1118) | "Ningún servicio declara `restart`, por eso `db-ready` es crítico" | 43 líneas `restart:` (p. ej. `restart: unless-stopped` en `notes-api`) | Obsoleta | Reemplazar |
| 43 | GCM §9.2 (:1087-1116) | Anatomía de servicio en compose (image, build, expose, env Jwt/Rabbit/Redis/OTLP, volúmenes, depends_on) | Coincide; faltan en el ejemplo `TZ: Etc/UTC`, `restart`, `ServiceAuthClient__*`, `Authorization__ModuleGate__*` | Parcialmente vigente | Adoptar con corrección (copiar el bloque real de `notes-api`) |
| 44 | GCM §9.3 (:1122,1134) | `apply-migrations.sh` migra 19 servicios; 4 pasos para agregar uno | 25 + FinancialInsights condicional; los 4 pasos siguen siendo los correctos | Parcialmente vigente | Adoptar con corrección |
| 45 | `docker-compose.yml:160-163`, MEM `feedback_wolverine_transaccion_antes_del_handler.md` | "Los servicios migran al arrancar" | Ningún `Database.Migrate`/`MigrateAsync` en `src/`; migra el contenedor `migrations` | Incorrecta | Descartar; guía dice "migra el deploy; Wolverine crea sus tablas al arrancar" |
| 46 | GCM §10.1 (:1164-1180) | user-secrets con `Jwt:Secret`; "el JWT secret / las claves RSA deben coincidir entre Auth, Tenant y Gateway" | RS256 con `Jwt:PublicKeyPath` en compose; HS256 solo como alternativa (`JwtAuthenticationRegistration.cs:16-18`) | Parcialmente vigente | Adoptar con corrección (RS256 por defecto; claves con `scripts/generate-dev-keys.*`) |
| 47 | GCM §10.2 (:1184,1208) | Secrets nuevos al environment `production` | `production` lleno; nuevos en `production-extra` (FinancialInsights `deploy.yml:595-601`) | Parcialmente vigente | Adoptar con corrección; **Decisión pendiente** del dueño por secret |
| 48 | GCM §11 offboarding (:1252-1258) | `[UserReference]` en cada referencia a staff; consumer de `UserOffboarded`; impacto; acuse | `[UserReference]` en `Note.cs`; acuse automático vía `UseTaxVisionRabbitMq` | Vigente | Adoptar |
| 49 | GCM §12 (38 guardrails) | Lista numerada 1-38 | Original de 36 en `G36`; GCM inserta `-m:1` (#32) y offboard (#38) | Parcialmente vigente | Reemplazar por `Referencias/Guardrails.md` (36 + complementarios) |
| 50 | BB2 BB-4 | `[MessageIdentity]` en todo contrato; nunca cambiar un alias; contratos nuevos `servicio.entidad.evento.v1`; `.v2` para incompatibles | `MessageIdentityTests`; aliases nuevos (`calendar.booking_requested.v1`, …) | Vigente | Adoptar |
| 51 | BB2 BB-6 | Heredar `LayeringRulesBase` | 26 servicios + subcontextos | Vigente | Adoptar |
| 52 | BB2 BB-3 (:73) | "`BuildingBlocks.Jobs` … Ningún servicio lo referencia todavía" | 12 servicios lo usan | Obsoleta (estado) | Marcar como histórica; adoptar la API |
| 53 | BB2 BB-1 / BB-2 / BB-11 | `UseTaxVisionDomainEvents()`, `UseTaxVisionDurableLocalQueues()`, `UseTaxVisionConflictPolicy()` (antes de las de fallo) | 17 / 19 / 26 servicios | Vigente | Adoptar; **Decisión pendiente**: obligatorios para servicios nuevos |
| 54 | BB2 BB-14 | Guardar antes de publicar; un servicio que publica por dentro esconde el problema | `SaveBeforePublishTests` | Vigente | Adoptar |
| 55 | EXT | Envío externo fuera de la transacción en 4 pasos (intención con clave, llamada `[NonTransactional]`, resultado en otra transacción, reconciliación) | `SendSmsBatch.cs:73` (`[NonTransactional]`) | Vigente | Adoptar para todo tercero no reversible |
| 56 | EXT trampas | Polly reintenta el POST: usar `CreateWithoutRetry`/`GetOrCreateWithoutRetry` en llamadas no idempotentes; no usar `HasDefaultValue` en columnas siempre asignadas | `src/BuildingBlocks/BuildingBlocks.Infrastructure/Resilience/` | Vigente | Adoptar |
| 57 | RBAC §3 | "HasQueryFilter global en los 13 servicios", "Session denylist en los 14 servicios" | 26 servicios con ambos | Obsoleta (cifras) | Marcar como histórica |
| 58 | RBAC §6.1-6.8 | Mini-doc: proteger endpoint, agregar permiso, proteger agregado por dueño, proyección de permisos | Coincide con Notes | Vigente | Adoptar (remitir) |
| 59 | MEM `feedback_permisos_catalogo_en_codigo_manda.md`, `feedback_no_conceder_permisos_sistema_por_migracion.md` | Permiso nuevo: `PermissionCatalog` + `InsertData` en `Permissions`; nunca grant por migración | `SystemRolePermissionsSyncService` | Vigente | Adoptar |
| 60 | MEM `feedback_fanout_rabbit_sin_dueno…` (primera parte) | Evento con un solo consumidor → exchange dedicado `taxvision-<dominio>` | Corrección del dueño en la misma nota (2026-10-21): "publicar siempre a `taxvision-events`" | Reemplazada | Adoptar solo la corrección |
| 61 | AUD 01 | Compose con SQL Server, Loki, Tempo; 18 áreas | SQL externo, Loki/Tempo comentados, 30 carpetas en `src/Services` | Obsoleta | Marcar como histórica |
| 62 | AUD 08 | DB-002: `rowversion` detecta pero hay que resolver (reintento o conflicto) | `UseTaxVisionConflictPolicy` + `ConflictException` → 409 | Parcialmente vigente | Adoptar como principio |
| 63 | RUB hechos técnicos | "Nadie usa `PublishDomainEventsFromEntityFrameworkCore`; ningún `Program.cs` activa `UseDurableLocalQueues`" | Reemplazado por `UseTaxVisionDomainEvents` (17) y `UseTaxVisionDurableLocalQueues` (19) | Reemplazada | Marcar como histórica |
| 64 | CONV | "Wallet no tiene proyecto de pruebas" | `deploy/tests/TaxVision.Wallet.Tests` existe y está en `.slnx` | Obsoleta | Descartar |
| 65 | CONV | Gate exacto `dotnet test TaxVision.slnx --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"`; T-INT en `.Integration.` | Vigente; se corre en local | Vigente | Adoptar |
| 66 | REM (:117) | "Cada `DbContext` llama `UseUtcDateTimes()`… lo prueba `UtcDateTimesTests` en cada servicio" | 23/26; Calendar, Signature, Tasks con conversor propio y sin la prueba | Parcialmente vigente | Adoptar como regla; **Decisión pendiente** para los 3 servicios |
| 67 | REM (:97-98) | "La suite completa solo es verde con `-m:1` … el CI corre en paralelo" | El CI ya no corre pruebas | Obsoleta | Marcar como histórica |
| 68 | LIB p. 8, p. 20 | Application no referencia Wolverine | Application referencia `WolverineFx` (`IMessageBus`) | Desvío aceptado (RUB) | Adoptar el desvío y explicarlo |
| 69 | LIB p. 22 | `ApplyMigrationsAsync()` (`MigrateAsync`) al arrancar | El propio libro (p. 65) lo desaconseja para producción; TaxVision migra en el deploy | Incorrecta para TaxVision | Descartar |
| 70 | LIB p. 37 | `AddDbContextWithWolverineIntegration` + `PublishDomainEventsFromEntityFrameworkCore` | BB-1: con `AddDbContextWithWolverineIntegration` (Wolverine 6.14) el evento no queda en el inbox durable; usar `AddDbContext` + `UseTaxVisionDomainEvents()` (`DomainEventDispatchExtensions.cs:17-20`) | Reemplazada | Reemplazar por BB-1 |
| 71 | LIB p. 42-43 | Excepciones de negocio + `GlobalExceptionHandler` | `Result`/`Error` + `ErrorHttpMapping` | Desvío aceptado (RUB) | Adoptar el desvío |
| 72 | LIB p. 43 | `appsettings.Development.json` con usuario y contraseña de SQL y RabbitMQ | TaxVision: user-secrets en local, `.env` generado por `deploy.yml` | Insegura | Descartar (no copiar el ejemplo) |
| 73 | LIB p. 37 | Exchange por servicio (`orders.events`) | Un fanout `taxvision-events` | No aplica | Descartar |
| 74 | `src/Services/Communication/README.md` | "Único servicio en Node.js… Node ≥ 20.11" | 3 servicios Node; `engines.node >= 24.0.0` | Obsoleta | Reemplazar en la guía; **Decisión pendiente** actualizar el README |
| 75 | `src/BuildingBlocks/Tenancy/ITenantContext.cs:3-8` | Sin tenant, los DbContext "deben degradar a sin filtro" | Todos fail-closed (`NotesDbContext.cs:63`) | Incorrecta | Seguir al código (guardrail #12) |
| 76 | `DEVS/Guia_Integracion_*.md`, `Guia_Support_Tickets…`, `Guia_TenantBrands_Frontend.md` | Integración de frontend por servicio (endpoints, DTOs, permisos) | Audiencia declarada "desarrollador(a) frontend" | Fuera de alcance | Remitir como anexos; no forman parte de la guía de creación |

## Contradicciones entre fuentes (resumen)

1. **Publicar antes o después de guardar:** GCM §3.3 publica antes; BB-14, memoria y `SaveBeforePublishTests` exigen guardar primero. Gana el código (fila 24).
2. **"Los servicios migran al arrancar":** compose y memoria lo afirman; el código no lo hace (fila 45).
3. **Estilo de comentario:** IQF exige citar el bug; guardrail #36 y `AGENTS.md` lo prohíben (fila 30).
4. **Filtro de tenant sin tenant:** comentario de `ITenantContext` dice "sin filtro"; DbContext y guardrail #12 dicen fail-closed (fila 75).
5. **Exchange dedicado vs único:** primera parte de la lección de memoria vs corrección del dueño (fila 60).
6. **Enums de CloudStorage:** GCM dice corregir a GCS, pero hoy GCS está al día y GCM no (fila 36).
7. **Contador del gate de módulo:** 13 (guías) / 14 (prueba) / 15 (`Program.cs`) (fila 34).
8. **Numeración de guardrails:** 36 (original) vs 38 (GCM) (fila 49).
9. **UTC en todos los DbContext:** REM dice todos; el código dice 23/26 (fila 66).

## Huecos de evidencia

- EXT y GNE se verificaron por puntos (atributos y conteos), no flujo por flujo.
- No se leyeron completos `documents/architecture/{billing,campaigns,growth,…}/` ni `docs/audit` más allá de 00, 01, 08, 15.
- Las páginas del libro leídas: 1-9, 21-24, 31-32, 37-39, 43-44, 65-66.
