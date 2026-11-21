# Offboarding de TenantEmployees (retiro de un empleado de la oficina)

> Frente 3 de la Guía Maestra. Investigación de **solo lectura** (2026-11-15): no se modificó código, no se compiló, no se corrieron pruebas, no se tocó Docker ni bases de datos.
> Backend: `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd` (rutas relativas a esa raíz). Front CRM: `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_Front`.
> Matriz por servicio y procedimiento para un servicio nuevo: `Offboarding_Integraciones.md` (misma carpeta).

Etiquetas usadas:
- **Código verificado**: leído hoy en el código, con `ruta:línea`.
- **Documentación histórica**: viene de memoria o documentos previos; no se volvió a comprobar línea por línea.
- **Recomendación**: propuesta de esta investigación (no es plan de implementación).
- **Decisión pendiente**: necesita al dueño.

Alcance: **retirar (offboard)** a un empleado de su oficina. No es onboarding, ni borrado de usuario (no existe), ni desactivación global, ni cancelación del tenant (`SessionAccessCutoff.ForTenantAsync`, otro camino).

---

## 1. Resumen en una pantalla

| Tema | Respuesta corta |
|---|---|
| Servicio dueño | **Auth**. Comando `OffboardUserCommand`, endpoint `POST /auth/users/{userId}/offboard`. |
| Quién puede | Permiso `users.manage` + actor `TenantAdmin` o `PlatformAdmin`. |
| Qué es | Estado **terminal** `UserStatus.Offboarded` (no se puede reactivar). Invariante `Offboarded ⇒ IsActive == false`. |
| Corte de acceso | Síncrono, en el mismo comando: denylist del `sid` en Redis + aviso Pub/Sub (logout por socket) + revocación de sesiones/refresh en BD. MFA purgado, invitaciones que envió canceladas, avatar borrado. |
| Limpieza | Asíncrona, por **coreografía**: Auth publica `UserOffboardedIntegrationEvent` al fanout `taxvision-events`; 28 servicios reaccionan (52 pasos). |
| Seguimiento | `OffboardingRun` en Auth: cada paso acusa con `UserOffboardingStepCompletedIntegrationEvent`. A los 30 min sin todos los acuses → `UserOffboardingIncompleteIntegrationEvent` → Notification avisa a los admins. CRM: "Handover status N/N". |
| Guardarraíl | `[UserReference(Kind, HandledBy, Via)]` en cada campo de dominio que apunta a un empleado + 3 fitness tests en `TaxVision.BuildingBlocks.Tests/Offboarding/`. |
| Recuperación | No hay re-ejecución automática del handover. Reintentos Wolverine/Node (1s/5s/15s) → DLQ → drenado manual (README §48). |

---

## 2. Estado actual

### 2.1 Flujo síncrono (Auth) — Código verificado

1. **Endpoint** `UsersController.Offboard` — `src/Services/Auth/Api/Controllers/UsersController.cs:109-138`.
   - Atributos: `[HttpPost("{userId:guid}/offboard")]`, `[HasPermission(PermissionCatalog.UsersManage)]`, `[AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]`, `[RateLimit("auth.g.user_manage")]`.
   - Cuerpo opcional `OffboardUserRequest(Guid? SuccessorUserId, OffboardingReason? Reason, string? ReasonNote)` (`UsersController.cs:103-107`).
   - El tenant sale del JWT (`User.TryGetTenantId`), nunca del cuerpo.
2. **Handler** `OffboardUserHandler.Handle` — `src/Services/Auth/Application/Users/Commands/UserManagementCommands.cs:225-345`. Orden real:
   1. Carga al objetivo; si no existe **o es de otro tenant** → `User.NotFound` (línea ~243).
   2. `EnsureCanBeOffboardedBy` → nadie se retira a sí mismo (`User.SelfAction`) — `Domain/Users/User.cs:476-479`.
   3. **Idempotencia**: si ya está `Offboarded` → `Result.Success()` sin republicar (línea ~251).
   4. `EnsureAllowedAsync` (líneas 343-355): rechaza cliente de portal (`User.PortalClient`, `Domain/Offboarding/OffboardingPolicy.cs:14-22`); si el objetivo es TenantAdmin **activo**, toma el candado del tenant (`ITenantAdminLock`) y exige que quede otro admin activo (`User.LastAdmin`, `OffboardingPolicy.cs:28-31`).
   5. Valida sucesor y ficha antes de tocar nada (`OffboardingPolicy.PrepareDetails`, `OffboardingPolicy.cs:37-59`): sucesor ≠ objetivo, existe, mismo tenant, `IsActive`, actor `TenantEmployee|TenantAdmin` → si no, `User.Successor`. Motivo/nota inválidos → falla.
   6. `User.Offboard` (`User.cs:482-499`): libera prioridad de asiento (`ReleaseSlotPriority`; el **dueño de la cuenta no se puede retirar**: `User.OwnerSlot`, `User.cs:526-527`), `Status=Offboarded`, `IsActive=false`, `RemovedAtUtc`.
   7. Quita el avatar (`RemoveAvatar`).
   8. **Corte de acceso**: `SessionAccessCutoff.ForUserAsync(target, "admin_revoke")` — `Application/Common/SessionAccessCutoff.cs:44-67`.
   9. **Higiene**: `OffboardingHygiene.RunAsync` — purga factores MFA y cancela invitaciones de staff pendientes que envió (las de portal no) — `Application/Offboarding/OffboardingHygiene.cs:14-50`.
   10. Crea `OffboardingRun` con los pasos esperados del registro (`OffboardingParticipants.ExpectedSteps`) — misma transacción que el evento.
   11. Auditoría `AuthAuditAction.UserOffboarded` con `mfaCleared`, `cancelledInvitations`, `successorUserId` (motivo y nota NO van a la bitácora de seguridad; quedan en la ficha).
   12. `SaveChangesAsync` y **después** `PublishAsync(UserOffboardedIntegrationEvent)` (líneas ~312-325). Luego avisos de avatar (`FilesDeletionRequested`, `ProfileUpdated`).
