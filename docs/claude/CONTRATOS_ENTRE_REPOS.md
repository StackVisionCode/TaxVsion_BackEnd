# Contratos entre repositorios

**Este archivo es idéntico en los tres repos.** Es la fuente de verdad compartida: los nombres exactos
que el backend expone y que los dos frontends consumen. Si una fase cambia algo de aquí, **se actualiza
este archivo en los tres repos en el mismo PR**.

- `BACKENDPERMISSIONS` — produce estos contratos.
- `FRONTENDPERMISSIONS` — CRM del staff; los consume.
- `CLIENTREDESIGN` — Portal del cliente; los consume.

Todo lo listado aquí está **verificado en el código** al 2026-09-26. Lo que el plan propone crear y
todavía **no existe** está marcado `[POR CREAR]`.

---

## 1. Claims del JWT

| Claim | Valores | Notas |
|---|---|---|
| `actor_type` | `TenantEmployee` · `TenantAdmin` · `CustomerPortal` · `PlatformAdmin` · `Service` | **Inmutable**, se fija al registrar el usuario. Es el eje de autorización de la capa 2 y **el único** discriminador válido de PlatformAdmin |
| `role` (`ClaimTypes.Role`) | pseudo-rol del actor type + nombres de los custom roles activos | **No es fiable para autorizar**: un custom role puede llamarse igual que el pseudo-rol. Solo para mostrar |
| `perm_v` | entero | Versión de permisos. Si la proyección local del servicio está por delante → `401 Auth.TokenStale` |
| `sid` | GUID | Id de sesión. Base de la denylist por Redis |
| `tenant_id` | GUID | Tenant del token |
| `customer_id` | GUID | **Solo** para `CustomerPortal` |
| `surface` | ausente, o el valor de la superficie | Ausente = CRM/Portal. Presente = Account del Landing. Communication rechaza **cualquier** valor |
| `reauth_at` | epoch | Step-up. El refresh **no** lo copia |

**El claim `perm` ya no se emite.** Todo lector debe pasar por `IUserPermissionsSource`, nunca leer el
claim directo.

## 2. Forma de los errores

Forma plana de `BuildingBlocks.Results.Error`, serializada en camelCase:

```json
{ "code": "Auth.Invalid", "message": "Invalid credentials.", "retryAfterSeconds": 45 }
```

- `retryAfterSeconds` solo aparece en throttles de dominio; los limiters HTTP además mandan la cabecera
  `Retry-After`.
- `ProblemDetails` (RFC 7807) aparece **solo** en 500 y en 409 de conflicto: `{ title, status, detail, code, correlationId }`.
- Un fallo de red llega como `status 0`; los frontends lo normalizan a un código sintético propio.

**Los frontends leen `code`, no `type`.**

### Códigos que los frontends deben tratar de forma especial

| Código | HTTP | Significa | Qué hace el frontend |
|---|---|---|---|
| `Auth.TokenStale` | 401 | El token trae `perm_v` viejo | Refrescar **una vez** y reintentar. Nunca logout |
| `Auth.SurfaceNotAllowed` | 401 | Token de otra superficie | Mostrar el error. Nunca logout |
| `RateLimit.Exceeded` | 429 | Límite de tasa | Leer `retryAfterSeconds` / `Retry-After` y mostrar cuenta atrás |
| `Authz.ModuleUnavailable` | 403 | El plan del tenant no incluye el módulo | Pantalla "no incluido en tu plan". **Distinto** de "sin permiso" |
| `SubscriptionInactive` | 403 | La suscripción de la oficina no está activa | Portal → `office-inactive`. **Nunca** a login |
| *(sin código)* | 403 | Sin permiso | Pantalla "acceso restringido" |

**Regla dura, en los dos frontends:** un **403 nunca cierra la sesión**. Solo un refresh rechazado
cierra sesión.

`[POR CREAR]` (fase A5): todo 403 de las capas 1 y 2 responderá RFC 9457 con
`{ code, reason, module? }`. Hasta entonces el 403 de "sin permiso" llega con el cuerpo vacío y el
frontend lo trata por el status.

## 3. Endpoints de autorización que consumen los frontends

| Endpoint | Quién | Devuelve |
|---|---|---|
| `GET /auth/me` | CRM y Portal | Usuario, tenant, roles, `plan.enabledModules` |
| `GET /auth/me/effective-access` | CRM y Portal | Permissions efectivas por módulo, con la marca de denegado, y `permissionsVersion` |
| `GET /auth/users/{id}/effective-access` | CRM (admin) | Lo mismo, para otro usuario. Gateado por `roles.manage` |
| `PUT /auth/users/{id}/permission-overrides` | CRM (admin) | Reemplaza el set completo de denies. Gateado por `roles.manage` |
| `GET /auth/permissions` | CRM (admin) | Catálogo con `{ id, code, module, description, isCustomerPortal }` |
| `GET /auth/me/access` | ambos | `[POR CREAR]` (A5). Bootstrap único: `effectivePermissions`, `modules`, `permissionsVersion`, `entitlementsRevision`, `subscription.state`, `canManageBilling`, con ETag. **Debe ser consciente de la superficie** y su forma para `CustomerPortal` **no lleva semántica comercial** |

