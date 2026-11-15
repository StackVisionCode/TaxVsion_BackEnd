# Checklist — Pruebas de un microservicio

> Propósito: que el servicio nazca con su proyecto de pruebas y las pruebas mínimas de arquitectura, dominio, aplicación, integración, seguridad y EDA que ya usa la flota.
> El CI no corre pruebas (decisión del dueño desde 2026-11-13): el gate local es la única barrera antes del push a `main`.

**Cuándo aplica:** desde el primer commit de un servicio nuevo y en todo cambio de dominio, handler, consumer, persistencia, controller o despliegue.

**Regla innegociable:** el proyecto `deploy/tests/TaxVision.<Svc>.Tests` existe desde el inicio, está en `TaxVision.slnx` y cada cambio empieza por una prueba en rojo.

**Formato:** *Verificación* = comando, prueba o archivo; *Origen* = investigación. Condicionales entre corchetes.

---

## 1. Gate y disciplina

- [ ] **Gate exacto en local, verde, antes de pedir push:**
  `dotnet test TaxVision.slnx --nologo --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"`
  - Verificación: salida del comando sin fallos.
  - Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §1 y §9.5; [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §2.6.
- [ ] **`.Integration.` del servicio** corridas contra la infraestructura local si se tocó persistencia, un handler o sus dependencias:
  `dotnet test deploy/tests/TaxVision.<Svc>.Tests/TaxVision.<Svc>.Tests.csproj --nologo --filter "FullyQualifiedName~.Integration."`
  - Verificación: salida del comando, reportada junto al gate.
  - Origen: [Testing_Integration](../Investigaciones/Testing_Integration.md) §2.7 y §9.3.
- [ ] **CSharpier sobre lo tocado antes del gate** (`dotnet csharpier format <archivos tocados>`; ignora `**/Internal/Generated/`). El formato puede romper fitness de tamaño de método.
  - Verificación: sin diferencias tras formatear; gate verde después.
  - Origen: [Testing_EDA](../Investigaciones/Testing_EDA.md) §9.3; [Testing_Unit](../Investigaciones/Testing_Unit.md) §6.
- [ ] **Ciclo rojo → arreglo mínimo → verde → gate.**
  - Verificación: salida en rojo guardada antes del arreglo.
  - Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §2.5.
- [ ] **Pensar en Linux**: nada de `Path.GetInvalidFileNameChars()` de Windows en expectativas, ni fábricas con user-secrets fuera de `.Integration.`.
  - Verificación: revisión; las pruebas de arquitectura usan `<Svc>RoutingHostFactory`.
  - Origen: [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §2.6; [Testing_Integration](../Investigaciones/Testing_Integration.md) §2.1.
- [ ] **Al terminar**, cerrar `VBCSCompiler`/`testhost` que queden vivos.
  - Verificación: Administrador de tareas sin esos procesos.
  - Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §9.5.

## 2. Proyecto de pruebas

- [ ] **csproj copiado de `TaxVision.Notes.Tests.csproj`**: `net10.0`, `IsTestProject=true`, xunit 2.9.3, runner 3.1.5, Test.Sdk 17.14.1, EF InMemory 10.0.9, NetArchTest 1.3.2, Mvc.Testing 10.0.9, Jwt 8.19.1; referencias a las 4 capas, `BuildingBlocks.Messaging` y `TaxVision.BuildingBlocks.Testing` con `Aliases="BbTesting"`. Sin Moq/FluentAssertions: dobles a mano.
  - Verificación: `dotnet build` del proyecto.
  - Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §1 y §9.1.
- [ ] **Registrado en `TaxVision.slnx`** dentro de `<Folder Name="/deploy/tests/">` (estar en la solución lo mete al gate).
  - Verificación: `TaxVision.slnx`.
  - Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §2.6 y §9.2.
- [ ] **El Api termina con `public partial class Program;`** (para `WebApplicationFactory<Program>`).
  - Verificación: última línea de `Program.cs`.
  - Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §2.6.
- [ ] **Carpetas y nombres**: `Domain/`, `Application/`, `Architecture/`, `Integration/`, `Api/`…; clase por clase probada (`<Clase>Tests`), sin números de fase, identificadores en inglés.
  - Verificación: revisión del árbol.
  - Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §2.2 y §2.5.
- [ ] **Pruebas con InMemory que no necesitan infraestructura fuera de `Persistence/`** (el filtro del gate excluye `.Persistence.`).
  - Verificación: namespace de cada archivo nuevo.
  - Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §5; [Migraciones](../Investigaciones/Migraciones.md) "Pruebas".

## 3. Pruebas mínimas de arquitectura (T-ARQ, corren en el gate)

- [ ] `<Svc>LayeringTests : LayeringRulesBase` (Domain sin EF/ASP.NET/Wolverine; Application sin Infrastructure/EF).
  - Verificación: gate. · Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §9.3; [Arquitectura](../Investigaciones/Arquitectura.md) §Pruebas.
- [ ] `<Svc>ArchitectureTests` copiado de Notes (`[AllowActorTypes]`, `[RateLimit]`/`[RateLimitExempt]`, controllers solo dependen de Application).
  - Verificación: gate. · Origen: [Testing_Security](../Investigaciones/Testing_Security.md) §9.1.
- [ ] `PreGeneratedHandlerCodeTests` + `<Svc>RoutingHostFactory` (colección `DisableParallelization = true`). Verla en rojo una vez (borrar un archivo de `Internal/Generated` o agregar un handler sin regenerar).
  - Verificación: gate. · Origen: [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §8 y §10; [Testing_EDA](../Investigaciones/Testing_EDA.md) §9.1.
- [ ] `UtcDateTimesTests` copiado de Notes (usa `UseSqlServer("Server=unused;...")` sin conectar).
  - Verificación: gate. · Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §9.4.
- [ ] [si rutea a colas locales] `LocalQueueDurabilityTests : LocalQueueDurabilityRules` con la fábrica de rutas.
  - Verificación: gate. · Origen: [Testing_EDA](../Investigaciones/Testing_EDA.md) §9.2.
- [ ] [si emite eventos de dominio] `DomainEventHandlerConventionTests : DomainEventHandlerConventionRules`.
  - Verificación: gate. · Origen: [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §8.
- [ ] [si publica eventos de integración] Prueba de rutas de runtime copiada de Calendar `IntegrationEventRoutingTests` (la de texto `EventRoutingContractTests` no ve eventos en cascada).
  - Verificación: gate. · Origen: [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §3.3 y §10; [Testing_EDA](../Investigaciones/Testing_EDA.md) §5.
- [ ] [si tiene jobs que escriben y publican] `JobPublishingRulesBase` heredada.
  - Verificación: gate. · Origen: [BuildingBlocks](../Investigaciones/BuildingBlocks.md) §Pruebas.
- [ ] [si el servicio quiere vigilar tamaño de handler] `HandlerSizeFitnessTests` (patrón PaymentApp: `MaxHandleBodyLines = 40`).
  - Verificación: gate. · Origen: [Guardrails](../Referencias/Guardrails.md) #1.
- [ ] [si el Domain no debe usar LINQ] Regla `ShouldNot().HaveDependencyOn("System.Linq")` copiada de `CalendarArchitectureTests`.
  - Verificación: gate. · Origen: [Guardrails](../Referencias/Guardrails.md) #4.

## 4. Dominio y aplicación (T-DOM, T-APP)

- [ ] **T-DOM por agregado y VO**: sin infraestructura, reloj explícito en UTC, `Assert` sobre `Result`; una transición inválida → `Result` fallido.
  - Verificación: `Domain/<Clase>Tests.cs` (patrón `NoteTests`). · Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §2.3; [Guardrails](../Referencias/Guardrails.md) #2-#3.
- [ ] **T-APP por handler y consumer** con dobles por puerto: publica lo que debe, no publica si falla. Ojo: `FakeMessageBus` pasa aunque falte la ruta.
  - Verificación: `Application/<Handler>Tests.cs` (patrón `CreateNoteHandlerTests`). · Origen: [Testing_Unit](../Investigaciones/Testing_Unit.md) §2.3; [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §3.8.
- [ ] [si hay hijas con Guid generado en dominio] Prueba con DbContext real (InMemory basta) que afirme `EntityState.Added` y relea en un contexto nuevo, **fuera** de `Persistence/`.
  - Verificación: gate. · Origen: [Guardrails](../Referencias/Guardrails.md) #10; [Migraciones](../Investigaciones/Migraciones.md) "Pruebas".

## 5. Integración (T-INT, namespace `.Integration.`, fuera del gate)

- [ ] `Integration/<Svc>IntegrationDatabase.cs` con `TemporaryTestDatabase("<prefijo>_tint", ...)` y `[assembly: TestFramework(TemporaryDatabaseTestFramework...)]`; conexión desde `<SVC>_DB_CONNECTION` o los user-secrets del Api (nunca impresa).
  - Verificación: corrida `.Integration.` deja cero bases huérfanas. · Origen: [Testing_Integration](../Investigaciones/Testing_Integration.md) §2.2 y §9.1.
- [ ] `Integration/<Svc>ApiFactory.cs`: `Development`, JWT HS256 aleatorio en memoria, `DisableAllExternalWolverineTransports()` (si no, roba mensajes de la flota) y jobs quitados.
  - Verificación: `IntegrationIsolationTests` (BB) en el gate. · Origen: [Testing_Integration](../Investigaciones/Testing_Integration.md) §2.3-§2.4.
- [ ] [si guarda y publica] Prueba de atomicidad del outbox: fila **y** sobre en `wolverine_outgoing_envelopes`; fallo después de guardar → ni fila ni sobre (patrón Wallet). Nada de RabbitMQ real.
  - Verificación: `.Integration.` del servicio. · Origen: [Testing_Integration](../Investigaciones/Testing_Integration.md) §9.2; [Testing_EDA](../Investigaciones/Testing_EDA.md) §2.4.
- [ ] [si hay concurrencia o índice único] T-INT de concurrencia (patrón `NoteConcurrencyIntegrationTests`); antes de un índice único sobre datos, consulta previa de duplicados.
  - Verificación: `.Integration.` del servicio. · Origen: [Testing_Integration](../Investigaciones/Testing_Integration.md) §2.5 y §6.
- [ ] [si la migración toca datos previos] `MigrationTestDatabase` (migra hasta antes, siembra, migra el resto).
  - Verificación: `.Integration.` del servicio. · Origen: [Migraciones](../Investigaciones/Migraciones.md) "Pruebas".

## 6. Seguridad, proyecciones, notificaciones, offboarding

- [ ] Seguridad mínima: ver [Seguridad](Seguridad.md) (aislamiento de tenant, 403 con actor real, ownership, firma de webhooks, denylist).
  - Origen: [Testing_Security](../Investigaciones/Testing_Security.md) §5.
- [ ] Proyecciones y reconciliación: ver [Proyecciones_Reconciliation](Proyecciones_Reconciliation.md).
  - Origen: [Proyecciones](../Investigaciones/Proyecciones.md) §7; [Reconciliation](../Investigaciones/Reconciliation.md) §7.
- [ ] Notificaciones y enlaces: ver [Notificaciones](Notificaciones.md) y [Tenant_Links](Tenant_Links.md).
- [ ] Offboarding: ver la batería de [Offboarding](Offboarding.md) §3.
- [ ] Despliegue: las pruebas de wiring (`deploy/tests/TaxVision.BuildingBlocks.Tests/Deploy/`) leen compose, Dockerfile y `deploy.yml`; tocarlos exige correr el gate completo.
  - Origen: [Docker_Deploy](../Investigaciones/Docker_Deploy.md) §2.4 y §6.

## 7. Matriz de pruebas (plantilla)

Copiar al documento del servicio y completar `Proyecto` y `Estado` (`Pendiente` → `Rojo visto` → `Verde`). Las filas son los escenarios mínimos que piden las investigaciones; borrar las que no apliquen por condición.

| Área | Tipo de prueba | Escenario | Proyecto | Estado |
|---|---|---|---|---|
| Arquitectura | T-ARQ | Capas (`LayeringRulesBase`) | `TaxVision.<Svc>.Tests` | Pendiente |
| Arquitectura | T-ARQ | Toda acción declara actor y rate limit | `TaxVision.<Svc>.Tests` | Pendiente |
| Arquitectura | T-ARQ | Handlers cargan de código pregenerado y está al día | `TaxVision.<Svc>.Tests` | Pendiente |
| Persistencia | T-ARQ | Todo `DateTime` se lee como UTC | `TaxVision.<Svc>.Tests` | Pendiente |
| EDA | T-ARQ | [si publica] Todo evento va a `rabbitmq://` (runtime) | `TaxVision.<Svc>.Tests` | Pendiente |
| EDA | T-ARQ | [si usa colas locales] Colas locales durables | `TaxVision.<Svc>.Tests` | Pendiente |
| Dominio | T-DOM | Cada transición válida e inválida devuelve `Result` | `TaxVision.<Svc>.Tests` | Pendiente |
| Aplicación | T-APP | Handler publica tras guardar; no publica si falla | `TaxVision.<Svc>.Tests` | Pendiente |
| Persistencia | T-INT | Atomicidad fila + sobre del outbox | `TaxVision.<Svc>.Tests` (`.Integration.`) | Pendiente |
| Persistencia | T-INT | Concurrencia / índice único | `TaxVision.<Svc>.Tests` (`.Integration.`) | Pendiente |
| Seguridad | T-INT | Staff sin permiso → 403 | `TaxVision.<Svc>.Tests` (`.Integration.`) | Pendiente |
| Seguridad | Persistencia (InMemory) | Filtro de tenant aísla A de B | `TaxVision.<Svc>.Tests` | Pendiente |
| Seguridad | T-INT | `sid` denylistado → 401 `Auth.SessionRevoked` | `TaxVision.<Svc>.Tests` (`.Integration.`) | Pendiente |
| Seguridad | T-APP | [si hay webhook] Firma inválida rechazada | `TaxVision.<Svc>.Tests` | Pendiente |
| Proyecciones | T-APP | Crea / mayor / menor / duplicado | `TaxVision.<Svc>.Tests` | Pendiente |
| Proyecciones | T-APP | Evento con `TenantId` vacío falla cerrado | `TaxVision.<Svc>.Tests` | Pendiente |
| Reconciliación | T-APP | Faltantes, desfasados, fila nueva intacta, fuente caída | `TaxVision.<Svc>.Tests` | Pendiente |
| Rate limit | T-ARQ | Cuotas acordadas de las políticas | `TaxVision.<Svc>.Tests` | Pendiente |
| Rate limit | T-INT | Exceder cuota → 429 con `Retry-After` | `TaxVision.<Svc>.Tests` (`.Integration.`) | Pendiente |
| Módulo | T-APP | [si vende módulo] `null` pasa, `[]` deniega, módulo presente permite | `TaxVision.<Svc>.Tests` | Pendiente |
| M2M | T-APP | [si llama M2M] Sin config → null; un POST por vida del token | `TaxVision.<Svc>.Tests` | Pendiente |
| Offboarding | T-APP | Con sucesor / sin sucesor / sucesor inválido / reentrega / histórico | `TaxVision.<Svc>.Tests` | Pendiente |
| Notificaciones | T-APP | [si tiene consumer en Notification] Fan-out a 2 destinatarios; reintento no reenvía | `TaxVision.Notification.Tests` | Pendiente |
| Enlaces | T-APP | Oficina A → host A; oficina B → host B | `TaxVision.Notification.Tests` | Pendiente |
| Vivo | T-VIVO | Flota local + Chrome del dueño | manual | Pendiente |

Fuentes de la matriz: [Testing_Unit](../Investigaciones/Testing_Unit.md), [Testing_Integration](../Investigaciones/Testing_Integration.md), [Testing_Security](../Investigaciones/Testing_Security.md), [Testing_EDA](../Investigaciones/Testing_EDA.md), [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §7, [RateLimit_Cuotas_Gates](../Investigaciones/RateLimit_Cuotas_Gates.md) §7, [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §7, [Offboarding_Integraciones](../Investigaciones/Offboarding_Integraciones.md) paso 14, [Notification_Email](../Investigaciones/Notification_Email.md) §7, [Tenant_Links](../Investigaciones/Tenant_Links.md) §7.

## Decisiones pendientes

- Si el filtro `.Persistence.` debe seguir excluyendo pruebas InMemory ([Testing_Unit](../Investigaciones/Testing_Unit.md) §5).
- Si la suite necesita `-m:1` (guardrail #32 de la guía antigua) ([Testing_Unit](../Investigaciones/Testing_Unit.md) problema 7).