3. **Transporte**: Auth usa outbox durable + transacciones automáticas — `src/Services/Auth/Api/Program.cs:512-517` (`UseTaxVisionRabbitMq`, `PersistMessagesWithSqlServer`, `UseDurableOutboxOnAllSendingEndpoints`, `AutoApplyTransactions`) y ruta `PublishMessage<UserOffboardedIntegrationEvent>().ToRabbitExchange("taxvision-events")` (`Program.cs:538`).

### 2.2 Estados y entidades — Código verificado

- `UserStatus { Active, Deactivated, Offboarded }` — `src/Services/Auth/Domain/Users/UserStatus.cs:7-17`. `IsActive` sigue siendo la compuerta de login/refresh; el estado solo agrega lo terminal.
- `User.MarkDeactivated` no degrada un `Offboarded` (`User.cs:445-453`); `Reactivate` falla con `User.Offboarded` (`User.cs:456-472`).
- `OffboardingRun : TenantEntity` — `src/Services/Auth/Domain/Offboarding/OffboardingRun.cs`: `UserId`, `StartedAtUtc`, `OffboardedByUserId`, `SuccessorUserId`, `Reason`, `ReasonNote`, `IncompleteAtUtc`, pasos `OffboardingRunStep`. Estado derivado `InProgress | Completed | Incomplete` (`OffboardingRun.cs:42-45`). Índice único `(TenantId, UserId)` — `Infrastructure/Persistence/Configurations/OffboardingRunConfiguration.cs:27`.
- Ficha consultable: `GET /auth/users/{userId}/offboarding-status` (`UsersController.cs:140-158`, mismos actores y permiso).
- Pre-flight descubrible: `GET /auth/offboarding/participants` (`Api/Controllers/OffboardingController.cs:27-38`) devuelve servicio, módulo, ruta `offboarding-impact` y si acepta `POST` con email.

### 2.3 Flujo asíncrono (coreografía) — Código verificado

- Evento: `UserOffboardedIntegrationEvent` — `src/BuildingBlocks/Messaging/AuthIntegrationEvents/UserOffboardedIntegrationEvent.cs:11-25`
  `[MessageIdentity("BuildingBlocks.Messaging.AuthIntegrationEvents.UserOffboardedIntegrationEvent")]`; campos `UserId`, `Email`, `ActorType`, `OffboardedByUserId?`, `SuccessorUserId?`, `RemovedAtUtc`; heredados de `IntegrationEvent` (`src/BuildingBlocks/Messaging/IIntegrationEvent.cs:11-20`): `EventId`, `TenantId`, `OccurredOn`, `CorrelationId`.
- Exchange fanout `taxvision-events`; cada servicio .NET escucha su cola `<servicio>-events` enlazada al fanout (p. ej. Auth `auth-tenant-events`, `Program.cs:600-608`); Communication escucha `communication-events` (`src/Services/Communication/src/infrastructure/config.ts:42`) y mapea el tipo CLR al alias `auth.user.offboarded.v1` (`src/infrastructure/rabbit/consumer-runtime.ts:71`).
- Consumidores: 28 servicios, 52 pasos (detalle en `Offboarding_Integraciones.md`). Registro: `src/Services/Auth/Application/Offboarding/OffboardingParticipants.cs:25-144`.
- Eventos derivados que nacen del retiro (todos en `src/BuildingBlocks/Messaging/...`): `CustomerPreparerAssigned/Unassigned`, `CustomerAssignmentsChanged`, `MeetingHostTransferred/Cancelled/Ended` (Node), `AppointmentCancelled`, `ReminderTargetClosed`, `ShareLinkRevoked`, `ConnectorsTenantEmailAccountDisconnected`, `ConnectorsOfficeMailboxReviewRequired`, `CloudConnectionReviewRequired`, `SignaturePreparerChanged`, `SignaturePreparerAttentionRequired`, `CampaignSchedulesPausedOnOffboarding`, `OfficeSecurityCoordinatorVacated`, `TaskAssigned`. Notification los convierte en avisos (`Notification.Application/Consumers/Offboarding/*`).

### 2.4 Acuse por servicio (N/N) — Código verificado

