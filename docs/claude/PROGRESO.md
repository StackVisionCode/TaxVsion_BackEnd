# Progreso — Track A (`BACKENDPERMISSIONS`)

Inicializado el **2026-09-26** con el estado real encontrado en la auditoría (§R.2 y §R.3 del PLAN).
**La sesión en la nube mantiene esta tabla al día: una fila por fase, actualizada al cerrarla.**

Estados: **NI** no iniciada · **EN CURSO** · **P** parcial · **C** completa · **BLOQUEADA** · **OB** obsoleta.

| Fase | Estado | Rama | Commits | Tests agregados | Verificación | Pendientes |
|---|---|---|---|---|---|---|
| A0 — Hotfixes de seguridad | **C** | `claude/great-heisenberg-j7fnkh` | `7a85c03`, `4caa3fd` | 63 (.NET) + 20 (Node) | build Release · gate de CI 4835/4835 · `npm run typecheck` + 446/446 de Communication · migración aplicada a SQL Server 2022 real · integración de Auth 6/6 | Solo queda el **scope** M2M de `internal/stock/commit-sale`: preparado y documentado, sin activar (ver `DECISIONES.md`) |
| A1 — Ownership de recurso | **NI** | — | — | — | — | Signature (14 sub-recursos), Tasks, Correspondence, Customer, CloudStorage, Campaigns |
| A2 — Deny layer y propagación | **P** | — | — | — | — | G2 ya estaba **C** (`UserAccessResolver.cs:31-32`). Abiertas: fan-out por titular, consumidores de `RolePermissionsChanged`, jerarquía en deactivate, `Reason`/`ExpiresAtUtc` |
| A3 — Baseline y catálogo | **NI** | — | — | — | — | Employee (75 permissions) sigue sin Campaigns ni Notes |
| A4 — Techo y API de roles | **NI** | — | — | — | — | `Grantable(...)` unificado, `GET /auth/roles/{id}/users`, reactivar rol |
| A5 — Bootstrap, errores, realtime | **NI** | — | — | — | — | Sin `/auth/me/access`, sin `access.changed`, sin `IAuthorizationMiddlewareResultHandler`. **Debe nacer consciente de la superficie** (§R.4.1) |
| A6 — Entitlement enforcement | **NI** | — | — | — | — | `Authorization:ModuleGate:Enforce` no existe en ninguna configuración. Va **al final** y solo con B7 y C5 desplegados |
| A7 — Tests y observabilidad | **P** | — | — | — | — | Ya hay fitness de catálogo y de superficie. Falta la matriz actor × permission × deny × plan × **asignación** |
| A8 — Documentación | **NI** | — | — | — | — | — |

## Qué verificar antes de dar una fase por cerrada

1. `dotnet build TaxVision.slnx -c Release` sin errores.
2. El gate **exacto** del CI en verde (ver `TESTING.md`), redirigido a un archivo — **nunca** a `tail`.
3. `npm run typecheck && npm test` en `src/Services/Communication` si la fase lo tocó.
4. `dotnet csharpier check` **de los archivos de la fase**.
5. La migración de la fase **aplicada**, no solo creada.
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
