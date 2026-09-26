# Progreso — Track A (`BACKENDPERMISSIONS`)

Inicializado el **2026-09-26** con el estado real encontrado en la auditoría (§R.2 y §R.3 del PLAN).
**La sesión en la nube mantiene esta tabla al día: una fila por fase, actualizada al cerrarla.**

Estados: **NI** no iniciada · **EN CURSO** · **P** parcial · **C** completa · **BLOQUEADA** · **OB** obsoleta.

| Fase | Estado | Rama | Commits | Tests agregados | Verificación | Pendientes |
|---|---|---|---|---|---|---|
| A0 — Hotfixes de seguridad | **C** | `claude/great-heisenberg-j7fnkh` | `7a85c03`, `4caa3fd` | 63 (.NET) + 20 (Node) | build Release · gate de CI 4835/4835 · `npm run typecheck` + 446/446 de Communication · migración aplicada a SQL Server 2022 real · integración de Auth 6/6 | Solo queda el **scope** M2M de `internal/stock/commit-sale`: preparado y documentado, sin activar (ver `DECISIONES.md`) |
| A1 — Ownership de recurso | **NI** | — | — | — | — | Signature (14 sub-recursos), Tasks, Correspondence, Customer, CloudStorage, Campaigns |
| A2 — Deny layer y propagación | **C** | `claude/great-heisenberg-j7fnkh` | `8004694` | 15 | build Release · gate de CI 4850/4850 · migración aplicada a SQL Server 2022 real · integración de Auth 6/6 | El fallback pre-RBAC de `UserAccessResolver` se dejó como está: ya exige `activeCustomRoles.Count == 0` (G2 cerrado) y quitarlo dejaría sin permisos a los usuarios creados antes del modelo. Ver `DECISIONES.md` |
| A3 — Baseline y catálogo | **P** | `claude/great-heisenberg-j7fnkh` | `01a0859` | 21 (.NET) + 6 (Node) | build Release · gate de CI 4865/4865 · `npm test` 452/452 · migración aplicada a SQL Server 2022 real | Falta **A3.4 primera mitad**: el endpoint `POST billing/invoices/{id}/email` no se construyó (necesita una plantilla de correo y registrar a Billing como cliente M2M de Notification en la configuración de producción). Ver `DECISIONES.md`. A3.3 (`correspondence.organize`, COULD) tampoco |
| A4 — Techo y API de roles | **P** | `claude/great-heisenberg-j7fnkh` | `574e0e6` | 31 | build Debug · gate de CI 4896/4896 · `csharpier` de los archivos de la fase | La migración `AddRoleTargetActorType` está **generada, no aplicada** (el usuario pidió no aplicar migraciones ni usar Docker en esta sesión), así que **falta la integración de Auth**. Duplicar rol (COULD) no se hizo. La dirección inversa del fitness (todo código del catálogo se exige o es `IsReserved`) queda para A7 |
| A5 — Bootstrap, errores, realtime | **C** | `claude/great-heisenberg-j7fnkh` | `PENDIENTE_A5` | 33 (.NET) + 6 (Node) | build Release · gate de CI 4929/4929 · `npm run typecheck` + 457/457 de Communication · `csharpier` de los archivos de la fase | Sin migración: la fase no toca el esquema. El CRM y el Portal tienen que **migrar** a `/auth/me/access` y a `subscriptions/me/status` (Track B y C); hasta entonces `GET subscriptions/me` sigue respondiendo, con los campos comerciales vacíos si el caller no tiene `billing.view` |
| A6 — Entitlement enforcement | **NI** | — | — | — | — | `Authorization:ModuleGate:Enforce` no existe en ninguna configuración. Va **al final** y solo con B7 y C5 desplegados |
| A7 — Tests y observabilidad | **P** | — | — | — | — | Ya hay fitness de catálogo, de superficie y (A4) de "todo código exigido existe en el catálogo". Falta la matriz actor × permission × deny × plan × **asignación** y la dirección inversa del fitness (~60 códigos del catálogo se exigen fuera de un atributo: Node y chequeos imperativos) |
| A8 — Documentación | **NI** | — | — | — | — | — |

## Qué verificar antes de dar una fase por cerrada

1. `dotnet build TaxVision.slnx -c Release` sin errores.
2. El gate **exacto** del CI en verde (ver `TESTING.md`), redirigido a un archivo — **nunca** a `tail`.
3. `npm run typecheck && npm test` en `src/Services/Communication` si la fase lo tocó.
4. `dotnet csharpier check` **de los archivos de la fase**.
5. La migración de la fase **aplicada**, no solo creada. **Excepción vigente desde A4:** el usuario
   pidió no aplicar migraciones ni levantar Docker en esta sesión por consumo de créditos, así que
   desde A4 las migraciones se **generan y se revisan a mano**, y los tests de integración que
   necesitan SQL Server quedan sin correr. Está anotado en la columna "Pendientes" de cada fase.