- Contrato: `UserOffboardingStepCompletedIntegrationEvent` (alias estable `auth.user.offboarding_step_completed.v1`) — `src/BuildingBlocks/Messaging/AuthIntegrationEvents/UserOffboardingStepCompletedIntegrationEvent.cs:36-52`.
- .NET: automático. `UseTaxVisionRabbitMq` llama `AcknowledgeOffboardingSteps()` (`src/BuildingBlocks/BuildingBlocks.Web/Hosting/RabbitMqTransportExtensions.cs:37`). La política `OffboardingStepAcknowledgementPolicy` (`.../Hosting/OffboardingStepAcknowledgements.cs:87-115`) inserta, como **primer postprocesador** de toda cadena de `UserOffboardedIntegrationEvent`, un frame que publica un acuse por clase handler (`Service` = segundo tramo del ensamblado, `Step` = nombre de la clase). Corre tras todos los handlers y antes del commit del middleware transaccional: con outbox queda en la misma transacción. Si un handler lanza, no sale ningún acuse de esa cadena.
- Node: a mano, al final del consumer — `src/Services/Communication/src/application/event-handlers/offboarding-consumers.ts:147-160`.
- Auth registra el acuse: `OffboardingStepCompletedConsumer` (`Application/Offboarding/OffboardingStepCompletedConsumer.cs:15-60`), idempotente por `Service+Step`; corrida inexistente o paso no esperado → log y descarte.
- Barrido: `OffboardingRunsSweepJob` (`Api/Jobs/OffboardingRunsSweepJob.cs`, `PeriodicTimer` cada `IntervalMinutes`=5) → `OffboardingRunSweeper` con lock SQL (`Application/Offboarding/OffboardingRunSweeper.cs:83-113`) → `MarkOffboardingRunIncompleteCommand` por corrida vencida (`TimeoutMinutes`=30, `OffboardingTrackingOptions.cs:118-145`) → guarda y publica `UserOffboardingIncompleteIntegrationEvent` con los pasos pendientes y el nombre visible del módulo (`MarkOffboardingRunIncomplete.cs:160-205`). Solo avisa **una vez** por corrida; un acuse tardío convierte `Incomplete` en `Completed`.

### 2.5 Front CRM — Documentación histórica (archivos confirmados, lógica no releída)

- `src/app/features/user-management/ui/offboard-dialog/*` (diálogo con impacto, sucesor, motivo, nota), `ui/offboard-status-dialog/*` ("Handover status"), `data-access/offboard-impact.service.ts` (arma la lista desde `GET /auth/offboarding/participants`, renglón genérico si falta texto). Abre con `/company/users?handover=<userId>` (fuente: `Offboard-Revision-2026-11-03.md` §8).

---

## 3. Seguridad inmediata vs limpieza eventual

### 3.1 Qué se corta al instante (síncrono) — Código verificado

| Mecanismo | Dónde | Efecto |
|---|---|---|
| `User.IsActive=false` + `Status=Offboarded` | `User.cs:482-499` | Login y refresh fallan: `RefreshAccessToken.cs:132` (`user is null || !user.IsActive`). Pull de permisos falla: `GetPermissionsSnapshotQuery.cs:45-47` (`Auth.UserInactive`). |
| Denylist del `sid` en Redis, TTL 20 min | `SessionAccessCutoff.cs:41, 60-64` | Invalida access tokens ya emitidos (vida 15 min: `Api/appsettings.json:17`, `JwtTokenGenerator.cs:24`). |
| Middleware de denylist en los 27 .NET | `BuildingBlocks.Web/Session/SessionDenylistMiddleware.cs:18-78`, registrado en cada `Program.cs` (p. ej. `Auth/Api/Program.cs:661`) | 401 `Auth.SessionRevoked`. M2M (sin `sid`) no se chequea. |
| Communication HTTP y handshake de socket | `src/infrastructure/jwks/jwt-verifier.ts:94-114`; usado en `api/http/plugins/auth.plugin.ts:57` y `infrastructure/socket/build-io.ts:139` | Rechaza token denylisteado; **fail-closed por defecto** (`config.ts:23-32`). |
| Aviso Pub/Sub `auth:session-revoked` | `SessionAccessCutoff.cs:65-66` → `Communication/src/infrastructure/redis/session-denylist-watcher.ts:23-60` | Emite `session.revoked` y expulsa sockets (`SocketIoSessionTerminator.disconnectUser`, `socket-session-terminator.ts:9-15`, cableado en `main.ts:513`). Best-effort. |
| Cierre del socket al `exp` | `Communication/src/api/socket/handlers/session-handlers.ts:18-25, 49-72` | Aunque se pierda el Pub/Sub, el socket se cierra al vencer el token (+5 s). |
| Revocación de sesiones y refresh en BD | `SessionAccessCutoff.cs:48` | Autoritativa; persiste el motivo. |
| MFA purgado, invitaciones canceladas | `OffboardingHygiene.cs:14-50` | Sin segundo factor ni invitaciones vivas a su nombre. |

### 3.2 Qué queda para la limpieza eventual (asíncrono)

Proyecciones de permisos (`IsActive=false` en ~25 servicios), directorios, trabajo activo (clientes, tareas, citas, reuniones, borradores, firmas, formularios, campañas), credenciales de terceros (buzones personales, feed de calendario, share links, push), avisos privados (recordatorios). Ver matriz.

### 3.3 Ventanas de acceso indebido posibles

