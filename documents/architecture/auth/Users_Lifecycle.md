# Auth — Ciclo de vida del usuario (empleado del tenant)

Fecha: 2026-09-24

Documenta la máquina de estados de un `User` (empleado del tenant) y los endpoints/eventos que la mueven. Es el punto 3.2 del backlog "Correcciones y Mejoras CRM" (retiro seguro de TenantEmployees). Rutas bajo el Gateway con prefijo `/auth`. Tenant/actor SIEMPRE del JWT, nunca de body/query. Todas las transiciones son idempotentes y auditadas.

Regla de oro (JML — Joiner/Mover/Leaver): **block sign-in first**. `deactivate` ≠ `offboard` ≠ `anonymize`. Nunca hay hard-delete: la ley de impuestos (IRS §6107/§6695(g)/§6112/§6109) obliga a conservar el trabajo del preparador 3–7 años.

## Máquina de estados

```
                 deactivate                 offboard (handover)
   ┌────────┐ ───────────────▶ ┌─────────────┐ ───────────────▶ ┌────────────┐ ──(Fase 4)──▶ ┌────────────┐
   │ Active │                  │ Deactivated │                  │ Offboarded │               │ Anonymized │
   └────────┘ ◀─────────────── └─────────────┘                  └────────────┘               └────────────┘
                 reactivate            │                               ▲                         (terminal,
                                       └──────── offboard ─────────────┘                        PII destruido)
```

- **Active → Deactivated**: reversible. Corta el acceso (IsActive=false), revoca sesiones y denylist de tokens ~20 min.
- **Deactivated → Active**: `reactivate`. Bloqueado si el usuario ya está **Offboarded** (terminal).
- **Active/Deactivated → Offboarded**: retiro terminal del tenant, con **handover obligatorio** del trabajo activo al sucesor.
- **Offboarded → Anonymized**: **Fase 4 (PLANIFICADA, no implementada)** — destruye el PII conservando el Guid, bajo candado de retención IRS/legal-hold.

`User` conserva `IsActive` (booleano legado, invariante `Offboarded/Anonymized ⇒ IsActive=false`) además de `Status { Active, Deactivated, Offboarded }` (+ `Anonymized` en Fase 4) y `RemovedAtUtc`.

## Endpoints (`/auth/users/*`)

| Método / ruta | Permiso | Actores | Body | Resp | Descripción |
|---|---|---|---|---|---|
| `GET /auth/users?page&size&search&isActive&customerId` | `users.view` | Employee / TenantAdmin / PlatformAdmin | — | 200 | Listado paginado |
| `GET /auth/users/{userId}` | `users.view` | Employee / TenantAdmin / PlatformAdmin | — | 200 | Detalle |
| `PATCH /auth/users/{userId}/deactivate` | `users.manage` | Employee / TenantAdmin / PlatformAdmin | — | 204 | Desactivar (reversible) |
| `PATCH /auth/users/{userId}/reactivate` | `users.manage` | Employee / TenantAdmin / PlatformAdmin | — | 204 | Reactivar (falla si Offboarded) |
| `POST /auth/users/{userId}/offboard` | `users.manage` | **TenantAdmin / PlatformAdmin** | `{ "successorUserId": <guid?> }` | 204 | Retiro terminal + handover |
| `POST /auth/users/{userId}/anonymize` | `users.manage` | **TenantAdmin / PlatformAdmin** | *(por definir)* | — | **PLANIFICADO (Fase 4)** — aún NO existe |

Notas de autorización:
- `offboard` es **más estricto** que deactivate: excluye `TenantEmployee` (solo admins).
- Ambas escrituras comparten el permiso `users.manage` (no hay permiso nuevo por acción — decisión deliberada para evitar drift de catálogo/migración HasData → 503 load-shedding).
- Guardas comunes: **self-block** (no puedes retirarte/desactivarte a ti mismo) y **último admin activo** (no puedes dejar el tenant sin admin).

## Comportamiento por transición

### `deactivate` (`DeactivateUserCommand`)
IsActive=false, Status=Deactivated, revoca sesiones + denylist de tokens (~20 min), publica `UserDeactivatedIntegrationEvent`, audita. **Reversible.** No degrada un usuario ya Offboarded.

### `reactivate` (`ReactivateUserCommand`)
Status=Active, IsActive=true, publica `UserReactivatedIntegrationEvent`, audita. **Bloquea** si el usuario está Offboarded (terminal).

### `offboard` (`OffboardUserCommand`)
Terminal e idempotente. Corta el acceso (reusa el path de deactivate: sesiones + denylist), **destruye credenciales/MFA/refresh tokens**, fija Status=Offboarded + RemovedAtUtc, publica `UserOffboardedIntegrationEvent` (con `SuccessorUserId`), audita `auth.user.offboarded`. El `successorUserId` es a quién se reasigna el trabajo activo; `null` = se desasigna / va a oficina.

