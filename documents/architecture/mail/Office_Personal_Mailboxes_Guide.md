# Guía DEVS — Buzones de correo: Oficina vs Personal

Cómo funcionan los buzones de correo del tenant (Connectors) y la visibilidad del correo
(Correspondence) para el buzón **de oficina** (compartido) frente al **personal** de cada empleado.

Servicios involucrados: **Connectors** (cuentas de buzón + OAuth/IMAP), **Correspondence** (inbox
por cliente), **Auth** (catálogo de permisos). Frontend: `features/mail` (ruta `/email`).

---

## 1. Modelo

Un buzón conectado es un `TenantEmailAccount` (agregado en Connectors), clave única `(TenantId, EmailAddress)`.

- **Oficina** = buzón **compartido** del despacho (p. ej. `office@taxpro.com`). Se modela con
  **`OwnerUserId == null`**. Lo conecta el admin. Hay como mucho uno.
- **Personal** = buzón de un empleado (== su email de login). `OwnerUserId == <userId>`.

`IsOffice => OwnerUserId is null` (calculado, no hay enum). `CreatedByUserId` queda solo como auditoría.
El DTO (`GET /connectors/accounts`) expone `isOffice` y `ownerUserId`.

> Decisión: admin conecta **1** buzón (el de oficina); empleado conecta **1** personal. Se prefirió
> `OwnerUserId` sobre un enum `Kind` porque el tipo es derivable del dueño.

---

## 2. Permisos (Auth `PermissionCatalog` / `BuildingBlocks.Authorization.ConnectorsPermissions`)

| Permiso | Const | Default | Qué habilita |
|---|---|---|---|
| `connectors.accounts.read` | `AccountsRead` | Empleado + Admin | Ver la lista de buzones conectados |
| `connectors.accounts.write` | `AccountsWrite` | Admin | Conectar/reconectar/desconectar el buzón de **oficina** (o cualquiera). Incluye implícitamente los dos de abajo |
| `connectors.accounts.connect_own` | `AccountsConnectOwn` | **Empleado** + Admin | Conectar/administrar su **propio** buzón personal (== su login) |
| `connectors.accounts.office.read` | `AccountsOfficeRead` | **Empleado** + Admin | Ver el **buzón de oficina** y su **correo** |

- Todos son `IsAssignableByTenant = true`, no peligrosos. El modelo RBAC per-usuario es **solo-DENY**:
  el admin puede **restringir** por usuario (en *Edit access → Mail*) cualquiera de los que el rol otorga;
  no existe "grant" por usuario.
- `connect_own` y `office.read` están en el bundle **SystemEmployee** (ON por defecto). El
  `SystemRolePermissionsSyncService` los backfillea a los roles Employee existentes al arrancar Auth.
- Compose/reply/read/send/adjuntos son permisos de **Correspondence** (`correspondence.*`), por-tenant,
  NO por-buzón.

### Autorización por operación (Connectors `AccountsController`)

- **Conectar** (`POST /accounts`, `POST /accounts/manual`): oficina (`asOffice=true`) → `write`;
  personal → `write || connect_own`.
- **Administrar** (`DELETE /accounts/{id}`, `POST /accounts/{id}/reauth`): oficina → `write`;
  personal → dueño con `connect_own` (o `write`). Resuelto por `ResolveAccountOwnershipQuery` + `CanManageAsync`.
- **Ver lista** (`GET /accounts`): `read`. Devuelve los **personales del usuario** siempre + el de
  **oficina solo si `office.read`** (o `write`).
- **Ver detalle** (`GET /accounts/{id}`): personal solo su dueño; oficina solo con `office.read`/`write`
  → sin permiso, **404** (anti-enumeración).

**Revocar** `connect_own` a un empleado con personal ya conectado: el buzón sigue **usable** pero no
**administrable** (sin reauth/reconectar). **Revocar** `office.read`: deja de ver el buzón de oficina
y su correo (ver §5), conservando su personal.

---

## 3. Conectar un buzón

Dos vías, ambas con el flag **`asOffice`** (default `false` = personal):

### OAuth (Gmail / Microsoft 365)
`POST /connectors/accounts { providerCode, asOffice, returnUrl }` → `{ authorizationUrl }`. El frontend
redirige el navegador. El email lo toma **del proveedor autenticado** (userinfo / `/me`). `asOffice`
viaja en el `state` (Redis, pipe-delimited) y vuelve en el callback.

### Manual IMAP+SMTP
`POST /connectors/accounts/manual { emailAddress, displayName, imap*, smtp*, asOffice }`. Valida
conectividad real contra ambos servidores antes de persistir.

### Guard de identidad (`ConnectedEmailIdentityGuard`)
- **Personal** (`asOffice=false`): `emailAddress` **debe ser el email de login** del usuario. En el
  front, para manual el campo email queda bloqueado y el **username IMAP/SMTP = el propio email**
  (no se pide aparte).
- **Oficina** (`asOffice=true`): el guard **NO aplica** — el email puede ser cualquiera (`office@…`),
  y el username es editable (una cuenta de servicio puede diferir del email).

