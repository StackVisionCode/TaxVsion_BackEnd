# Checklist maestro — Nuevo microservicio TaxVision

> Propósito: recorrer por fases todo lo que un servicio .NET nuevo necesita para llegar a producción sin quedar "funcional pero inaccesible", enlazando el checklist temático de cada fase.
> Manda el código: cada ítem sale de las investigaciones de `../Investigaciones/` y del inventario; lo no decidido va en "Decisiones pendientes".

**Cuándo aplica:** alta de un servicio .NET con API y base propia (para un worker sin dominio, solo las fases que correspondan).

**Regla innegociable:** **el proyecto de pruebas existe desde el inicio** (`deploy/tests/TaxVision.<Svc>.Tests`, registrado en `TaxVision.slnx`) y cada cambio empieza con su prueba en rojo. Sin CI de pruebas, el gate local es la única barrera.

**Reglas consolidadas:** guardar antes de publicar · publicar al fanout `taxvision-events` con ruta explícita · migraciones las aplica el contenedor `migrations` del deploy (local `scripts/migrate-local.ps1`) · codegen Static + `scripts/codegen/regenerate-wolverine-code.ps1` · Scribe republica por hash · revocación `DELETE auth/sessions/{id}` · gate local exacto + `.Integration.` del servicio + CSharpier sobre lo tocado · secrets nuevos en `production-extra` · reconciliación con `ReconciliationRetry` y URL de Tenant en compose · Cloudflare solo manual por el senior y con aprobación.

**Formato:** *Verificación* = comando, prueba o archivo; *Origen* = investigación. Condicionales entre corchetes.

---

## Fase 1 — Decidir

- [ ] **¿Hace falta otro servicio?** Servicio propio si no tiene invariantes de consistencia inmediata con otro contexto y nadie lo llama en su write path (ADR-018); un mecanismo compartido es biblioteca, un subdominio con estados e invariantes propios es bounded context (ADR-021); el criterio es de quién es el invariante (ADR-022). Proceso aparte sin dominio solo si el recurso de cómputo o ciclo de vida difiere. Considerar el monolito modular (Growth: dos contextos, un despliegue).
  - Verificación: decisión escrita con el criterio aplicado. · Origen: [Arquitectura](../Investigaciones/Arquitectura.md) "¿Hace falta otro microservicio?".
- [ ] **Servicio de referencia**: Notes (principal), Customer (eventos de dominio, TenantTimeZones), Reminder (solo modelado de agregado y Quartz); FinancialInsights para permisos, gate, rate limit y reconciliaciones.
  - Verificación: lista de archivos a copiar. · Origen: [03_INVENTARIO](../03_INVENTARIO_ARQUITECTURA.md) §5; [Auth_RBAC](../Investigaciones/Auth_RBAC.md) encabezado.
- [ ] **No copiar las deudas de la referencia**: comentarios con fases/narrativa, mensajes de error en español, `DateTime.UtcNow` dentro del agregado, `UseRabbitMq(...).AutoProvision()` de la guía antigua o el libro.
  - Verificación: revisión del PR. · Origen: [03_INVENTARIO](../03_INVENTARIO_ARQUITECTURA.md) §5 "Deudas"; [Arquitectura](../Investigaciones/Arquitectura.md) "Riesgos".
- [ ] **Elegir BuildingBlocks**: siempre núcleo, `Messaging`, `Authorization`, `Web`, `Infrastructure`, `EfCore`; + `Jobs` [si un job escribe y publica]; + `TenantTimeZones` [si calcula fechas en la zona de la oficina]; + `CustomerVisibility` [si filtra por cliente asignado]; + `Imaging` [si guarda fotos de perfil]; `PurchaseIntentAmount` [si fija montos que cobra PaymentApp]; protección de secretos [si cifra secretos en su base].
  - Verificación: referencias de los `.csproj`. · Origen: [BuildingBlocks](../Investigaciones/BuildingBlocks.md) "Recomendación".
