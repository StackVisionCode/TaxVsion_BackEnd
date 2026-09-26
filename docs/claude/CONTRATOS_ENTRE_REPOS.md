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
| `Signature.Request.PreparerNotSelf` | 403 | Se intentó firmar como preparer una solicitud cuyo preparer es otra persona (A1) | "Solo {nombre} puede firmar como preparador". No ofrecer el botón si el preparer no es el usuario actual |
| `SignatureRequest.NotOwner` | 403 | La solicitud de firma es de otro y no tenés `signature.request.manage` (A1) | "Solo quien creó la solicitud puede hacer esto". Solo aparece con el flag de ownership encendido |
| `Task.Forbidden` | 403 | La tarea es de otro y no tenés `tasks.manage_all` (A1: ahora también en dependencias, adjuntos y series) | No ofrecer la acción sobre tareas ajenas |
| `Draft.AccountNotVisible` | 400 | Se intentó redactar desde un buzón al que no tenés acceso (A1) | Ofrecer en el selector de remitente **solo** los buzones visibles |
| `Folder.FileDeletePermissionRequired` | 403 | La carpeta no está vacía: borrarla borra sus archivos, y eso exige `cloudstorage.file.delete` (A1) | "Esta carpeta tiene archivos. Pedí permiso para borrar archivos o vaciala primero" |
| `Chat.CustomerToCustomerNotAllowed` | 400 | Un cliente del portal intentó abrir un chat con otro cliente | Portal: no ofrecer esa acción. Si llega, mensaje "solo puedes escribirle a tu oficina" |
| `Call.CustomerToCustomerNotAllowed` | 400 | Un cliente del portal intentó llamar a otro cliente | Igual que el anterior, en la llamada |
| `Authz.PermissionDenied` | 403 | Sin el permiso que el endpoint exige | Pantalla "acceso restringido". Trae `permission` cuando el endpoint lo declara |
| `Authz.ActorTypeNotAllowed` | 403 | Ese tipo de cuenta no puede usar el endpoint | Pantalla "acceso restringido". No ofrecer la acción a ese actor |
| `Authz.ActorTypeNotDeclared` | 403 | El endpoint no declara qué cuentas pueden usarlo | **Es un bug del backend**, no del usuario. Reportarlo; la UI muestra un error genérico |
| *(sin código)* | 403 | Sin permiso, de un endpoint anterior a A5 | Pantalla "acceso restringido" |

**Regla dura, en los dos frontends:** un **403 nunca cierra la sesión**. Solo un refresh rechazado
cierra sesión.

**Forma del 403 (hecho en A5).** Las cuatro capas de autorización responden `application/problem+json`
(RFC 9457) con este cuerpo. Antes, el 403 de las capas 1 y 2 llegaba **vacío**:

```json
{
  "type": "https://taxvision.dev/problems/authorization",
  "title": "Forbidden",
  "status": 403,
  "detail": "Your plan does not include the 'campaigns' module required for this action.",
  "code": "Authz.ModuleUnavailable",
  "reason": "module",
  "module": "campaigns",
  "message": "Your plan does not include the 'campaigns' module required for this action.",
  "correlationId": "..."
}
```

| Campo | Para qué |
|---|---|
| `code` | El switch del frontend. **No cambió ninguno de los que ya existían** |
| `reason` | Vocabulario cerrado: `permission`, `actor_type`, `not_declared`, `surface`, `module`. Es lo que decide **qué pantalla** mostrar |
| `module` | Solo con `reason: "module"`. El código del módulo que falta, para poder nombrarlo sin mantener tu propia copia del mapa permiso → módulo |
| `permission` | Solo con `reason: "permission"` y cuando el endpoint lo declara |
| `message` | **Duplicado de `detail`, a propósito**: los frontends desplegados leen `{code, message}`. Podés seguir usándolo |