| # | Ventana | Evidencia | Tamaño | Mitigación actual |
|---|---|---|---|---|
| V1 | **Redis caído al chequear la denylist en .NET** → `FailOpen` por defecto deja pasar el token vigente. | `SessionDenylistOptions.cs:35` (`FailureMode = FailOpen`), `SessionDenylistMiddleware.cs:43-59`. | Hasta el `exp` del token (≤15 min). | Métrica `authz.session_denylist_unavailable`; configurable a `FailClosed` por servicio. Communication es fail-closed. |
| V2 | **Pub/Sub perdido**: socket abierto sin expulsión. | `session-denylist-watcher.ts:31-33` (best-effort). | Hasta el `exp` del token (+5 s). | Cierre por `exp` en `session-handlers.ts`. |
| V3 | **Acciones "en su nombre" que no usan su token**: agendados de campañas (`RunAs`), buzón personal que sigue poleando, push al teléfono, correos de audiencias por permiso, recordatorios, enlaces públicos de intake con responsable = retirado. | Consumers de Campaigns, Connectors, Notification, Reminder, Tasks.ClientForms (ver matriz). | Hasta que cada consumer procese (segundos en condiciones normales; 49/49 en ~10 s en vivo el 2026-11-04, Documentación histórica). Si un consumer está caído: hasta que vuelva o se drene la DLQ. | Acuse N/N + aviso a los 30 min. |
| V4 | **Node: caída del proceso a mitad del consumer**. El inbox marca el `EventId` antes del handler y solo lo desmarca en el `catch`; si el proceso muere, la reentrega se descarta como duplicado y el paso nunca acusa. | `consumer-runtime.ts` (`tryMarkProcessed` antes del handler, `unmark` solo en `catch`); `prisma-processed-event-store.ts:13-31`. | Indefinida (trabajo a medias en Communication). | El run queda `Incomplete` y se avisa a los 30 min; no hay re-ejecución automática. Análisis de código, **no reproducido**. |
| V5 | **Proyección de permisos reactivada por un `UserRolesChanged` posterior**: `ApplyIfNewer` pone `IsActive = true`. `AssignUserRolesHandler` y `SetUserPermissionOverrides` no rechazan a un usuario retirado. | `Billing.Domain/Permissions/AuthzUserPermissionsProjection.cs:51-63` (mismo patrón en los demás servicios); `UserManagementCommands.cs:428-520` (sin chequeo de `Status`). | Bajo: el retirado no puede obtener token (Auth `IsActive=false`). Es defensa en profundidad rota, no acceso directo. | El reconciliador de Auth solo republica activos (Documentación histórica, memoria 24-09). **Pendiente de confirmar** si la UI permite cambiar roles a un retirado (memoria: el menú se oculta para filas `removed`). |
| V6 | Caché de permisos en memoria 30 s por servicio. | `ProjectionPermissionsSource.cs:63-73`. | ≤30 s, pero el token ya está denylisteado → sin efecto práctico salvo V1. | — |
| V7 | El denylist y el aviso Pub/Sub se aplican **antes** del `SaveChanges`. Si el commit falla, el usuario queda deslogueado pero sigue `Active` y puede volver a entrar. | `UserManagementCommands.cs` (orden: `accessCutoff` → `hygiene` → `SaveChangesAsync`). | Falla segura hacia seguridad; el admin ve el error y reintenta. | — |

---

## 4. Consistencia

### 4.1 Modelo — Código verificado

- **Coreografía** con **seguimiento orquestado ligero**: Auth no ordena a nadie; publica un hecho y lleva la cuenta de acuses. No es saga con compensación: el retiro es terminal y no se deshace.
- **Orden**: ningún consumer asume orden respecto a otro. Ejemplos explícitos en código: `ClientRequestsUserOffboardedConsumer` salta a un asignado que todavía es el retirado (comentario en su cabecera); `CalendarUserOffboardedConsumer` dice "sin depender del orden frente al retiro en Reminder"; `UserEmailDirectoryOffboardingConsumer` usa `SetActive(false, evt.OccurredOn)` para ignorar eventos más viejos (`Notification.Application/Consumers/UserEmailDirectoryConsumers.cs:116-148`).
- **Dentro de un servicio .NET**: Wolverine en modo clásico arma **una cadena** con todos los handlers del mensaje en serie; si uno falla, se reintenta la cadena entera (`OffboardingStepAcknowledgements.cs:19-23`). Por eso cada handler debe ser idempotente: los que ya terminaron vuelven a correr.
- **Consumer que solo delega**: si el consumer reenvía un comando local, el acuse automático saldría antes del trabajo. Por eso se marca con `[AcknowledgesOffboardingLater]` (`BuildingBlocks/Messaging/AuthIntegrationEvents/OffboardingStepNames.cs`): la política no acusa por él y el handler que termina publica `OffboardingStepNames.CompletedBy(typeof(<Consumer>), …)` en su misma unidad con outbox, con el mismo `Service+Step`. Caso real: `ConnectorsUserOffboardedConsumer` → `PurgeOffboardedMailboxesHandler`, que acusa también cuando el empleado no tenía buzones personales.
- **Duplicados**: idempotencia por estado ("lo resuelto sale del filtro"): consultas que solo devuelven lo que todavía apunta al retirado (p. ej. `ListByAssignedPreparerAsync`, `ListOpenByAuthorAsync`, `ListActiveByCreatorAsync`). Auth: `Offboard` idempotente; acuses idempotentes por `Service+Step`. Node: inbox `ProcessedEvent` por `(EventId, Source)`.
- **Reintentos**: .NET `ApplyStandardFailurePolicies` — permanentes (`JsonException`, `ArgumentException`, `FormatException`, `NotSupportedException`) directo a DLQ; resto 1s/5s/15s y DLQ (`src/BuildingBlocks/Messaging/WolverineFailurePolicies.cs:61-117`). Node: mismos cooldowns con colas de espera y `nack(requeue=false)` a la DLQ (`consumer-runtime.ts`, `rabbit-connection.ts:52`).
- **Publicación fiable**: outbox durable + confirmaciones del broker en los 27 .NET (`RabbitMqTransportExtensions.cs:38-45`; todos los `Program.cs` con `UseDurableOutboxOnAllSendingEndpoints` y `AutoApplyTransactions`, ContentScanner no usa bus). Node: outbox Prisma (`publisher.enqueue`).
- **CorrelationId**: Auth publica con `audit.CorrelationId`; los consumers hacen `correlation.Push(msg.CorrelationId ?? EventId)` y lo propagan a eventos derivados y al acuse (`OffboardingStepAcknowledger.PublishAsync`, `OffboardingStepAcknowledgements.cs:57-83`).
- **Consumer caído**: los mensajes esperan en su cola durable; al volver, procesa. Si no vuelve en 30 min → aviso `auth.offboarding.incomplete` (plantilla Scribe, Documentación histórica: v25).
- **Recuperación/reconciliación**: no existe re-ejecución automática del handover ni endpoint para "reintentar el retiro". Las reconciliaciones de directorio (p. ej. Customer `EmployeeDirectoryReconciliation.cs:59-63`) **solo marcan inactivo** y registran un warning si Auth dice retirado y el directorio no; no reasignan trabajo. Drenado de DLQ manual: README §48.
- **Handover status**: `OffboardingRun` (N/N), consultable y visible en el CRM.