6. El test de regresión que demuestra que ningún rol existente perdió accesos (§R.7 del PLAN).

## Mini plan por fase

### A0 — cerrada el 2026-09-26

| Sub | Archivos tocados | Tests |
|---|---|---|
| A0.1 | `BuildingBlocks/ActorTypeAuthorization/ClaimsPrincipalExtensions.cs`, `Growth.Api/Common/ClaimsPrincipalExtensions.cs`, `Tenant.Api/Program.cs` | `ClaimsPrincipalExtensionsTests`, `ControllerIdentityExtensionsTests` |
| A0.2 | `Auth/Domain/Roles/ReservedRoleNames.cs` (nuevo), `Role.cs` | `ReservedRoleNamesTests`, `RoleTests` |
| A0.5 | `CloudStoragePermissions.cs`, `PermissionCatalog.cs`, `LegalController.cs`, migración `AddCloudStorageDmcaManagePermission` | `PermissionCatalogTests` (DMCA PlatformOnly + regresión de legal hold) |
| A0.6 | Communication: `calls.route.ts`, `initiate-call.ts`, `call-handlers.ts`, `start-direct-conversation.ts`, `join-meeting.ts`, `auth.plugin.ts`; y los 6 `offboarding-impact` de Calendar, Customer, Connectors, CloudStorage, Correspondence y Tasks | `customer-call-history-route`, `initiate-call`, `start-direct-conversation`, `meeting-invitations-flow`, `auth-plugin-user-rate-limit` |
| A0.7 | `realtime-emitter.ts`, `socket-realtime-emitter.ts`, `build-io.ts`, `correspondence-consumers.ts`, `customer-consumers.ts`, `signature-consumers.ts` | `correspondence-consumers`, `customer-consumers` |
| A0.8 | `ShareResolutionQueries.cs` | `ShareLinkHandlerTests` (TenantOnly y TenantCustomers) |
| A0.9 | `InternalAccountsController.cs`, `MessagesController.cs` | `InternalTenantBoundaryTests` |

### A2 — cerrada el 2026-09-26

| Sub | Archivos tocados | Tests |
|---|---|---|
| Fan-out por titular | `Auth/Application/Common/RolePermissionsFanOut.cs` (nuevo), `RoleCommands.cs`, `SystemRolePermissionsSyncService.cs`, `IUserRepository`/`IRoleRepository` + sus implementaciones | `RolePermissionsFanOutTests` (6) |
| Consumidores | los 24 `RolePermissionsChanged…Consumer` dejan de recomponer la unión del usuario | los 9 tests de unión reescritos al contrato nuevo |
| Jerarquía en la baja | `UserManagementCommands.cs` (`DeactivateUserCommand.CallerActorType`), `UsersController.cs` | `UserManagementCommandsTests` (5) |
| Invitaciones | `CreateInvitation.cs`, `AcceptInvitation.cs` | `AcceptInvitationHandlerTests` (2) |
| Denies con razón y vencimiento | `UserPermissionDeny.cs`, `RoleConfigurations.cs`, `RoleRepository.cs`, `SetUserPermissionOverridesCommand.cs`, `UsersController.cs`, `ExpiredPermissionDeniesService.cs` (nuevo), migración `AddUserPermissionDenyReasonAndExpiry` | `SetUserPermissionOverridesHandlerTests` (2) |

### A5 — completa, 2026-09-26