### Reconexión (RECONNECT)
Desconectar es **soft**: conserva la fila (`Status=Disconnected`), revoca (no borra) la conexión/token.
Reconectar el **mismo** email:
- **OAuth**: reutiliza la misma fila (mismo `Id`), revive la conexión y refresca tokens.
- **Manual/IMAP**: si la fila está `Disconnected`, `ConnectManualAccountHandler` la **revive**
  (misma fila, `UpdateSettings` sobre las credenciales) en vez de fallar. Cualquier otro estado →
  `AlreadyConnected` (desconectar primero).

Los **datos de correo sobreviven** siempre a un disconnect/reconnect: Correspondence es
customer-céntrico (`EmailThread`/`IncomingEmail` por tenant+customer; `AccountId` es referencia opaca
sin FK).

---

## 4. Visibilidad del buzón (Connectors)

`ITenantEmailAccountRepository.ListVisibleAsync(tenantId, userId, includeOffice)`:
`WHERE TenantId == tenantId AND (OwnerUserId == userId OR (includeOffice AND OwnerUserId == null))`.

`includeOffice` lo calcula el controller: `office.read || write`. Efecto: sin `office.read`, el
empleado no ve el buzón de oficina en la lista, ni puede seleccionarlo como "enviar desde".

---

## 5. Gate de correo de oficina (Correspondence) — semántica **thread-level**

El correo (hilos por cliente) puede **mezclar** buzones (un hilo tiene mensajes recibidos por oficina
y/o enviados desde un personal). Para un empleado **sin `office.read`**:

- Un **hilo se oculta** si NINGÚN mensaje suyo (entrante vivo o enviado) es de un buzón visible.
  Los hilos **solo-oficina** desaparecen de: lista de hilos, Sent, Trash y el contador de no-leídos.
- Un **hilo mixto** (tiene algo de su personal) se muestra **entero** (incluidos los mensajes de
  oficina) — sin "hilos parciales".
- Abrir cuerpo/metadata/adjuntos de un mensaje cuyo hilo no es visible → **404**.

**Cómo se resuelve** (`Api/Authorization/MailboxVisibilityResolver`):
1. `canSeeOffice = office.read || write` → si true, devuelve `null` = **ve todo** (hot path intacto:
   sin llamadas M2M ni EXISTS; es el caso de admins y empleados normales).
2. Si no, pide a Connectors los `accountIds` visibles del usuario (solo sus personales) vía M2M
   `POST /connectors/internal/accounts/visible-ids` (ServiceOnly). **Fail-closed**: si no responde,
   devuelve set vacío (no ve nada) en vez de abrir la oficina.

El set (o `null`) viaja en los query records de lectura. Los repos aplican el filtro solo cuando no
es `null`:
- Hilos (`EmailThreadRepository.ListByCustomerAsync`): `EXISTS` (IncomingEmail vivo ∈ ids ∨ Draft Sent ∈ ids).
- Sent/Trash (listas planas): `WHERE AccountId ∈ ids`.
- Cuerpo/metadata/adjuntos: `MailboxVisibility.CanSeeMessageAsync` → hilo visible (`HasVisibleMessageAsync`).

Sin índices nuevos: los `EXISTS` por-hilo usan los índices `(TenantId, EmailThreadId, …)` existentes,
y el filtro solo corre para empleados restringidos.

---

## 6. Endpoints

Connectors (`/connectors`, vía Gateway):
- `POST /accounts` — Initiate OAuth (`asOffice`).
- `POST /accounts/manual` — conectar/reconectar IMAP+SMTP (`asOffice`).
- `GET /accounts` — lista (personales + oficina si `office.read`).
- `GET /accounts/{id}` — detalle (oficina requiere `office.read`).
- `DELETE /accounts/{id}` — desconectar (soft).
- `POST /accounts/{id}/reauth` — reintentar watch.
- `POST /connectors/internal/accounts/visible-ids` — **M2M ServiceOnly**, ids visibles de un usuario.

Correspondence (`/correspondence`): sin cambios de contrato; los listados/detalle de correo se
**filtran** por `office.read` del JWT (ver §5).

---

## 7. Cómo restringir a un empleado

En *Edit access → Mail* del empleado, **desactivar** "See office mailbox"
(`connectors.accounts.office.read`). El empleado pierde el buzón de oficina y su correo, y conserva su
personal. Como el front cachea permisos en la sesión, el empleado debe **re-loguear** para que el
cambio surta efecto.

---

## 8. Frontend (`features/mail`)

- Vías Office/Personal **excluyentes por permiso**: admin (`write`) → oficina; empleado (`connect_own`)
  → personal. Sin selector.
- Manual: dropdown de proveedor (M365/Gmail/Yahoo/iCloud/Zoho/Otro); email editable solo en oficina.
- `connectors-permissions.ts` (`ConnectorsCapabilities`): `canManageOffice`, `canConnectOwn`.
- Selector de "Send from" (buzón activo) y encabezado del cliente = comboboxes con chip Office/My email.