### 4.2 Problemas de consistencia detectados

1. **Node sin transacción única**: cada paso del consumer de Communication persiste por separado; reintentos retoman, pero una caída del proceso deja el inbox marcado (V4).
2. **Aviso de incompleto único**: tras el primer aviso no hay recordatorio ni escalado (`OffboardingRun.MarkIncomplete`, `OffboardingRun.cs:105-118`).
3. **Volumen**: cada retiro produce 52 acuses que el fanout entrega a ~29 colas; los servicios que no los manejan los descartan (ruido "No known handler", Documentación histórica §8). Aceptable hoy; crece con cada paso.
4. **Carrera de dos retiros del mismo usuario**: el índice único de `OffboardingRun` impide dos corridas/eventos; la respuesta HTTP del segundo no está probada (**Pendiente de confirmar**).

---

## 5. Diferencias entre operaciones

| Operación | Dónde | Reversible | Quién | Qué hace | Evento |
|---|---|---|---|---|---|
| **Desactivar (suspender)** | `DeactivateUserHandler`, `UserManagementCommands.cs:38-122` | Sí (`ReactivateUserHandler`, valida asiento) | `users.manage`; un empleado no suspende a un admin (`User.Hierarchy`); nunca el último admin | `IsActive=false`, `Status=Deactivated`, corte de acceso, auditoría. No reasigna, no purga MFA. | `UserDeactivatedIntegrationEvent` |
| **Retirar (offboard)** | `OffboardUserHandler` | **No** (terminal) | `users.manage` + TenantAdmin/PlatformAdmin; no a sí mismo, no al dueño, no al último admin activo, no a un cliente de portal | Todo lo de suspender + MFA, invitaciones, avatar, `OffboardingRun`, ficha | `UserOffboardedIntegrationEvent` (Auth **no** emite también `UserDeactivated`, decisión D-1) |
| **Revocar sesiones** | `SessionAccessCutoff` | — | Parte de los anteriores (y de suspensión de tenant) | Denylist + Pub/Sub + BD | Pub/Sub `auth:session-revoked` |
| **Reasignar** | Consumers de cada servicio | Manual | Sistema, con el sucesor del evento | Trabajo activo → sucesor; sin sucesor: desasignar, pausar, cancelar o pasar a quien retiró (según servicio) | Eventos derivados por servicio |
| **Revocar credenciales** | Consumers (Connectors, Calendar feed, CloudStorage share links, push) | No | Sistema | Desconecta/purga/archiva | Derivados |
| **Anonimizar** | **No implementado** | — | — | Plan Fase 4 (tombstone, candado de retención): `DEVS/Futuras Implementaciones/Fase4-Anonymize-Retiro-TenantEmployees-PLAN.md` (Documentación histórica) | — |
| **Conservar** | `[UserReference(Provenance)]` | — | — | Autoría, auditoría, cadena HMAC de firmas, identidad 8879, runs enviados, `CreatedByUserId`: nunca se tocan | — |
| **Re-invitar al mismo email** | Auth (D-8) | — | Admin | Cuenta nueva; el Guid viejo queda como registro. Pruebas `OffboardedEmailReuseTests` | — |

Regla del dominio (Código verificado en `UserReferenceContractTests`): **retiro ⊇ suspensión** — todo servicio que maneja `UserDeactivated` debe manejar `UserOffboarded`.

---

## 6. Evidencia (índice)