| Sub | Estado | Qué se hizo |
|---|---|---|
| A5.3 403 RFC 9457 | **C** | `AuthorizationDenial` (nuevo) + `ProblemDetailsAuthorizationResultHandler` (nuevo, registrado en `AddActorTypeAuthorization` → los 14 servicios sin tocar su `Program.cs`). Las cuatro capas responden `{code, reason, module?, permission?}`, y el gate de módulo agrega `reason: "module"` en `ExceptionHandlingMiddleware` |
| A6.1 `GET /auth/me/access` | **C** | Permisos efectivos, módulos, `permissionsVersion`, `entitlementsRevision`, bloque `subscription` (solo staff) y ETag con 304. **Sin `[AllowSurface]`** (§R.4.1), con un test que lo fija |
| A6.2 `access.changed` | **C** | Evento de socket nuevo + room `t:{tenant}:members` (staff **y** clientes, nunca invitados). Se emite al usuario cuando cambian sus permisos y al tenant cuando cambian los módulos |
| A6.3 denylist + `session.revoked` | **C** | `SessionAccessCutoff` (nuevo): denylist → anuncio → revocación, en ese orden. Aplicado en baja, offboard, suspensión administrativa y bloqueo por facturación |
| A6.4 Subscription | **C** | `GET subscriptions/me/status` (nuevo, sin dato comercial alguno); `GET subscriptions/me` redacta plan, precio y límites sin `billing.view`; `GET subscriptions/plan-change` pasa a exigirlo |
| A6.5 estado coherente | **C** | `state` = `active` / `billing_blocked` / `suspended`, derivado del mismo flag que corta el acceso de verdad (así `Expired` no aparece como activo) |
| **Deuda de A2 cerrada** | **C** | El consumer `auth.role.permissions_changed.v1` de **Node** seguía recomponiendo la unión de permisos del usuario, o sea **resucitando los denies** (G3). Es el mismo bug que se cerró en los 24 consumers de .NET en A2; este había quedado afuera |

**Lo que A5 NO hizo:** poner `GET subscriptions/me` entero detrás de `billing.view`. El shell del CRM
desplegado lo pide en cada sesión para el banner de ciclo de vida y solo lee `status`,
`billingAccessBlocked` y `gracePeriodEndsAtUtc` (verificado en `FRONTENDPERMISSIONS`), así que un 403 le
apagaría el banner a todos los empleados en silencio. Los campos comerciales se redactan y el endpoint
limpio (`me/status`) queda listo para que el CRM migre. Ver `DECISIONES.md`.

**Secuencia de despliegue de A5** (todo aditivo, sin orden crítico):

1. Desplegar los servicios .NET: el cuerpo del 403 cambia de vacío a RFC 9457, que es compatible
   (`code` y `message` siguen ahí).
2. Desplegar Auth, con `/auth/me/access` disponible.
3. Desplegar Communication: el room `members` se llena en el siguiente handshake de cada socket, así
   que `access.changed` a nivel tenant empieza a llegar a medida que los clientes reconectan.
4. Los frontends migran cuando quieran (B7, B8, C5, C8). Nada los obliga.

### A4 — parcial, 2026-09-26

| Sub | Estado | Qué se hizo |
|---|---|---|
| A4.1 techo formal (§27) | **C** | `PermissionCeiling.cs` (nuevo) es el único techo; `RolePermissionGuard` queda como fachada. `PlatformOnly`, `IsDangerous` e `IsReserved` se leen **explícito**. Aplicado además en **asignar rol**, **invitar con roles** y **aceptar la invitación**, que no lo revalidaban |
| A4.1 validación por delta | **C** | Al editar un rol el techo mide solo el **delta añadido**: un rol con permisos dormidos por un downgrade vuelve a ser editable (§27 [D]) |
| A4.2 `GET /auth/roles/{id}/users` | **C** | Titulares **activos** del rol (la misma lista que recibe el fan-out, para que la UI y los avisos no divergan) |
| A4.2 reactivar rol | **C** | `POST /auth/roles/{id}/reactivate` + `Role.Reactivate()` + fan-out por titular + acción de auditoría propia `auth.role.reactivated` |
| A4.2 unicidad en el handler | **C** | Renombrar a un nombre ocupado devuelve `Role.NameConflict`, no un 409 del índice |
| A4.2 roles de portal editables (G8) | **C** | `Role.TargetActorType` (columna nueva, nullable) + backfill conservador en la migración |
| A4.2 validar contra los titulares (G7) | **C** | El delta se valida contra el actor type de **cada titular activo** del rol |
| A4.2 duplicar rol | **NI** | COULD; no se abordó |
| A4.3 `GET /auth/permissions` con flags | **C** | `isAssignableByTenant`, `platformOnly`, `isDangerous`, `isReserved`, `minPlanTier`, `gateModule`, `allowedActorTypes` y `grantable` para el tenant del token |
| A4.4 fitness | **P** | Nuevo: todo código exigido por `[HasPermission]`/`[HasPermissionForActor]` existe en el catálogo (0 violaciones hoy). Ya existía: PlatformOnly/IsDangerous ⇒ no asignable, y nombres de rol reservados (A0). La dirección inversa va a A7 |

**Lo que A4 deliberadamente NO hizo:** la dimensión `∩ Effective(caller)` de §27 queda documentada y
sin implementar. Exigirla hoy rompería la gestión de roles del portal: el bundle del rol de sistema
Tenant Admin excluye los permisos `IsCustomerPortal`, así que un Tenant Admin no tiene —ni debe
tener— los permisos que concede a un rol de clientes. Ver `DECISIONES.md`.