- [ ] **¿EDA o M2M?** Hecho para quien le interese → evento; dato de otro servicio leído en cada request → proyección local; rellenar o recuperar → M2M en un job de reconciliación; consulta que no admite retraso → M2M directo; orden a un único servicio → cola dedicada sin fanout.
  - Verificación: tabla de integraciones del servicio. · Origen: [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §5.

## Fase 2 — Crear estructura y pruebas

- [ ] **4 proyectos** `TaxVision.<Svc>.{Domain,Application,Infrastructure,Api}` con `net10.0`, `ImplicitUsings`, `Nullable`; solo Api con `Sdk.Web` y `UserSecretsId`; referencias exactamente como la tabla "Capas reales".
  - Verificación: `dotnet build`; `LayeringRulesBase`. · Origen: [Arquitectura](../Investigaciones/Arquitectura.md) "Capas reales" y "Paso a paso".
- [ ] **Alta en `TaxVision.slnx`** (`<Folder Name="/src/Services/<Svc>/">`); requisito para que la imagen `migrations` lo compile.
  - Verificación: `TaxVision.slnx`. · Origen: [Arquitectura](../Investigaciones/Arquitectura.md) "Alta en TaxVision.slnx".
- [ ] **`AssemblyMarker`** en Application y `Add<Svc>Infrastructure` en Infrastructure; `Program.cs` copiado de Notes (o FinancialInsights) sin lógica propia, terminado en `public partial class Program;`.
  - Verificación: `Program.cs` contra la tabla de orden real. · Origen: [Arquitectura](../Investigaciones/Arquitectura.md) "Program.cs típico".
- [ ] **Proyecto de pruebas creado y registrado** con las 4 pruebas mínimas de arquitectura (`<Svc>LayeringTests`, `<Svc>ArchitectureTests`, `PreGeneratedHandlerCodeTests` + `<Svc>RoutingHostFactory`, `UtcDateTimesTests`).
  - Verificación: gate exacto. · Origen: [Testing](Testing.md) §2-§3; [Testing_Unit](../Investigaciones/Testing_Unit.md) §5.
- [ ] **Versiones fijadas por `.csproj`** (no hay `Directory.Packages.props`): WolverineFx 6.14.0, EF 10.0.9, etc.; Fluid.Core 2.31.0 si usa Liquid.
  - Verificación: `.csproj` contra la tabla 6.1. · Origen: [03_INVENTARIO](../03_INVENTARIO_ARQUITECTURA.md) §6.

## Fase 3 — Dominio

- [ ] **Reglas de negocio con `Result`/`Error`**, nunca excepciones.
  - Verificación: T-DOM sobre `Result`. · Origen: [Arquitectura](../Investigaciones/Arquitectura.md) "Result / Error".
- [ ] **Agregado**: `sealed`, constructor privado, `Create()` → `Result<T>`, `private set`, colecciones por backing field; sin `virtual`.
  - Verificación: T-DOM; revisión. · Origen: [Guardrails](../Referencias/Guardrails.md) #5.
- [ ] **Una transición = un método con nombre de negocio** (sin `ChangeStatus` genérico) y su evento.
  - Verificación: T-DOM por transición inválida. · Origen: [Guardrails](../Referencias/Guardrails.md) #2.
- [ ] **Value Objects** para datos con reglas, uno por archivo en `Domain/ValueObjects/`.
  - Verificación: T-DOM con valores límite. · Origen: [Guardrails](../Referencias/Guardrails.md) #3 y #6.
- [ ] **Sin `System.Linq` en Domain.**
  - Verificación: regla NetArchTest copiada de `CalendarArchitectureTests`. · Origen: [Guardrails](../Referencias/Guardrails.md) #4.
- [ ] **El agregado recibe la hora** (desde `TimeProvider`), no llama a `DateTime.UtcNow`.
  - Verificación: T-DOM con reloj explícito. · Origen: [03_INVENTARIO](../03_INVENTARIO_ARQUITECTURA.md) §5 "Deudas".
- [ ] **Handlers ≤ ~30 líneas**, métodos privados por fase (load → validate/mutate → persist+publish).
  - Verificación: revisión o `HandlerSizeFitnessTests`. · Origen: [Guardrails](../Referencias/Guardrails.md) #1.
- [ ] **Códigos `NotFound`/`Conflict` nuevos registrados en `ErrorHttpMapping`** (si no, responden 400).
  - Verificación: prueba del controller o de `ErrorHttpMappingTests`. · Origen: [Arquitectura](../Investigaciones/Arquitectura.md) "Result / Error" y paso 6.
- [ ] [si emite eventos de dominio] `AggregateRoot` + `UseTaxVisionDomainEvents()` + `LocalQueue(DomainEventsQueue).Sequential()` + `Ignore(x => x.DomainEvents)`; handler `XxxDomainEventHandler` estático que devuelve el evento de integración.
  - Verificación: `DomainEventHandlerConventionTests`. · Origen: [Arquitectura](../Investigaciones/Arquitectura.md) "Program.cs típico"; [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §4 y §10.
- [ ] **Referencias a empleados anotadas** con `[UserReference]`.
  - Verificación: `UserReferenceContractTests`. · Origen: [Offboarding](Offboarding.md) §2 paso 2.
- [ ] **Comentarios cortos, en español, con el porqué**; sin fases ni fechas; identificadores en inglés.
  - Verificación: revisión. · Origen: [Guardrails](../Referencias/Guardrails.md) #36.

## Fase 4 — Persistencia

- [ ] **`<Svc>DbContext`** con filtro fail-closed sobre `ITenantOwned` y `UseUtcDateTimes()` como **última** línea de `OnModelCreating`; implementa `IUnitOfWork`.
  - Verificación: `UtcDateTimesTests`; `TenantIsolationTests`. · Origen: [Migraciones](../Investigaciones/Migraciones.md) "Paso a paso" 1 y "Buenas prácticas".
- [ ] **`<Svc>DbContextFactory`** copiado de Notes (`ConnectionStrings__Default`).
  - Verificación: `dotnet ef migrations add` funciona. · Origen: [Migraciones](../Investigaciones/Migraciones.md) "Paso a paso" 2.
- [ ] **Configuraciones**: una por entidad; `ValueGeneratedNever()` en hijas con Guid de dominio; `IsRowVersion()` en raíces con escritura concurrente; `Ignore()` en calculadas.
  - Verificación: prueba `EntityState.Added` fuera de `Persistence/`; revisar la migración generada. · Origen: [Migraciones](../Investigaciones/Migraciones.md) "Paso a paso" 3; [Guardrails](../Referencias/Guardrails.md) #9-#10.
- [ ] **Repositorios**: `IgnoreQueryFilters()` + `TenantId` explícito cuando los llaman handlers, consumers o jobs.
  - Verificación: prueba de repositorio con contexto sin tenant. · Origen: [Guardrails](../Referencias/Guardrails.md) #8.
- [ ] **`InitialCreate` aunque el DbContext esté vacío**, creada con `dotnet ef migrations add <Nombre> --project <Infra.csproj> --startup-project <Api.csproj> --context <Svc>DbContext` (nombre sin número de fase) y **aplicada de inmediato**: `scripts/migrate-local.ps1 -Apply -Service <Svc>`. Cadena de desarrollo desde los user-secrets del Api.
  - Verificación: `scripts/migrate-local.ps1` sin pendientes. · Origen: [Migraciones](../Investigaciones/Migraciones.md) "Cómo se crean y aplican en local"; [Guardrails](../Referencias/Guardrails.md) #11.
- [ ] [si un job escribe y publica] `TransactionalJobUnit.RunEachAsync` (una unidad por ítem con outbox).
  - Verificación: `JobPublishingRulesBase`. · Origen: [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §4; [BuildingBlocks](../Investigaciones/BuildingBlocks.md) matriz "Jobs".
- [ ] Pruebas: ver [Testing](Testing.md) §4-§5.

## Fase 5 — Seguridad

- [ ] Completar [Seguridad](Seguridad.md): JWT RS256 y orden del pipeline, `[AllowActorTypes]` + `[HasPermission]` + `[RateLimit]` por acción, permisos en BuildingBlocks y catálogo de Auth, modo Projection, aislamiento de tenant, denylist, superficie `internal/*`, webhooks y rate limit.

## Fase 6 — Integración y eventos

- [ ] **Bloque `UseWolverine`**: `UseTaxVisionCodeGeneration`, `Discovery.IncludeAssembly(AssemblyMarker)`, `UseTaxVisionRabbitMq(uri)`, `PersistMessagesWithSqlServer`, `UseDurableOutboxOnAllSendingEndpoints`, `UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, <Svc>DbContext>()`, `AutoApplyTransactions`, `UseTaxVisionConflictPolicy()` **antes** de `ApplyStandardFailurePolicies()`, `UseTaxVisionDurableLocalQueues()`, middlewares de tenant.
  - Verificación: `Program.cs` contra FinancialInsights `:83-123`. · Origen: [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §10; [BuildingBlocks](../Investigaciones/BuildingBlocks.md) "Buenas prácticas".
- [ ] [si publica] **Contrato** en `src/BuildingBlocks/Messaging/<Svc>IntegrationEvents/` heredando `IntegrationEvent`, con `[MessageIdentity(...)]` y `CorrelationId`; solo campos nuevos opcionales al evolucionar.
  - Verificación: `MessageIdentityTests`, `CorrelationIdFitnessTests`. · Origen: [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §4 y §10.
- [ ] [si publica] **Ruta explícita** `options.PublishMessage<T>().ToRabbitExchange("taxvision-events")` por cada evento.
  - Verificación: `EventRoutingContractTests` + prueba de rutas de runtime (patrón Calendar). · Origen: [Guardrails](../Referencias/Guardrails.md) #13; [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §6.
- [ ] [si consume] **Cola `<svc>-events`** enlazada a `taxvision-events` con `UseDurableInbox()`; handlers `public static class XxxConsumer`/`XxxHandler` en singular; `correlation.Push(...)` en la primera línea.
  - Verificación: `HandlerNamingContractTests`; T-APP del consumer. · Origen: [Guardrails](../Referencias/Guardrails.md) #14-#15; [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §4.
- [ ] **Guardar antes de publicar** (comandos con `InvokeAsync`); lanzar `DependencyNotReadyException` si falta una proyección que llegará.
  - Verificación: `SaveBeforePublishTests` (BB); T-INT de atomicidad. · Origen: [Guardrails](../Referencias/Guardrails.md) GC-W1; [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §4.
- [ ] [si lo consume Node] Mapa `CLR_TYPE_TO_EVENT_TYPE` o lista de ignorados (`ignored-event-types.ts`).
  - Verificación: `NodeInteropContractTests`. · Origen: [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §10.
- [ ] **Regenerar codegen** de emisor y receptor al agregar/cambiar handlers, middleware o `IncludeType`: `powershell -ExecutionPolicy Bypass -File scripts\codegen\regenerate-wolverine-code.ps1 -Service <Svc>` (fuerza Dynamic mientras regenera). Agregar el servicio a `$all` del script.
  - Verificación: `PreGeneratedHandlerCodeTests` en el gate. · Origen: [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §6 y §10.
- [ ] [si llama a otros servicios por M2M] Cliente, acquirer con caché y secretos: ver [Seguridad](Seguridad.md) §6.

## Fase 7 — Proyecciones y reconciliación

- [ ] Completar [Proyecciones_Reconciliation](Proyecciones_Reconciliation.md): permisos, plan, kits compartidos, reparación pull/push, `ReconciliationRetry` + `ReconciliationRetryTests`, URL de Tenant/Customer explícita en compose.

## Fase 8 — Offboarding

- [ ] Completar [Offboarding](Offboarding.md): decisión ¿participa?, 15 pasos y batería de pruebas.

## Fase 9 — Comercialización

- [ ] Completar [Modulos_Planes](Modulos_Planes.md): decidir si se vende; si sí, permisos + mapa + catálogo + revisión + gate + frontends a la vez.

## Fase 10 — Notificaciones, enlaces y zona horaria

- [ ] Notificaciones: [Notificaciones](Notificaciones.md). Enlaces: [Tenant_Links](Tenant_Links.md).
- [ ] **`AddTimeZoneSelfCheck()`** en `Program.cs` (no detiene el arranque; deja una línea de log).
  - Verificación: log de arranque. · Origen: [Observabilidad](../Investigaciones/Observabilidad.md) §2.6.
- [ ] **Cálculos de fecha civil o zona con los tipos de `BuildingBlocks/TimeZones`** (`IanaTimeZone`, `CivilDate`, `LocalDay`, `MonthlyUsagePeriod`…), sin aritmética de offsets a mano.
  - Verificación: T-DOM con vectores de tiempo. · Origen: [BuildingBlocks](../Investigaciones/BuildingBlocks.md) matriz "TimeZones".
- [ ] [si calcula vencimientos o formatea en la zona de la oficina] Kit `TenantTimeZones` con migración y reconciliación (ver [Proyecciones_Reconciliation](Proyecciones_Reconciliation.md) §2).
  - Origen: [BuildingBlocks](../Investigaciones/BuildingBlocks.md) matriz "TenantTimeZones".
- [ ] **Contenedor en UTC**: `ENV TZ=Etc/UTC` en la etapa final del Dockerfile y `TZ: Etc/UTC` en compose.
  - Verificación: `ContainerUtcTimeZoneDeployTests`. · Origen: [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §2.4 y §9.1.

## Fase 11 — Gateway y Cloudflare

- [ ] Completar [YARP_Cloudflare](YARP_Cloudflare.md): ruta, cluster, criticidad, override en compose, Caddy si hay rutas abiertas en el navegador y, solo si hace falta, el procedimiento manual de Cloudflare con el senior y aprobación.

## Fase 12 — Docker, compose, scripts locales, CI y secrets

- [ ] **Dockerfile** en `src/Services/<Svc>/TaxVision.<Svc>.Api/Dockerfile` copiado de Tenant/FinancialInsights (`sdk:10.0.300` / `aspnet:10.0.9`, `TZ=Etc/UTC`, `MALLOC_ARENA_MAX=2`, `EXPOSE 8080`).
  - Verificación: `ContainerUtcTimeZoneDeployTests`, `DotnetImageMallocArenaTests`. · Origen: [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §9.1.
- [ ] **Bloque `<svc>-api` en compose** (patrón `wallet-api`): `restart: unless-stopped`, `build:`, `expose: "8080"`, **sin `ports:`**, `ASPNETCORE_ENVIRONMENT: Production`, `ConnectionStrings__Default: ${<SVC>_DB_CONNECTION:?...}`, Redis, RabbitMQ, JWT, `Authorization__PermissionsSource: Projection`, `ServiceAuthClient__*`, OTLP, `depends_on` con condiciones.
  - Verificación: `ComposeRestartPolicyWiringTests`, `DockerComposeNoPublicApiPortsFitnessTests`. · Origen: [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §9.2-§9.3.
- [ ] **URLs de otros servicios como `http://<otro>-api:8080`**, nunca `localhost`.
  - Verificación: `ComposeInternalBaseUrlWiringTests`. · Origen: [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §9.3.
- [ ] [si otro servicio lo espera con `service_healthy`] `healthcheck` + `curl` en la imagen.
  - Verificación: `ComposeHealthcheckWiringTests`. · Origen: [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §5.
- [ ] **Migraciones en producción**: bloque en `apply-migrations.sh` y `<SVC>_DB_CONNECTION` en el servicio `migrations` (ver [Produccion](Produccion.md) §2).
  - Origen: [Migraciones](../Investigaciones/Migraciones.md) "Paso a paso" 6.
- [ ] **Secrets**: GitHub environment `production-extra`, línea en *Append production-extra secrets to .env* del job `deploy`, nombre en `ProductionExtraSecretsDeployTests` (el dueño carga los valores).
  - Verificación: `ProductionExtraSecretsDeployTests`, `ServiceClientSecretsDeployWiringTests`. · Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §9.1.
- [ ] **El workflow descubre el servicio solo** por su `build:` (sin lista); construye en tandas de 3.
  - Verificación: `.github/workflows/deploy.yml` paso "Build images". · Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §9.1.
- [ ] **Flota local**: puerto libre de `scripts/start-fleet.ps1` fijado en `launchSettings.json`; misma línea en `start-fleet.ps1`, `restart-down.ps1` y `$ports` de `stop-fleet.ps1`; servicio en `$all` del script de codegen; cluster local en el Gateway; user-secrets (`ConnectionStrings:Default`, `ConnectionStrings:Redis`, `RabbitMq:Uri`, `ServiceAuthClient:*` y su contraparte en Auth); `dotnet build -c Release` del Api.
  - Verificación: `DevClientPortDefaultsTests`; arranque con `scripts\start-fleet.ps1`. · Origen: [Scripts_Locales](../Investigaciones/Scripts_Locales.md) §9.1.
- [ ] **Probar en Static en local** cuando se toca un handler: `$env:Wolverine__CodeGeneration__TypeLoadMode = "Static"` (doble guion bajo).
- [ ] **Registro de DI igual en todo entorno**: nada se registra solo si hay una clave; el servicio opcional se registra siempre y se apaga por propiedad (`IsEnabled`). Si no, el código pregenerado de Wolverine toma servicios scoped desde la raíz cuando la clave existe.
  - Verificación: usar cada handler una vez. · Origen: [Scripts_Locales](../Investigaciones/Scripts_Locales.md) §9.3; [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §10 paso 6.
- [ ] **Nunca leer la cadena del `.env`** en local (apunta a otra base): la configuración sale de user-secrets.
  - Origen: [Scripts_Locales](../Investigaciones/Scripts_Locales.md) §6.

## Fase 13 — Postman

- [ ] **Colección** `Postman_Collection/TaxVision_<Svc>.postman_collection.json` copiada de Notes: descripción con environment, actores y permisos; carpetas por superficie (staff, portal, público, webhooks; sin `internal`); URL `{{UrlBase}}/<prefijo>/...`; `Authorization: Bearer {{accessToken}}` (o el token del actor).
  - Verificación: la colección corre contra `http://localhost:5047`. · Origen: [Postman](../Investigaciones/Postman.md) §9.1.
- [ ] **Scripts de test** en cada `POST` de creación que validan el código y guardan `<svc><Entidad>Id`.
  - Verificación: corrida en Postman. · Origen: [Postman](../Investigaciones/Postman.md) §9.1.
- [ ] **Variables nuevas vacías** en `TaxVisionBackEnd.postman_environment.json`, sensibles con `type: secret`; nunca exportar con tokens llenos.
  - Verificación: diff del environment sin valores. · Origen: [Postman](../Investigaciones/Postman.md) §6 y §9.1.
- [ ] **Casos 401 sin token, 403 sin permiso y 404 de otro tenant** en la colección; sin `X-Tenant-Id`.
  - Verificación: corrida en Postman. · Origen: [Postman](../Investigaciones/Postman.md) §2.2 y §9.1.

## Fase 14 — Observabilidad

- [ ] **`UseTaxVisionSerilog("<svc>-service")`, `AddTaxVisionOpenTelemetry(config, "<svc>-service")`**, `CorrelationIdMiddleware` antes de `UseSerilogRequestLogging`.
  - Verificación: `Program.cs` contra Notes. · Origen: [Observabilidad](../Investigaciones/Observabilidad.md) §5 y §9.1.
- [ ] **Health checks** `AddDbContextCheck<<Svc>DbContext>("sql-server", tags: ["ready"])` + `TcpEndpointHealthCheck` de RabbitMQ; `/health/live` (sin checks) y `/health/ready` (tag `ready`).
  - Verificación: `GET /health/ready` en local. · Origen: [Observabilidad](../Investigaciones/Observabilidad.md) §2.3 y §9.1.
- [ ] [si mide algo propio] `Meter` con el mismo nombre que se pasa a `AddTaxVisionOpenTelemetry`.
  - Verificación: patrón `Observability/` de Calendar con `OpenTelemetry.Exporter.InMemory`. · Origen: [Observabilidad](../Investigaciones/Observabilidad.md) §9.2.

## Fase 15 — Producción

- [ ] Cumplir [Produccion](Produccion.md): DoD local, migraciones, secrets, consumers, proyecciones pobladas (SQL de ejemplo de TenantTimeZones), reconciliación, codegen Static, verificación funcional, logs, rollback y runbook.
- [ ] **El dueño hace commit y push** (el push a `main` despliega). Nunca `commit`/`push`/`reset` desde la sesión.
  - Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §2.6.

---

## Decisiones pendientes que afectan a un servicio nuevo

- Si `UseTaxVisionDomainEvents()` y `UseTaxVisionDurableLocalQueues()` pasan a obligatorios ([BuildingBlocks](../Investigaciones/BuildingBlocks.md) "Recomendación").
- Convención de `[MessageIdentity]` para eventos nuevos (`.v1` vs nombre CLR) y regla de exchanges dedicados ([Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §3).
- Códigos de error en `ErrorHttpMapping` central vs convención por sufijo ([Arquitectura](../Investigaciones/Arquitectura.md) "Recomendación").
- Si `Error.Message` llega a la UI (idioma de los mensajes) ([03_INVENTARIO](../03_INVENTARIO_ARQUITECTURA.md) §5).
- `USER` no root en imágenes .NET y volumen `taxvision-logs` ([Docker_Deploy](../Investigaciones/Docker_Deploy.md) §5).
- Una sola convención de Postman (`Postman_Collection/` vs `postman/`) ([Postman](../Investigaciones/Postman.md) §5).