## 4. Capas de autorización, en orden

1. **Superficie** — `[AllowSurface]` + `SurfaceAuthorizationFilter`, fail-closed. Un token con claim
   `surface` solo entra donde se declara. Es un filtro **de MVC**: no cubre Communication (Node), que
   rechaza cualquier `surface` por su cuenta.
2. **Actor type** — `[AllowActorTypes(...)]` + `ActorTypeAuthorizationFilter`, global y fail-closed: una
   acción sin el atributo se bloquea. `PlatformAdmin` siempre pasa esta capa.
3. **Permission** — `[HasPermission(code)]` → `PermissionPolicyProvider` → `IUserPermissionsSource`
   (proyección local + `perm_v`). Aquí también corre el **module gate** (hoy en log-only).
   - `[HasPermissionForActor(actorType, code)]` exige un permiso **solo** a un actor type, para
     endpoints compartidos entre staff y portal. Apilar dos `[HasPermission]` **no** sirve: ASP.NET
     exige todas las policies.
4. **Ownership / scope** — `IsOwnerOrHasManageHandler`, visibilidad por asignación
   (`customers.view_all` como bypass), scope por `customer_id` en el portal.

**La barrera real es siempre el backend.** El frontend solo oculta o adapta la UI.

## 5. Permissions que los frontends nombran hoy

Los códigos son `string` estables. **No se replican reglas de negocio en el frontend**: se pregunta por
el código.

### CRM (staff)
`users.view` · `users.invite` · `users.manage` · `roles.manage` · `billing.view` ·
`customers.view` · `customers.view_all` · `customers.manage` · `customers.import` ·
`customers.fiscalprofile.reveal` · `cloudstorage.file.view/upload/download` ·
`campaigns.manage` · `notes.read` · `notes.manage` · `sms.read` · `sms.send` ·
`signature.*` · `correspondence.*` · `tasks.*` · `calendar.*`

### Portal del cliente — el bundle completo del rol "Customer Portal" (14)
`portal.folders.view` · `tasks.portal.client_requests` · `notes.portal.read` ·
`cloudstorage.file.view` · `cloudstorage.file.upload` · `cloudstorage.file.download` ·
`communication.chat.start` · `communication.chat.reply` · `communication.support.open` ·
`communication.call.start` · `communication.video_call.start` · `communication.meeting.join` ·
`communication.screenshot.create` · `communication.notification.read`

**Declarados en el catálogo para portal pero fuera del bundle:** `portal.calls.use` y
`portal.miles.use`. Ver §R.6 del plan: hoy **no los tiene ningún cliente y no los exige ningún
endpoint**. No se asuma que existen.

## 6. Módulos y entitlements

- `plan.enabledModules` llega en `GET /auth/me` como lista de códigos de módulo.
- El mapeo permission → módulo vive **solo en el backend** (`PermissionModuleMap`). **No se replica en
  los frontends.**
- Los módulos `portal` y `miles` **no** están en el mapa a propósito: son features de frontend sin
  endpoints propios.
- El gate de módulos está en **log-only**: `Authorization:ModuleGate:Enforce` no está definido en
  ninguna configuración y el default del código es `false`.
- La visibilidad por asignación tiene su propio flag por servicio
  (`<Svc>:AssignmentVisibility:Enabled`), **apagado en producción**.

## 7. Realtime

- Socket.IO en Communication. Un token con claim `surface` es rechazado.
- `access.changed` `[POR CREAR]` (A5): señal para que el frontend refresque su bootstrap.
- `session.revoked` `[POR CREAR]` (A5): la sesión murió en otro sitio.
- Respaldo sin realtime, ya válido hoy: refetch en `focus` / `visibilitychange`, y tras un 403.

## 8. Reglas de cambio

1. El contrato **nace en el backend**. Los frontends no inventan códigos ni formas.
2. Cambiar algo aquí obliga a actualizar este archivo **en los tres repos**, en el mismo PR.
3. Un permission nuevo se agrega en `PermissionCatalog` (código) + migración `HasData` **solo** para la
   fila, y se propaga por el sync al arrancar Auth. **Nunca** se concede por INSERT de migración.
4. Un endpoint nuevo declara siempre `[AllowActorTypes]` (fail-closed) y, si lo alcanza el Account del
   Landing, `[AllowSurface]`.
