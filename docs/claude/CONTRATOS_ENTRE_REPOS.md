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
| `role` (`ClaimTypes.Role`) | pseudo-rol del actor type + nombres de los custom roles activos | **No es fiable para autorizar**: un custom role puede llamarse igual que el pseudo-rol. Solo para mostrar. El backend ya no lo mira para decidir si alguien es PlatformAdmin (A0.1), y `Role.Create`/`Role.Update` rechazan los nombres reservados con comparación normalizada (A0.2) |
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
| `UserPermissionDeny.ExpiryInPast` | 400 | La fecha de vencimiento de un deny ya pasó | Error en el campo de la fecha, en el drawer de accesos |
| `User.Hierarchy` | 403 | Un empleado intentó dar de baja a un administrador | Mensaje "solo un administrador puede hacerlo". No ofrecer la acción |
| `User.LastAdmin` | 400 | Es el último administrador activo de la oficina | Mensaje "la oficina necesita al menos un administrador" |
| `Role.NameReserved` | 400 | El nombre del rol colisiona con uno reservado por la plataforma | Error en el campo del nombre, en el formulario de rol. No es un fallo de permisos |
| `Role.NameConflict` | 409 | Ya existe otro rol con ese nombre en la oficina | Error en el campo del nombre. Antes salía como un 409 sin código desde el índice de la base |
| `Role.PermissionNotAssignable` | 400 | Alguno de los permisos pedidos está fuera del techo del tenant (de plataforma, peligroso, reservado, o fuera del plan) | Error en el picker, nombrando los códigos que vienen en el mensaje. Con `grantable` de `GET /auth/permissions` no debería llegar a pasar |
| `Role.NotAssignableToActorType` | 400 | El rol mezcla permisos de actor types incompatibles, o el permiso no es válido para los titulares del rol | Error en el picker. Filtrar por `allowedActorTypes` del catálogo |
| `Role.AlreadyActive` | 400 | Se intentó reactivar un rol que ya está activo | No ofrecer la acción cuando `isActive` es true |
| `Chat.CustomerToCustomerNotAllowed` | 400 | Un cliente del portal intentó abrir un chat con otro cliente | Portal: no ofrecer esa acción. Si llega, mensaje "solo puedes escribirle a tu oficina" |
| `Call.CustomerToCustomerNotAllowed` | 400 | Un cliente del portal intentó llamar a otro cliente | Igual que el anterior, en la llamada |
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
| `PUT /auth/users/{id}/permission-overrides` | CRM (admin) | Reemplaza el set completo de denies. Gateado por `roles.manage`. Acepta **dos formas** del cuerpo: `deniedPermissionIds` (ids planos, la forma que ya usa el CRM desplegado) o `denies` (`[{ permissionId, reason?, expiresAtUtc? }]`, con razón y vencimiento). Si viene `denies`, manda ese |
| `GET /auth/permissions` | CRM (admin) | Catálogo con `{ id, code, module, description, isCustomerPortal }` **más las banderas del techo** (A4): `isAssignableByTenant`, `platformOnly`, `isDangerous`, `isReserved`, `minPlanTier`, `gateModule`, `allowedActorTypes` y `grantable`. **`grantable` es la única que hay que mirar para habilitar una casilla**: ya resuelve la fórmula completa contra el plan del tenant del token. Las otras sirven para explicar el motivo ("no incluido en tu plan", "solo la plataforma"). Un permiso con `isReserved` **no se ofrece**: está declarado pero todavía no protege nada |
| `GET /auth/roles/{id}/users` | CRM (admin) | **Nuevo** (A4). Titulares **activos** del rol: `[{ id, name, lastName, email, actorType, isActive }]`. Gateado por `roles.manage`. Úsalo antes de desactivar un rol, para decir a cuántos afecta |
| `POST /auth/roles/{id}/reactivate` | CRM (admin) | **Nuevo** (A4). Vuelve a poner en servicio un rol desactivado. `204`, o `Role.AlreadyActive` / `Role.NotFound`. Gateado por `roles.manage` |
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
`campaigns.view` · `campaigns.manage` · `campaigns.send` · `campaigns.senders.manage` ·
`notes.read` · `notes.manage` · `sms.read` · `sms.send` ·
`signature.*` · `correspondence.*` · `tasks.*` · `calendar.*`

### Portal del cliente — el bundle completo del rol "Customer Portal" (15)
`portal.folders.view` · `portal.calls.use` · `tasks.portal.client_requests` · `notes.portal.read` ·
`cloudstorage.file.view` · `cloudstorage.file.upload` · `cloudstorage.file.download` ·
`communication.chat.start` · `communication.chat.reply` · `communication.support.open` ·
`communication.call.start` · `communication.video_call.start` · `communication.meeting.join` ·
`communication.screenshot.create` · `communication.notification.read`