- Endpoint y handler: `src/Services/Auth/Api/Controllers/UsersController.cs:103-158`; `src/Services/Auth/Application/Users/Commands/UserManagementCommands.cs:216-357`.
- Dominio: `src/Services/Auth/Domain/Users/User.cs:384-499, 526-527`; `Domain/Users/UserStatus.cs`; `Domain/Offboarding/OffboardingPolicy.cs`; `Domain/Offboarding/OffboardingRun.cs`.
- Corte: `src/Services/Auth/Application/Common/SessionAccessCutoff.cs`; `src/BuildingBlocks/BuildingBlocks.Web/Session/SessionDenylistMiddleware.cs`; `SessionDenylistOptions.cs`; Communication `jwt-verifier.ts`, `session-denylist-watcher.ts`, `socket-session-terminator.ts`, `session-handlers.ts`.
- Seguimiento: `src/Services/Auth/Application/Offboarding/{OffboardingParticipants, OffboardingStepCompletedConsumer, OffboardingRunSweeper, MarkOffboardingRunIncomplete, OffboardingTrackingOptions, OffboardingHygiene, GetOffboardingStatus}.cs`; `Api/Jobs/OffboardingRunsSweepJob.cs`; `Api/Controllers/OffboardingController.cs`.
- Contratos: `src/BuildingBlocks/Messaging/AuthIntegrationEvents/UserOffboarded*.cs`.
- Acuse .NET: `src/BuildingBlocks/BuildingBlocks.Web/Hosting/OffboardingStepAcknowledgements.cs`, `RabbitMqTransportExtensions.cs`.
- Guardarraíl: `src/BuildingBlocks/Domain/UserReferenceAttribute.cs`; `deploy/tests/TaxVision.BuildingBlocks.Tests/Offboarding/{UserReferenceContractTests, OffboardingParticipantsContractTests, OffboardingStepAcknowledgementPolicyTests}.cs`.
- Permisos: `src/BuildingBlocks/BuildingBlocks.Web/ActorTypeAuthorization/ProjectionPermissionsSource.cs`; `Services/Auth/Application/Permissions/Internal/Queries/GetPermissionsSnapshotQuery.cs:45-47`.
- Documentación histórica: memoria `project_tenant_employee_offboarding_3_2_analysis.md`; `Implementaciones/DEVS/Futuras Implementaciones/Offboard-Revision-2026-11-03.md` (§0–§8); `Fase4-Anonymize-Retiro-TenantEmployees-PLAN.md`; `DEVS/Guia_Creacion_Microservicio.md:1252-1256, 1334` (DoD y guardarraíl #38).

---

## 7. Problemas

1. **P-1 Inbox de Node sin recuperación ante caída del proceso** (V4). Análisis de código, no reproducido.
2. **P-2 `ApplyIfNewer` reactiva proyecciones** y Auth acepta cambios de roles/overrides sobre un retirado (V5). Código verificado; impacto bajo.
3. **P-3 Sin re-ejecución del handover**: el aviso de incompleto obliga a intervención manual (DLQ o arreglo a mano); no hay "reintentar retiro" ni reconciliación de trabajo activo.
4. **P-4 `FailOpen` por defecto** en la denylist de los .NET (V1). Decisión de riesgo documentada en el código.
5. **P-5 Aviso de incompleto una sola vez**, sin escalado.
6. **P-6 PII en el evento**: `Email` viaja en el fanout y queda en tablas de outbox/inbox de todos los servicios. Se usa en Notification (directorio por email) y en el pre-flight por email (CloudStorage, Connectors).
7. **P-7 Huecos de pruebas** de punta a punta (ver §9).
8. **P-8 Pendiente declarado**: migraciones del retiro sin desplegar en producción (Documentación histórica, memoria; **Pendiente de confirmar** contra prod).

---

## 8. Buenas prácticas observadas (para copiar)

- **Cortar el acceso primero, limpiar después**, y en el orden denylist → aviso → BD (`SessionAccessCutoff.cs:22-25`).
- **Validar todo antes de mutar** (sucesor, motivo, nota) para no dejar un retiro a medias (`OffboardingPolicy.PrepareDetails`).
- **Guardar antes de publicar** y outbox durable.
- **Evento nuevo en vez de modificar uno vivo** (no se tocó `UserDeactivated`; comentario en el contrato).
- **Clasificar cada referencia a un empleado** (`Provenance | ActiveWork | Credential | Private | Projection`) y que un test lo exija.
- **Registro de participantes alineado por test** con los consumers reales y los pre-flight.
- **Acuse automático por política** (los consumers no tienen que acordarse).
- **Idempotencia por estado** + keyset por lotes (Customer `SweepAsync` avanza siempre por `Id`, `CustomerUserOffboardedConsumer.cs:95-123`).
- **Sucesor revalidado localmente** en cada servicio (activo, mismo tenant, permiso que exige la acción) y fallback seguro (desasignar, pausar, cancelar con aviso, pasar a quien retiró).
- **Fail-closed** donde importa: pull de permisos rechaza inactivos; Communication rechaza si no puede leer la denylist.
- **Avisos a la oficina** cuando algo no se puede resolver solo (WISP vacante, preparador 8879, buzones/nube de oficina para revisar, campañas pausadas).

---

## 9. Pruebas

### 9.1 Existentes (nombres y rutas) — Código verificado

Auth (`deploy/tests/TaxVision.Auth.Tests/`):
- `Application/OffboardUserHandlerPathsTests.cs`: `Nobody_removes_their_own_account`, `A_user_of_another_office_does_not_exist`, `Offboarding_twice_publishes_only_once`, `An_invalid_successor_stops_the_offboarding_before_touching_the_user` (self/missing/foreign/inactive/portal), `A_bad_reason_is_refused_after_the_successor_check`, `A_valid_offboarding_cuts_access_saves_once_and_announces_after_saving`, `The_record_keeps_the_reason_and_the_note_without_blanks`, `With_an_avatar_the_notices_go_in_order`, `Mfa_and_cancelled_invitations_show_in_the_audit_details`.
- `Application/UserManagementCommandsTests.cs`: `OffboardUserHandler_refuses_to_remove_a_portal_client`, `..._retires_a_suspended_admin_when_a_single_active_admin_remains`, `..._refuses_to_remove_the_last_active_admin`, `..._refuses_the_account_owner_and_keeps_the_state`, `..._purges_mfa_and_cancels_the_pending_staff_invitations_the_leaver_sent`, `..._keeps_the_offboarding_record_with_actor_successor_and_reason`, `..._rejects_a_bad_note_before_touching_the_user`, avatar (2).
- `Domain/UserLifecycleTests.cs`: `Offboard_is_terminal`, `Offboard_is_idempotent`, `Reactivate_Offboarded_ReturnsFailure`, `Offboard_BySelf_ReturnsFailure`, `Offboard_Owner_ReturnsOwnerSlotFailureAndKeepsState`.
- `Offboarding/OffboardingRunTrackingTests.cs` (16 casos: pasos, idempotencia de acuses, paso inesperado, plazo, aviso guardado antes de publicar, opciones, round-trip).
- `Persistence/OffboardedEmailReuseTests.cs` (5), `Integration/LastAdminGuardConcurrencyIntegrationTests.cs::TwoOffboardsOfDifferentAdmins_InParallel_LeavesOneActiveAdmin`.
- Arquitectura: `HandlerSizeFitnessTests` (incluye `OffboardUserHandler`), `AuthHandlersCompileTests`, `AuthActorTypeArchitectureTests` (toda acción declara `AllowActorTypes`).

BuildingBlocks (`deploy/tests/TaxVision.BuildingBlocks.Tests/`):
- `Offboarding/UserReferenceContractTests.cs` (12 casos, incluye `Every_staff_user_reference_in_domain_declares_what_offboarding_does_with_it`, `Every_service_that_handles_suspension_also_handles_offboarding`, `Pending_annotation_list_only_shrinks`).
- `Offboarding/OffboardingParticipantsContractTests.cs` (7 casos, incluye `The_registry_lists_exactly_the_real_offboarding_consumers`, `..._offboarding_impact_endpoints`, `Every_service_with_active_work_credentials_or_private_data_exposes_offboarding_impact`).
- `Offboarding/OffboardingStepAcknowledgementPolicyTests.cs`: `Publishes_one_acknowledgement_per_handler_of_the_offboarded_event`, `Does_not_acknowledge_when_a_handler_of_the_chain_fails`, `Leaves_other_message_types_alone`.
- `Messaging/NodeInteropContractTests.cs` (alias `auth.user.offboarding_step_completed.v1`), `Session/SessionDenylistMiddlewareTests.cs`, `Session/SessionDenylistReaderTests.cs`.

Servicios: un archivo de pruebas por consumer (ver columna "Test" de la matriz). Communication: `tests/unit/offboarding-consumers.test.ts` (11), `offboarding-handover.test.ts` (22), `meeting-offboard-handover.test.ts` (8), `group-administration.test.ts`.

### 9.2 Huecos frente a la lista pedida

| Caso | Estado | Evidencia |
|---|---|---|
| Admin autorizado | Cubierto en handler (unitario). | `A_valid_offboarding_cuts_access_saves_once_and_announces_after_saving`. |
| Actor no autorizado (empleado con `users.manage`, cliente de portal como actor) | **Hueco**: solo existe el fitness genérico de que el atributo existe; no hay prueba de 403 en el endpoint. | `AuthActorTypeArchitectureTests.Controller_actions_should_declare_AllowActorTypes`. |
| Empleado de otro tenant | Cubierto (objetivo y sucesor). | `A_user_of_another_office_does_not_exist`; caso `foreign`. |
| Ya desvinculado / command repetido | Cubierto. | `Offboarding_twice_publishes_only_once`, `Offboard_is_idempotent`. |
| Dos comandos simultáneos del mismo usuario | **Hueco** (índice único lo protege; respuesta no probada). | `OffboardingRunConfiguration.cs:27`. |
| Evento repetido | Cubierto en la mayoría de consumers (`Redelivery_*`, `Reprocessing_does_not_notify_twice`, `is idempotent: a second run changes nothing`); **no uniforme** (no todos los consumers tienen un caso explícito). | Matriz. |
| Consumer caído | **Hueco de integración**: solo unitario del barrido. | `Flagging_an_overdue_run_saves_before_publishing_the_notice_with_human_module_names`. |
| Reintento tras fallo | Cubierto (sin acuse si falla). | `Does_not_acknowledge_when_a_handler_of_the_chain_fails`; Node `does not acknowledge when a step fails`. |
| Fuera de orden | Parcial (Notification directorio, Signature/ClientRequests sin orden). **Hueco**: `UserRolesChanged` después del retiro (V5). | `Replayed_registered_after_offboarding_does_not_reactivate` (`TaxVision.Notification.Tests/Lifecycle/UserEmailDirectoryConsumersTests.cs:57`). |
| Sesión activa / JWT vigente tras retirar | Parcial: el handler llama al corte (dobles); el middleware tiene pruebas propias. **Hueco**: ninguna prueba que encadene "retirar → request con el token viejo → 401" ni en Communication "retirar → socket expulsado". | `SessionDenylistMiddlewareTests.cs`. |
| Acceso tras desvincular (refresh/login) | **Hueco específico de retiro** (lógica compartida con suspensión, `RefreshAccessToken.cs:132`). | — |
| Proyección actualizada | Cubierto en todos los servicios con proyección de permisos (`UserLifecyclePermissionsProjectionConsumerTests`). | Matriz. |
| Histórico conservado | Parcial (Signature `A_preparer_signature_already_given_is_never_touched`, Calendar "past/cancelled no se tocan", Customer `CreatedByUserId`). | Matriz. |
| Reconciliation posterior | Parcial: solo directorio (Customer). No existe reconciliación de handover. | `EmployeeDirectoryReconciliationTests.cs`. |
| Aislamiento de tenants en consumers | Parcial: sucesor de otro tenant rechazado en Auth; los consumers revalidan tenant (Customer, Communication) pero no todos tienen caso. | `CustomerUserOffboardedConsumer.cs:81-92`. |
| E2E del retiro completo | **Descartado por el dueño** (nightly excesivo). Verificación manual en vivo 49/49 (2026-11-04, Documentación histórica). | `Offboard-Revision-2026-11-03.md` §8. |

---

## 10. Recomendación

1. **Re-ejecución segura del handover**: un comando de Auth "republicar el retiro" (mismo `UserId`, `EventId` nuevo, `CorrelationId` del run) que los consumers ya soportan por ser idempotentes. Hoy la única vía es la DLQ.
2. **Inbox de Node**: marcar el `EventId` en la misma unidad que el trabajo, o con estado "en curso" que expire, para que una caída no convierta la reentrega en duplicado.
3. **Auth rechaza mutaciones de acceso sobre un retirado** (roles, overrides, reproyección) y las proyecciones no reactivan con `ApplyIfNewer` si el último evento de ciclo de vida fue un retiro.
4. **Escalado del aviso de incompleto** (segundo aviso o tablero de runs incompletos).
5. **Decisión pendiente**: `FailClosed` de la denylist en servicios con datos sensibles (Customer, CloudStorage, Signature).
6. **Pruebas**: endpoint 403 por actor, carrera de dos retiros, "token viejo → 401" y "socket expulsado" a nivel de integración, `UserRolesChanged` posterior.

## 11. Riesgos

- Ventanas V1–V7 (§3.3). La más relevante para datos de clientes: V3 (acciones en nombre del retirado mientras un consumer está caído).
- Crecimiento: cada módulo nuevo con datos de empleados agrega pasos; el fitness test obliga a registrarlos, pero también a que el módulo exista y responda en <30 min.
- Dependencia de Redis para el corte inmediato (denylist y Pub/Sub).
- Migraciones del retiro en producción: **Pendiente de confirmar** (memoria dice "falta desplegar").

## 12. Dependencias

- Auth (dueño), RabbitMQ (fanout `taxvision-events`, DLQ `wolverine-dead-letter-queue`), Redis (denylist `taxvision:auth:denylist:sid:*`, canal `auth:session-revoked`), SQL Server (outbox/inbox Wolverine, `OffboardingRuns`), Notification + Scribe (avisos), CRM (diálogo y Handover status).
- Investigaciones hermanas: `Session_Denylist.md`, `Auth_RBAC.md`, `Proyecciones.md`, `Reconciliation.md`, `Wolverine_RabbitMQ.md`.

---

## 13. Material para la guía

**Regla de oro**: *si tu servicio guarda un Guid que apunta a un empleado, decide hoy qué le pasa cuando la oficina lo retira.*

1. El retiro lo hace **Auth**. Tu servicio nunca corta el acceso: eso ya pasó cuando te llega el evento. Tu trabajo es dejar los datos coherentes.
2. Escuchas **un** evento: `UserOffboardedIntegrationEvent` (alias Node `auth.user.offboarded.v1`). No inventes otro.
3. Clasifica cada referencia con `[UserReference(...)]`:
   - `Provenance` → no se toca (autoría, auditoría, legal).
   - `ActiveWork` → al sucesor validado localmente; sin sucesor, cerrar/pausar/rutar a la oficina **con aviso**.
   - `Credential` → revocar y purgar.
   - `Private` → cancelar o archivar.
   - `Projection` → marcar inactiva.
4. **Retiro ⊇ suspensión**: si manejas `UserDeactivated`, manejas `UserOffboarded`.
5. Consumer idempotente, sin orden asumido, por lotes, que guarde antes de publicar y lance ante dependencias caídas (Wolverine reintenta).
6. Regístrate en `OffboardingParticipants` (pasos y, si tienes trabajo activo/credenciales/privado, `ImpactPath`). El acuse .NET es automático; Node lo encola a mano.
7. Expón `GET /<svc>/offboarding-impact/{userId}` con `users.manage` (y `POST` con email si identificas por email; nunca PII en la URL).
8. Pruebas mínimas: con sucesor, sin sucesor, sucesor inválido (otro tenant/inactivo), reentrega, histórico intacto, conteo del pre-flight, proyección inactiva.
9. Diferencias que el desarrollador debe tener claras: suspender (reversible) ≠ retirar (terminal) ≠ anonimizar (no existe) ≠ borrar (prohibido).
10. Ventanas conocidas a documentar en cada servicio: qué sigue pasando "en nombre" del retirado hasta que tu consumer corre.