**Regla:** decidí por `reason`, no por el texto. `reason: "module"` es una pantalla comercial ("tu plan
no lo incluye"); `reason: "permission"` es acceso restringido ("pedile a tu administrador").

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
| `GET /auth/me/access` | ambos | **Hecho en A5.** El bootstrap único — ver el detalle abajo |

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

### Suscripción: qué pedir para el banner (A5)

| Endpoint | Quién | Qué devuelve |
|---|---|---|
| `GET subscriptions/me/status` | **usalo para el banner** | `{ status, billingAccessBlocked, gracePeriodEndsAtUtc, nextRenewalAtUtc, canManageBilling }`. Cero datos comerciales: ni plan, ni precio, ni límites. Cualquier empleado lo puede pedir |
| `GET subscriptions/me` | compatibilidad | La respuesta completa de siempre, pero **sin `billing.view` los campos comerciales vienen vacíos** (`planCode`/`planName` en `""`, precios y límites en `0`, `suspensionReason` y `lastPaymentFailure` en `null`). El estado, las fechas del lapso y `enabledModules` siguen llegando |
| `GET subscriptions/plan-change` | admin | Ahora exige `billing.view` (nombra el plan destino y su precio) |

**El CRM debe migrar el banner a `me/status`** (B7). Mientras siga usando `GET subscriptions/me` no se
rompe nada, pero un empleado sin `billing.view` ya no ve el precio ni el nombre del plan.

### `GET /auth/me/access` — el bootstrap único (A5)

Con esto, y nada más, el CRM arma sidebar, guards y botones, y el Portal sus áreas. Reemplaza la
combinación de `GET /auth/me` + `GET /auth/me/effective-access` + `GET /subscriptions/me`.

```json
{
  "actorType": "TenantEmployee",
  "effectivePermissions": ["customers.view", "documents.view"],
  "modules": ["customers", "documents"],
  "permissionsVersion": 7,
  "entitlementsRevision": 42,
  "subscription": { "state": "active", "canManageBilling": false },
  "eTag": "W/\"a1b2...\""
}
```

| Campo | Qué es |
|---|---|
| `effectivePermissions` | La unión de los roles activos **menos los denies vigentes**. Ya viene resuelto: no lo recompongas |
| `modules` | Módulos que el plan de la oficina habilita |
| `permissionsVersion` | El `perm_v` del usuario. Si tu token trae uno menor, el siguiente request da `401 Auth.TokenStale` |
| `entitlementsRevision` | Revisión del snapshot de entitlements del tenant. **No viaja en el JWT** a propósito: un cambio de plan no invalida tokens |
| `subscription` | `state` es `active`, `billing_blocked` o `suspended`, y coincide con el corte real de acceso (`Expired` sale como `billing_blocked`). `canManageBilling` sale de los **permisos**, nunca del actor type |

**Tres reglas duras:**

1. **`subscription` es `null` para `CustomerPortal`.** La forma del portal no lleva semántica comercial:
   un cliente de la oficina no tiene por qué saber si la oficina está al día con su suscripción. No lo
   pidas ni lo muestres.
2. **Un token de la superficie Account (Landing) NO obtiene este endpoint.** Responde
   `403 Auth.SurfaceNotAllowed`. Es deliberado (§R.4.1): el Account usa `GET /auth/me` y
   `GET subscriptions/me/account`.
3. **Usá el ETag.** Mandá `If-None-Match` y manejá el `304`: podés pedir el bootstrap en cada navegación
   sin costo. El ETag cubre **todo** el contenido, no solo `permissionsVersion` — un deny que se vence o
   un módulo que se habilita lo mueven igual.

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

### Ownership de recurso: qué cambió en A1

Algunas acciones que antes devolvían 200 ahora devuelven 403 o 404. **Es el efecto buscado**, no una
regresión:

| Recurso | Regla nueva |
|---|---|
| Solicitud de firma (14 sub-recursos: signers, fields, preparer, PIN…) | Su creador, o `signature.request.manage`. **Solo con el flag de ownership encendido** |
| Firmar como preparer | Solo el usuario asignado como preparer. No depende de ningún flag |
| Tarea: dependencias, adjuntos, series | Quien la creó o la tiene asignada, o `tasks.manage_all` |
| Borrador de correo | Su autor, o quien ve el buzón de la oficina. El ajeno responde **404** |
| Buzón de envío de un borrador | Solo un buzón visible para el caller |
| Adjunto de un correo entrante (descarga y URL) | El gate de buzón que ya regía el cuerpo del mensaje |
| Borrar carpeta con archivos dentro | Exige `cloudstorage.file.delete` además de `folder.manage` |

**Dos cosas para la UI:**

1. **El selector de remitente** al redactar tiene que ofrecer solo los buzones visibles del usuario; si
   manda otro, llega `Draft.AccountNotVisible`.
2. **El botón "firmar como preparador"** solo va si el preparer asignado es el usuario actual. El
   endpoint responde `Signature.Request.PreparerNotSelf` a cualquier otro.

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
| `t:{tenant}:members` | staff **y** clientes del portal, nunca invitados de meeting (A5) | `access.changed` de ámbito tenant |

  El portal **no** debe suscribirse a los tres del segundo grupo: nombran clientes, correos y
  solicitudes de firma de la oficina y ya no le llegan.

**`access.changed` (hecho en A5).** Dice "tu acceso cambió, volvé a pedir `GET /auth/me/access`", y
**nada más**: no lleva permisos ni datos del plan, para que no haya dos fuentes de verdad ni importe el
orden de llegada.

```json
{ "scope": "user", "permissionsVersion": 7 }
```

| `scope` | Cuándo llega | A quién |
|---|---|---|
| `user` | Cambiaron los permisos de esa persona (le tocaron los roles, o cambió un rol que tiene) | Solo a sus sockets (`t:{tenant}:u:{userId}`) |
| `tenant` | Cambiaron los módulos que el plan de la oficina habilita | A `t:{tenant}:members`. `permissionsVersion` es `null` |

Qué hacer al recibirlo: **refetch de `GET /auth/me/access`**, nada más. Con `scope: "user"` podés además
ignorar el evento si su `permissionsVersion` no es mayor que el que ya tenés. Un cliente del portal
recibe los dos ámbitos; un invitado de meeting, ninguno.

**`session.revoked` (ya existía; en A5 se completó quién lo dispara).** La sesión murió en otro sitio →
limpiar y mandar a login. Desde A5 lo emiten **también** la baja de un usuario, el offboard, la
suspensión administrativa del tenant y el bloqueo por facturación; antes solo el logout-all y el cambio
de contraseña, así que una pestaña abierta se quedaba con la sesión muerta hasta su siguiente request.

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