### `anonymize` — Fase 4 (PLANIFICADA)
Estado terminal `Anonymized`. Requiere estar **Offboarded** primero. Sobreescribe el PII (Email/Nombre/Teléfono → `"Former employee"`, borra secretos), **conserva el Guid**, y **nunca** toca auditoría/legal. Solo si pasa el **candado de retención** (RemovedAtUtc + 7 años, sin legal-hold) o un override legal de PlatformAdmin auditado. Técnica elegida: **tombstone/sobreescritura, no crypto-shredding**. Plan por fases completo fuera del repo: `DEVS/Futuras Implementaciones/Fase4-Anonymize-Retiro-TenantEmployees-PLAN.md`.

## Eventos de integración (fanout `taxvision-events`)

Auth publica; cada servicio consume lo que le toca desde su cola `<svc>-events` (auto-wire, sin cambio de binding). Contratos en `BuildingBlocks.Messaging.AuthIntegrationEvents`. `TenantId` heredado de `IntegrationEvent`.

| Evento | Campos propios | Publicado en |
|---|---|---|
| `UserDeactivatedIntegrationEvent` | `UserId, Email, ActorType` | deactivate |
| `UserReactivatedIntegrationEvent` | `UserId, Email, ActorType` | reactivate |
| `UserOffboardedIntegrationEvent` | `UserId, Email, ActorType, OffboardedByUserId?, SuccessorUserId?, RemovedAtUtc` | offboard |
| `UserAnonymizedIntegrationEvent` | `UserId, ActorType, AnonymizedAtUtc` *(Fase 4)* | anonymize *(planificado)* |

> Regla de evolución: nunca agregar un campo `required` a un evento vivo (rompe deserialización → DLQ). Un consumidor nuevo o un evento nuevo = 0 back-compat. Los tipos no manejados se descartan (no DLQ).

## Mapa de consumidores (qué hace cada servicio)

**`UserDeactivated` / `UserReactivated`** — flip de acceso:
- **RBAC IsActive (8 servicios)**: Tasks, Calendar, Signature, Subscription, Customer, Connectors, Correspondence, CloudStorage → `UserPermissionsProjection.IsActive` false/true (enforcement de `perm_v` fail-closed). *(Nota: el reconciliador de Auth solo republica `UserRolesChanged` para usuarios ACTIVOS, así que no revive un MarkInactive.)*
- **Subscription**: libera el asiento comprado (`UserLifecycleSeatConsumer`).
- **Customer**: `TenantEmployeeDirectoryEntry` MarkInactive/MarkActive (elegibilidad de preparador).
- **Communication**: marca inactivas sus proyecciones (userPermissions/userDirectory/portalAccounts).

**`UserOffboarded`** — todo lo de deactivate **más** el handover del trabajo activo (con `SuccessorUserId`):
- **Customer**: directorio → offboarded; reasigna/desasigna el keyset de clientes del preparador.
- **Communication**: reasigna host de reuniones activas al sucesor, o cancela/termina si no hay sucesor.
- **Connectors**: desconecta + **purga** los buzones **personales** del que se va (credenciales no transferibles).
- **Correspondence**: borradores abiertos → reasigna al sucesor o descarta.
- **Signature**: archiva los `SignatureProfile` personales. *(La solicitud NO guarda user-id del preparador → no hay reasignación; el 8879 firmado §6109 es intocable.)*
- **CloudStorage**: revoca los share links activos creados por el que se va.
- **Calendar**: revoca el feed token, reasigna/cancela citas futuras que organizaba, limpia disponibilidad (reglas + bloqueos).
- **Tasks**: reasigna/desasigna tareas abiertas + blueprint de series.
- **Reminder**: cancela + desagenda los recordatorios privados ("recordame a MÍ").
- **Subscription**: libera el asiento.
- **Notes**: registra al usuario en `OffboardedStaffProjection` → habilita el **manage-override** (un staff con `notes.view_all` puede editar las notas huérfanas del retirado; `CreatedByUserId` nunca se transfiere).

**`UserAnonymized`** *(Fase 4, planificado)*: barrido de PII denormalizado (Customer directory, Communication userDirectory + SenderDisplayName; resto no-op o sin PII). Nunca toca Guid ni cadenas legales/auditoría.

## Qué se conserva SIEMPRE (procedencia / legal)

Ni offboard ni anonymize borran: el **Guid** del usuario en todos los servicios, `CreatedByUserId`/`PreparerSignedByUserId`/`OrganizerUserId`/`AssignedPreparerUserId` (procedencia), ni las cadenas inmutables `AuthAuditLog`, `CustomerAuditLog`, `StorageAccessLog`, `SignatureAuditEvent` (HMAC), 8879 §6109, `ConsentEvent` §7216, `Invoice`/`PaymentReceipt`, `TenantTermsAcceptance`.

## Reglas transversales
- Consumidores **idempotentes**, **order-independent** y **fail-closed** (sin sucesor → bloquear o rutar a oficina).
- Corren en scope de Wolverine sin `TenantContext` ambiente → los repos usan `IgnoreQueryFilters()` + predicado de tenant explícito.
- Migraciones aditivas; procedencia/auditoría/legal nunca se borran.
- Copy de front en inglés (ej.: `User.Offboarded` = "This user has been removed and can't be reactivated.").