**Secuencia de despliegue de A4** (sin orden crítico, todo es aditivo):

1. Aplicar la migración `AddRoleTargetActorType` (columna nullable + backfill que solo marca roles
   que ya eran de portal).
2. Desplegar **Auth**. Los endpoints nuevos (`/users`, `/reactivate`) y los campos nuevos de
   `GET /auth/permissions` son aditivos: el CRM desplegado sigue leyendo lo de siempre.

### A3 — parcial, 2026-09-26

| Sub | Estado | Qué se hizo |
|---|---|---|
| A3.1 baseline del empleado | **C** | Notes, `signature.request.cancel`, `communication.group.create`, y los GET de plantillas bajo `signature.request.create` |
| A3.2 split de Campaigns | **C** | `campaigns.view/manage/send/senders.manage` + los 34 atributos + migración con backfill de roles custom |
| A3.3 `correspondence.organize` | **NI** | COULD; no se abordó |
| A3.4 factura por correo | **P** | Solo la mitad del emisor legal (`invoicing.issuer.manage`). El endpoint de enviar la factura queda pendiente |
| A3.5 permisos sin uso | **C** | Flag `IsReserved` + `portal.miles.use` reservado + fitness test |
| A3.6 alinear actor y permission | **C** | `sms.manage`, `notification.log.view`, `users.invite`, `audit.view` de Subscription |
| §R.6 `portal.calls.use` | **C** | Al bundle del portal + exigido solo al actor CustomerPortal, detrás de un flag apagado |

**Secuencia de despliegue obligatoria de A3** (si se invierte, los clientes pierden las llamadas):

1. Aplicar la migración `SplitCampaignsAndAddReservedFlag`.
2. Desplegar **Auth** y dejar que `SystemRolePermissionsSyncService` resincronice los roles de
   sistema (publica `RolePermissionsChanged` + el fan-out por titular de A2).
3. Verificar en un par de servicios que la proyección de un cliente de portal ya trae
   `portal.calls.use`.
4. Desplegar Communication y **solo entonces** poner
   `COMMUNICATION_PORTAL_CALLS_PERMISSION_ENFORCE=true`.

**Lo que NO se pudo verificar en A2:** el barrido de denies vencidos
(`ExpiredPermissionDeniesService`) no tiene test propio — es un `BackgroundService` con
`PeriodicTimer`; lo que sí está cubierto es la consulta que lo alimenta y el fan-out que usa. Los
tests de integración de los 24 consumidores tampoco (necesitan cada servicio levantado).

**Lo que NO se pudo verificar en A0:** los tests de integración de Tenant, Postmaster, CloudStorage y
PaymentApp (CloudStorage necesita MinIO, cuya imagen no se puede descargar desde este entorno). Los de
Auth sí corrieron (6/6) contra SQL Server 2022 + Redis + RabbitMQ reales.

## Entorno de esta sesión

- **.NET SDK 10.0.112** (archivo de Ubuntu 24.04). `global.json` pide 10.0.300 y **no se tocó**: los
  comandos se lanzan con el cwd fuera del repo y la ruta absoluta de la solución. Ver `DECISIONES.md`.
- **SQL Server 2022** en Docker (`deploy/docker/docker-compose.claude.yml`, servicio `sqlserver`).
  **Redis** y **RabbitMQ** instalados con `apt` (las imágenes de Docker Hub están bloqueadas en este
  entorno; `mcr.microsoft.com` sí se alcanza). **MinIO no está disponible.**
- Variables de entorno de ejemplo para los tests de integración: `ConnectionStrings__Default`,
  `ConnectionStrings__Redis`, `RabbitMq__Uri`, `Encryption__MasterKey`.

## Bloqueos

- **MinIO no disponible** en este entorno (la imagen sale de Docker Hub, bloqueado). Los tests de
  integración de CloudStorage quedan fuera de lo verificable desde la sesión.

## Notas del estado inicial

- El repo está **al día** con el trabajo cerrado el 2026-09-25/26: plan Account/Manage Subscription
  (F1–F14), ABAC por asignación (P1+P2), retiro de empleados (F0–F3) y el permiso `portal.folders.view`
  ya aplicado.
- Existe una **cuarta superficie** que el plan original no cubría: el Account del Landing
  (`[AllowSurface]`). Toda fase que toque endpoints debe tenerla en cuenta.
- Hay fitness tests de superficie que fallan si un endpoint nuevo no declara lo que debe: si uno se
  pone rojo, **la respuesta es actualizar la lista, no borrar el test**.