`portal.calls.use` entró al bundle en A3: lo exigen las rutas de llamada de Communication **solo** al
actor `CustomerPortal`, y detrás del flag `COMMUNICATION_PORTAL_CALLS_PERMISSION_ENFORCE` (apagado
hasta que las proyecciones converjan). Es la palanca que el administrador ve en el cajón de accesos
del cliente.

**Reservado, no se concede a nadie:** `portal.miles.use`. No existe el módulo ni ningún endpoint que
lo exija; está marcado `IsReserved` y dejó de ser asignable. La UI **no debe ofrecerlo**.

### Cambios del catálogo en A0 y A3

| Permission | Qué cambió | ¿Lo ve el CRM? |
|---|---|---|
| `cloudstorage.dmca.manage` | **Nueva** (A0), PlatformOnly y no asignable: registrar y cerrar takedowns DMCA. Ningún rol de tenant la recibe | No |
| `cloudstorage.legal.manage` | Ya no cubre el DMCA (A0). Queda solo el legal hold sobre archivos del propio tenant | No |
| `campaigns.view` · `campaigns.send` · `campaigns.senders.manage` | **Nuevas** (A3): `campaigns.manage` se partió en cuatro. Ver, editar, enviar y administrar remitentes | **Sí**: la sección de campañas tiene que preguntar por el código correcto por acción |
| `campaigns.manage` | Ya no cubre ver ni enviar | **Sí** |
| `invoicing.issuer.manage` | **Nueva** (A3): editar el emisor legal de las facturas. Sale de `invoicing.manage` | **Sí**: el formulario del emisor se gatea con este |
| `portal.miles.use` | Reservado y no asignable (A3) | No lo ofrezcas |

### Forma de un rol en `GET /auth/roles` (cambios de A4)

| Campo | Significa |
|---|---|
| `assignableActorTypes` | Actor types a los que el rol es asignable (todos sus permisos los permiten). Ya existía |
| `targetActorType` | **Nuevo** (A4). Para qué actor type se creó el rol, o `null` si no se declaró. Un rol con `"CustomerPortal"` es un rol de **clientes**: al editarlo, ofrece solo permisos con `CustomerPortal` en `allowedActorTypes` |

**Dos reglas del editor de roles que el CRM tiene que respetar (A4):**

1. **Al editar, manda el set completo, no solo lo nuevo.** `PUT /auth/roles/{id}/permissions`
   reemplaza el set entero, como siempre. El backend valida el techo solo sobre lo que **se agrega**,
   así que un rol que quedó con permisos "dormidos" por una baja de plan **se puede volver a guardar**
   sin quitarlos. No los filtres del cuerpo: si los quitas, los borras.
2. **Un permiso dormido no es un error.** Es uno que el rol ya tenía y que hoy tiene `grantable:
   false` por plan o módulo. Muéstralo como "Inactive — requires {gateModule}", no como inválido, y
   déjalo marcado.

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
- Las rutas HTTP de Communication rechazan un token M2M (`actor_type=Service`) con
  `403 Auth.Forbidden`.
- **Dos ámbitos de broadcast por tenant** (A0.7):

| Ámbito | Quién lo recibe | Qué va por ahí |
|---|---|---|
| `t:{tenant}` | todo el tenant, clientes del portal e invitados de meeting incluidos | `chat.presence.changed` |
| `t:{tenant}:staff` | solo empleados y admins de la oficina | `mail.incoming`, `customer.changed`, `signature.request.changed` |

  El portal **no** debe suscribirse a los tres del segundo grupo: nombran clientes, correos y
  solicitudes de firma de la oficina y ya no le llegan.
- `access.changed` `[POR CREAR]` (A5): señal para que el frontend refresque su bootstrap.
- `session.revoked` `[POR CREAR]` (A5): la sesión murió en otro sitio.
- Respaldo sin realtime, ya válido hoy: refetch en `focus` / `visibilitychange`, y tras un 403.

## 8. Reglas de cambio

1. El contrato **nace en el backend**. Los frontends no inventan códigos ni formas.
2. Cambiar algo aquí obliga a actualizar este archivo **en los tres repos**, en el mismo PR.
3. Un permission nuevo se agrega en `PermissionCatalog` (código) + migración `HasData` **solo** para la
   fila, y se propaga por el sync al arrancar Auth. **Nunca** se concede por INSERT de migración.
   Cuando cambian los permisos de un rol, Auth publica **dos** señales: `RolePermissionsChanged`
   (qué tiene el rol) y un `UserRolesChanged` **por titular** con sus códigos ya efectivos
   (roles − denies). Una proyección local **no** debe recomponer la unión de un usuario a partir de
   la primera: los denies por usuario viven solo en Auth.
4. Un endpoint nuevo declara siempre `[AllowActorTypes]` (fail-closed) y, si lo alcanza el Account del
   Landing, `[AllowSurface]`.
