# Informe de sesión — 2026-09-26

Plan de **autorización, RBAC y entitlements**, Track A (`BACKENDPERMISSIONS`).

| | |
|---|---|
| **Rama** | `claude/great-heisenberg-j7fnkh` (partió de `claude-trabajo`) |
| **Commits** | 13, **todos empujados** a `origin` |
| **HEAD** | `8a4065a` |
| **Pull Request** | **Ninguno abierto.** Cuando se abra, va hacia `claude-trabajo`, nunca hacia `main` |
| **Fases cerradas** | A0, A2, A3 (parcial), A4 (parcial), A5 (completa), A1 (parcial) |
| **Fases sin empezar** | A6, A7, A8 · Track B (`FRONTENDPERMISSIONS`) · Track C (`CLIENTREDESIGN`) |
| **Tests** | **4951** .NET + **457** Node, 0 fallos. Al empezar la sesión: 4772 + 446 |
| **Migraciones** | 6 generadas. 3 aplicadas contra un SQL Server local desechable; **3 sin aplicar** (ver §4) |

Los dos frontends **no se tocaron**: no hay un solo commit en `FRONTENDPERMISSIONS` ni en
`CLIENTREDESIGN`. Sí los leí, para verificar contra el código real qué consume el CRM antes de cerrar un
endpoint (ver §7, decisión de `GET subscriptions/me`).

---

## 1. Qué había que arreglar

La auditoría del plan (§20) contaba, sobre los 26 microservicios:

- **28 endpoints sin chequeo de ownership** — un id ajeno alcanzaba.
- El bypass de autorización **por nombre de rol**: una cadena `"PlatformAdmin"` en el claim `role`
  abría god-mode, y los nombres de rol los elige el tenant.
- La capa de **denies por usuario** se deshacía sola: los consumidores de 24 servicios recomponían la
  unión de permisos a partir de los roles, y como los denies viven solo en Auth, el permiso quitado
  reaparecía.
- El **403 llegaba vacío**: el frontend no podía distinguir "no tenés el permiso" de "tu plan no incluye
  el módulo".
- **Suspender un tenant no cortaba el acceso**: se revocaba en la base, pero el access token ya emitido
  seguía sirviendo 15 minutos.
- El **empleado recibía 403 en su trabajo diario** por permisos que su rol de sistema nunca tuvo.

---

## 2. Commits

```
8a4065a  fase A1: progreso al dia
b08533e  fase A1: un id ajeno deja de alcanzar, y nadie firma con el PTIN de otro
658ddf4  fase A5: progreso al dia
6751aca  fase A5: un solo bootstrap de acceso, el 403 deja de ser una caja negra y la sesion muere cuando debe
a68eb81  fase A4: progreso al dia
574e0e6  fase A4: un solo techo de delegacion y los roles de portal por fin se pueden editar
89a8ff7  fase A3: progreso, decisiones y contratos al dia
01a0859  fase A3: el empleado deja de recibir 403 en su trabajo diario y Campaigns se separa
c7ccc13  fase A2: progreso, decisiones y contratos al dia
8004694  fase A2: los denies dejan de resucitar y la jerarquia de baja se respeta
1bdbaa8  fase A0: progreso, decisiones y contratos al dia
4caa3fd  fase A0: DMCA solo plataforma, IDOR de llamadas, aislamiento de clientes y limite de tenant M2M
7a85c03  fase A0: PlatformAdmin por actor type y nombres de rol reservados
```

| Fase | Archivos | Tests tras la fase |
|---|---:|---:|
| A0 | 19 + 49 | 4835 .NET · 446 Node |
| A2 | 72 | 4850 |
| A3 | 26 | 4865 · 452 Node |
| A4 | 22 | 4896 |
| A5 | 53 | 4929 · 457 Node |
| A1 | 59 | 4951 |

---

## 3. Fase por fase

### A0 — Hotfixes de seguridad · **completa**

**El bypass por nombre de rol.** `ClaimsPrincipalExtensions.IsPlatformAdmin` aceptaba el claim `role`
con el valor `"PlatformAdmin"`. Los nombres de rol los elige el Tenant Admin, así que crear un rol
llamado `PlatformAdmin` era escalar a god-mode. Ahora se decide **solo por `actor_type`**, que es
inmutable y lo emite Auth. Cerrado en los tres sitios (`BuildingBlocks`, el `ClaimsPrincipalExtensions`
propio de Growth, y el gate de registro de tenants).

**Nombres de rol reservados** (`ReservedRoleNames.cs`, nuevo). Normaliza NFKC → pliega homóglifos
(cirílicos y griegos) → minúsculas → solo alfanuméricos, y rechaza cualquier variante de
`PlatformAdmin`, `TenantAdmin`, `TenantEmployee`, `CustomerPortal`, `Service` y los nombres de los roles
de sistema. Sin eso, `Р1atformAdmin` con una Р cirílica pasaba.

**Cinco agujeros más:**

| Qué | Dónde |
|---|---|
| IDOR del historial de llamadas: `GET /communication/customers/:id/calls` no validaba el customer, así que un cliente leía el historial de otro | `calls.route.ts` |
| Cliente ↔ cliente: un cliente del portal podía abrir chat y llamar a otro cliente de la misma oficina | `start-direct-conversation.ts`, `initiate-call.ts` |
| Token de invitación de meeting reutilizable: servía para desbloquear otro meeting | `join-meeting.ts` |
| Broadcasts que nombran clientes, correos y firmas iban al room del tenant, donde también están los clientes del portal | `emitToTenantStaff` (nuevo) + los 3 consumidores |
| Un share link privado exponía el archivo sin validar el scope del receptor | `ShareResolutionQueries.cs` |
| `cloudstorage.dmca.manage` (nuevo, PlatformOnly): cerrar un takedown DMCA no es trabajo del tenant | `CloudStoragePermissions.cs` + migración |
| Límite de tenant en M2M de Inventory | preparado, **no activado** (ver §7) |

---

### A2 — Capa de denies y propagación de roles · **completa**

**El bug de fondo (G3).** Al cambiar los permisos de un rol, Auth publicaba
`RolePermissionsChangedIntegrationEvent`, que solo dice *qué tiene el rol*. Cada uno de los 24 servicios
recomponía con eso la unión de permisos del usuario. Los denies por usuario viven **solo en Auth**, así
que la unión recompuesta **le devolvía al usuario el permiso que un administrador le había quitado**.

La solución no depende del orden de llegada de los eventos: **Auth hace fan-out por titular**
(`RolePermissionsFanOut.cs`, nuevo) con los códigos **ya efectivos** (roles − denies) y el `perm_v`
subido, y los 24 consumidores dejan de recomponer nada: solo cachean rol → permisos.

Tres consultas en total (titulares, sus roles, sus denies), no una por titular: esto corre también en el
resync de arranque, que recorre los roles de sistema de todos los tenants.

**Denies con razón y vencimiento.** `UserPermissionDeny` gana `Reason` y `ExpiresAtUtc`, más
`ExpiredPermissionDeniesService`, un barrido cada 15 minutos que limpia los vencidos y republica el
acceso. Sin el barrido, un deny "hasta el viernes" quedaba para siempre.

**Jerarquía en la baja.** Un empleado podía dar de baja a un administrador (`User.Hierarchy`), y se
podía dar de baja al último administrador activo de la oficina, dejándola sin quien la administre
(`User.LastAdmin`).

---

### A3 — Baseline del empleado y catálogo · **parcial**

**El empleado recibía 403 en su trabajo diario.** El bundle del rol de sistema Employee no incluía Notes,
`signature.request.cancel` ni `communication.group.create`, y los GET de plantillas de firma exigían un
permiso de escritura.

**`campaigns.manage` se partió en cuatro**: `view` / `manage` / `send` / `senders.manage`, con los 34
atributos actualizados. La migración trae **backfill**: un rol custom que tenía `campaigns.manage`
recibe los tres nuevos, porque si no perdería ver y enviar de un día para otro.

Igual `invoicing.issuer.manage`: editar el emisor legal (razón social, RNC/EIN) sale de
`invoicing.manage`. Cambia con qué identidad fiscal factura la oficina — lo decide el dueño, no el
preparador que emite la factura.

**Flag `IsReserved`** para los permisos declarados que todavía no protegen nada (`portal.miles.use`): no
se ofrecen en el cajón de accesos, para no vender una sensación de control que no existe.

**`portal.calls.use`** se exige solo al actor CustomerPortal, y **detrás de un flag apagado**
(`COMMUNICATION_PORTAL_CALLS_PERMISSION_ENFORCE=false`). El backfill lo hace Auth al arrancar y las
proyecciones convergen por evento: con el flag apagado, el orden de despliegue deja de ser una condición
de carrera.

**Pendiente:** `POST billing/invoices/{id}/email` (necesita plantilla de correo y registrar Billing como
cliente M2M de Notification en configuración de producción) y `correspondence.organize` (COULD).

---

### A4 — Techo de delegación y API de roles · **parcial**

**El techo existía a medias y en el lugar equivocado.** `RolePermissionGuard` solo corría al crear o
editar un rol. Los otros tres caminos por los que un permiso llega de verdad a un usuario —**asignar** un
rol, **invitar** con roles, **aceptar** la invitación— no revalidaban nada.

`PermissionCeiling.cs` (nuevo) es ahora el único techo, con la fórmula de §27 y `PlatformOnly`,
`IsDangerous` e `IsReserved` leídos **explícito**. Verifiqué que las tres banderas están hoy alineadas en
las ~190 filas del catálogo, así que leerlas explícito es **cero cambio de comportamiento**: defensa en
profundidad, con un fitness test que lo mantiene así.

**El techo se parte en dos, a propósito.** La mitad dura (lo que ningún tenant puede conceder jamás) se
exige en los cinco caminos. La comercial (tier + módulo habilitado) solo al escribir la configuración de
un rol: medirla al asignar dejaría roles enteros **inasignables** tras una baja de plan, y §27 pide
dormidos, no borrados. Los roles de **sistema** quedan fuera, porque el bundle raíz de Tenant Admin
incluye permisos `IsDangerous` por diseño y medirlo sería un lock-out del tenant.

**Dos cosas que estaban rotas:**

1. **Un rol con permisos dormidos no se podía guardar.** El techo validaba el set *completo* al
   reguardar, así que tras una baja de plan había que quitar los permisos del módulo perdido para poder
   editar el rol — o sea, borrarlos. Ahora se mide **solo el delta añadido**. Es el mismo bug que tuvo
   GitLab.
2. **Un rol de clientes del portal era inmutable.** Al editarlo se validaba siempre contra el staff, así
   que sus propios permisos de portal se rechazaban. El destino del rol ahora **se persiste**
   (`Role.TargetActorType`) en vez de adivinarse, con un backfill conservador que marca los roles que ya
   eran de portal.

**API nueva** para que el CRM pueda construir la pantalla de roles: `GET /auth/permissions` con las
banderas y `grantable` resuelto contra el plan del tenant; `GET /auth/roles/{id}/users`;
`POST /auth/roles/{id}/reactivate` con fan-out y acción de auditoría propia; y `Role.NameConflict` al
renombrar, en vez de un 409 del índice de la base.

**No se hizo:** duplicar rol (COULD) y la dirección inversa del fitness test (queda para A7).

---

### A5 — Bootstrap de acceso, errores, realtime y sesión · **completa**

**El bootstrap no existía.** Los frontends combinaban `GET /auth/me` (el plan),
`GET /auth/me/effective-access` (escrito para depurar) y `GET /subscriptions/me` (el estado comercial)
para saber qué puede hacer un usuario. Ahora `GET /auth/me/access` responde todo junto, con ETag y 304.

El ETag cubre **todo** el contenido, no solo `perm_v`: un deny que se vence o un módulo que se habilita
mueven el acceso sin mover la versión, y con un ETag ingenuo el sidebar se quedaría viejo.

Nace **consciente de la superficie** (§R.4.1): no declara `[AllowSurface]`, así que un token del Account
del Landing recibe `403 Auth.SurfaceNotAllowed`. Es una **ausencia**, y una ausencia se borra sin que nada
falle, así que hay un test que la fija. Para `CustomerPortal` el bloque comercial es `null`: un cliente
no tiene por qué saber si la oficina está al día con su suscripción.

**El 403 era una caja negra.** Las capas 1 y 2 devolvían `ForbidResult` — 403 con el cuerpo vacío. Las
cuatro capas responden ahora RFC 9457 con `code` y `reason` (`permission` / `actor_type` /
`not_declared` / `surface` / `module`), más `module` cuando falta uno. El cuerpo conserva `code` y
`message` para que los frontends desplegados no se enteren del cambio: RFC 9457 llama `detail` a lo que
este sistema viene llamando `message`.

**Cortar el acceso son tres cosas y faltaban dos.** Suspender un tenant o bloquearlo por facturación
revocaba solo en la base: eso corta el próximo refresh, no el access token ya emitido (G10). La baja y el
offboard sí denylisteaban, pero no anunciaban nada, así que la pestaña abierta se quedaba con la sesión
muerta (R12). Los cuatro caminos pasan ahora por `SessionAccessCutoff`: **denylist → anuncio →
revocación**, en ese orden. Al revés, un cliente avisado podría alcanzar a usar el token viejo.

**`access.changed`** en Communication, a un room nuevo de miembros autenticados: staff **y** clientes del
portal, nunca los invitados de meeting, que no tienen acceso que refrescar. El payload no lleva permisos
ni nada del plan: solo dice "volvé a pedir el bootstrap".

**Una deuda de A2 que encontré abierta en Node.** El consumidor `auth.role.permissions_changed.v1` de
Communication **todavía recomponía la unión de permisos del usuario**, o sea resucitaba los denies (G3).
Es el mismo bug que cerré en los 24 consumidores de .NET; este había quedado fuera del barrido porque
busqué por el patrón de nombre de archivo de C#. Lo cerré, y reescribí el test que afirmaba el
comportamiento viejo poniendo un deny en el set, para que la regresión falle.

---

### A1 — Ownership y permisos de recurso · **parcial (criterio de aceptación cumplido)**

El criterio de la fase era: §20 sin "Ownership faltante" en **Signature, Tasks y Correspondence**. Se
cumple, y además se cerraron CloudStorage y Campaigns.

**Lo más grave: nadie debería firmar con el PTIN de otro.** El PTIN/EFIN identifica a un profesional ante
el IRS (Pub. 1345, §6109(a)(4)) y era **un dato suelto en el cuerpo del request**: cualquier empleado con
`signature.document.sign` firmaba como preparer y el PDF sellado salía con la credencial del colega que
estuviera declarada. Eso no es un problema de permisos: es suplantación.

> **Discrepancia con el plan.** A1 pide "ligar `preparer/sign` al usuario del JWT (PTIN/EFIN **del perfil
> del caller**)". Fui a buscar ese perfil y **no existe**: `PTIN` aparece únicamente dentro de
> `PreparerInfo`, el value object del propio request; `SignatureProfile` es la imagen de la firma, no la
> credencial. No inventé un registro de PTIN por usuario — eso es una decisión de producto (¿quién lo
> administra? ¿se verifica?). Lo resolví ligando el hueco a un usuario: `SetPreparer` lo toma del JWT
> **nunca del cuerpo**, y `MarkPreparerSigned` exige que quien firma sea ese usuario.

| Servicio | Qué faltaba y qué se hizo |
|---|---|
| **Signature** | Los 14 sub-recursos (signers, fields, preparer-fields, preparer-signature, resend, PIN, preparer, preparer/sign) pasan por el chequeo de ownership que ya existía y solo corría en 5 endpoints |
| **Tasks** | `TaskAccessPolicy` en dependencias, adjuntos y series (`TaskSeriesAccessPolicy`, nuevo). La dependencia se valida sobre la **sucesora**: la predecesora no cambia, y exigir permiso sobre ella impediría el caso normal, "mi tarea espera a que termine la de otro" |
| **Correspondence** | El `AccountId` venía del cuerpo sin validar → se podía redactar **y enviar** desde el buzón personal de un colega. Los borradores se leían y listaban sin filtrar por autor. El get-or-create del reply reutilizaba el borrador de cualquiera sobre el mismo hilo, así que el segundo en responder editaba el texto del primero. El gate de buzón ahora corre también en la descarga de adjuntos y en la URL firmada |
| **CloudStorage** | Borrar una carpeta mandaba **todos** sus archivos a la papelera con solo `folder.manage`: administrar carpetas era un borrado de archivos encubierto. Ahora exige `file.delete`, pero solo si hay archivos dentro — una carpeta vacía no tiene nada que proteger |
| **Campaigns** | El scheduler dispara con un actor de sistema y corría con la visibilidad de clientes **abierta**: un preparador que solo ve sus asignados agendaba una campaña y el envío salía a la cartera completa de la oficina. El schedule congela ahora quién lo creó y qué veía |

**El flag `Authorization:ResourceOwnership:Enabled` sigue en `false`.** Los 14 endpoints nuevos de
Signature lo respetan igual que los 5 anteriores, así que encender el ownership sigue siendo una decisión
de operación. Lo que **no** depende del flag es el preparer ligado al usuario, el guard de buzón, los de
Tasks y el de borrar carpetas: ahí no hay ownership de recurso sino permiso o identidad.

---

## 4. Migraciones

| Migración | Servicio | Estado | Qué hace |
|---|---|---|---|
| `AddCloudStorageDmcaManagePermission` | Auth | **aplicada** (SQL Server local desechable) | Siembra `cloudstorage.dmca.manage` |
| `AddUserPermissionDenyReasonAndExpiry` | Auth | **aplicada** (ídem) | `Reason` + `ExpiresAtUtc` en los denies |
| `SplitCampaignsAndAddReservedFlag` | Auth | **aplicada** (ídem) | Columna `IsReserved`, 4 permisos nuevos y **backfill** de roles custom |
| `AddRoleTargetActorType` | Auth | **generada, sin aplicar** | Columna nullable + backfill conservador de roles de portal |
| `AddPreparerUserId` | Signature | **generada, sin aplicar** | Columna nullable `Preparer_UserId` |
| `AddScheduleCreatorVisibility` | Campaigns | **generada, sin aplicar** | `CreatedByUserId` + `CreatorCanViewAllCustomers` |

Las tres primeras se aplicaron **antes** de que pidieras no aplicar migraciones; desde ese momento solo se
generan y se revisan a mano. Las seis son **aditivas**: ninguna borra ni renombra nada.

> **Revisá esto.** El scaffolding de EF puso `defaultValue: false` en `CreatorCanViewAllCustomers` (es el
> default del tipo `bool`) y **lo cambié a mano a `true`**. Con `false`, cada campaña recurrente ya
> agendada pasaría de golpe a enviarse a menos clientes: un cambio de a quién se le manda un correo, en
> silencio y sin que nadie lo pidiera. Está comentado dentro de la migración.

> **También revisá esto.** La migración de A3 reescribí su cuerpo a mano: el scaffolding emitía un
> `UpdateData` por cada una de las ~190 filas del catálogo para poner `IsReserved = false`, que es
> exactamente lo que ya hace el `defaultValue` de la columna. Quedó solo lo que de verdad cambia.

### Secuencia de despliegue

Un orden importa de verdad; el resto es aditivo.

1. **Aplicar las migraciones** de Auth, Signature y Campaigns.
2. **Desplegar Auth** y dejar que `SystemRolePermissionsSyncService` resincronice los roles de sistema:
   publica `RolePermissionsChanged` y el fan-out por titular.
3. **Verificar** en un par de servicios que la proyección de un cliente de portal ya trae
   `portal.calls.use`.
4. **Desplegar Communication** y **solo entonces** poner
   `COMMUNICATION_PORTAL_CALLS_PERMISSION_ENFORCE=true`. Si se invierte, los clientes pierden las
   llamadas.
5. Desplegar el resto de los servicios .NET. El cuerpo del 403 cambia de vacío a RFC 9457, que es
   compatible. Los chequeos de ownership de A1 entran al instante: son 403 donde antes había 200 — es el
   efecto buscado, pero conviene avisar a la oficina piloto.
6. Los frontends migran cuando quieran. Nada los obliga.

---

## 5. Qué produjo esta sesión para los frontends

Todo está en `docs/claude/CONTRATOS_ENTRE_REPOS.md`, que es la fuente de verdad compartida. Lo nuevo:

**`GET /auth/me/access`** — el bootstrap único. `effectivePermissions` (roles − denies, ya resuelto: no lo
recompongas), `modules`, `permissionsVersion`, `entitlementsRevision`, `subscription` (solo staff) y
`ETag`. Manda `If-None-Match` y manejá el 304: se puede pedir en cada navegación sin costo.

**El cuerpo del 403**, RFC 9457. Decidí por `reason`, no por el texto: `reason: "module"` es una pantalla
comercial ("tu plan no lo incluye"); `reason: "permission"` es acceso restringido ("pedile a tu
administrador").

**`access.changed`** y **`session.revoked`** por socket. El primero solo dice "volvé a pedir el
bootstrap"; el segundo, "limpiá y andá a login".

**`GET subscriptions/me/status`** — el estado para el banner de ciclo de vida, sin un solo dato comercial.
El CRM debe migrar el banner acá (B7).

**`GET /auth/permissions` con `grantable`** — es la única bandera que hay que mirar para habilitar una
casilla del picker: ya resuelve la fórmula completa contra el plan del tenant. Las otras sirven para
explicar el motivo.

**Códigos de error nuevos** (13): `Role.NameReserved`, `Role.NameConflict`,
`Role.PermissionNotAssignable`, `Role.NotAssignableToActorType`, `Role.AlreadyActive`,
`Authz.PermissionDenied`, `Authz.ActorTypeNotAllowed`, `Authz.ActorTypeNotDeclared`,
`Signature.Request.PreparerNotSelf`, `SignatureRequest.NotOwner`, `Task.Forbidden`,
`Draft.AccountNotVisible`, `Folder.FileDeletePermissionRequired`, más
`UserPermissionDeny.ExpiryInPast`, `User.Hierarchy`, `User.LastAdmin`,
`Chat.CustomerToCustomerNotAllowed` y `Call.CustomerToCustomerNotAllowed` de A0/A2.

**Dos avisos para la UI:**

1. El **selector de remitente** al redactar tiene que ofrecer solo los buzones visibles del usuario; si
   manda otro, llega `Draft.AccountNotVisible`.
2. El botón **"firmar como preparador"** solo va si el preparer asignado es el usuario actual.

---

## 6. Decisiones tomadas sin intervención humana

31 decisiones, todas en `docs/claude/DECISIONES.md` con contexto, opciones, elección, por qué y si es
reversible. Las que más conviene que revises:

| Decisión | Por qué la tomé así |
|---|---|
| **`GET subscriptions/me` redacta los datos comerciales en vez de responder 403** | Verifiqué en `FRONTENDPERMISSIONS` que el shell del CRM lo pide en cada sesión de cualquier empleado y que el store **se come el error en silencio**. Un 403 habría apagado el banner de ciclo de vida para todos los empleados sin que nadie lo notara: peor que la fuga |
| **`∩ Effective(caller)` de §27 se documenta y no se implementa** | Exigirlo rompería lo que A4 viene a arreglar: el bundle del Tenant Admin excluye los permisos de portal, así que no podría editar un rol de clientes |
| **El preparer se liga a un usuario** | El "perfil con PTIN" que el plan supone no existe (§3, A1) |
| **El techo tiene dos mitades** | Las dos reglas de §27 se contradicen si se aplica el techo entero al asignar: un downgrade dejaría roles inasignables en vez de dormidos |
| **El destino del rol se persiste en vez de adivinarse** | Inferirlo de los permisos convertiría cada edición en una reinterpretación del rol: quitar el último permiso de portal lo volvería de staff en silencio |
| **`portal.calls.use` detrás de un flag apagado** | El orden de despliegue deja de ser una condición de carrera |
| **Un borrador ajeno responde 404, no 403** | Un 403 confirma que ese id existe y sobre qué cliente está escribiendo un colega |
| **El schedule congela la visibilidad, y las filas existentes quedan abiertas** | Asumir lo contrario cambiaría a quién se le envía un correo ya agendado |
| **El fallback pre-RBAC de `UserAccessResolver` se queda** | Ya exige `activeCustomRoles.Count == 0` (G2 cerrado); quitarlo dejaría sin permisos a los usuarios creados antes del modelo |
| **`global.json` pide el SDK 10.0.300 y el instalado es 10.0.112** | No toqué `global.json`: es configuración del equipo. Corrí `dotnet` desde un directorio fuera del repo |

También hay cuatro decisiones de saneo del repo, de la primera revisión: se retiraron tres artefactos de
depuración con tokens reales, se reemplazó un correo personal real en fixtures por uno ficticio, y los
enlaces de Firebase del README **se reportan, no se cambian**.

---

## 7. Qué NO se hizo — explícito

### Fases enteras

| Fase | Qué falta |
|---|---|
| **A6** | Enforcement real de entitlements. El plan manda hacerla **al final y solo con B7 y C5 desplegados**, servicio por servicio, sin encender a la vez el gate de módulos y el de visibilidad por asignación |
| **A7** | Matriz de tests (actor × permission × deny × plan × asignación), log estructurado de 403 con razón, paneles en `authorization.json` |
| **A8** | Documentación: README §41.x, guías de arquitectura y de creación de microservicio, marcar históricos los `RABC\audit\*` |
| **Track B** | `FRONTENDPERMISSIONS` sin tocar. Es el que consume todo lo que A4 y A5 produjeron |
| **Track C** | `CLIENTREDESIGN` sin tocar |

### Dentro de fases cerradas

- **A1, segunda pasada:** mutaciones y reveal de **Customer**, attributions de **Referrals**,
  `terms/publish`, mappings de **Notification**, P2 en `sms/messages` y en las escrituras de **Billing**, e
  indicadores de socket de **Communication** (§20 los marca "bajo").
- **A3:** `POST billing/invoices/{id}/email` — necesita una plantilla de correo y registrar Billing como
  cliente M2M de Notification **en configuración de producción**, que no me corresponde tocar. Y
  `correspondence.organize` (COULD).
- **A4:** duplicar rol (COULD). La dirección inversa del fitness test ("todo código del catálogo se exige
  o es `IsReserved`") va a A7: medí que ~60 códigos se exigen fuera de un atributo — Communication los
  chequea en Node y varios servicios llaman al chequeo de forma imperativa—, así que con un escaneo de
  atributos daría falsos positivos.
- **A0:** el scope M2M de `internal/stock/commit-sale` quedó **preparado y documentado, sin activar**:
  activarlo necesita un cambio en la configuración de producción (`ServiceAuth:Clients`).

### Lo que no pude verificar

| Qué | Por qué |
|---|---|
| **Tests de integración** de Auth, Signature, Tasks, Correspondence, Customer, CloudStorage y Campaigns | Necesitan SQL Server. Desde tu pedido de no usar Docker quedaron sin correr |
| **Tests de integración de CloudStorage** | La imagen de MinIO no se puede descargar: Docker Hub está bloqueado por la política de red del entorno |
| **Las 3 migraciones nuevas contra una base real** | Por el mismo pedido. Las revisé a mano, línea por línea |
| **El barrido de denies vencidos** (`ExpiredPermissionDeniesService`) | Es un `BackgroundService` con `PeriodicTimer`. Sí están cubiertas la consulta que lo alimenta y el fan-out que usa |
| **Los 24 consumidores de proyección, end-to-end** | Necesitan cada servicio levantado |

### Sobre pararme solo por créditos

Me pediste que me detuviera si detectaba que los créditos de la nube se acaban. **No puedo**: no tengo
ninguna herramienta que me diga el saldo. Lo único que puedo hacer —y lo que hice desde ese momento— es
mantener el consumo bajo: sin Docker, sin aplicar migraciones, y corriendo el gate de tests una sola vez
por fase en lugar de iterar.

---

## 8. Cómo verificar todo

Los comandos exactos están en `docs/claude/TESTING.md`. Los de esta sesión:

```bash
# build
dotnet build TaxVision.slnx -c Release

# gate de CI (4951 tests, 0 fallos)
dotnet test TaxVision.slnx --nologo \
  --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"

# Communication (457 tests, 0 fallos)
cd src/Services/Communication && npm run typecheck && npm test

# formato
dotnet csharpier check .
```

Lo que **no** corrí, y cómo correrlo cuando haya base:

```bash
# integración de Auth (necesita SQL Server)
dotnet test deploy/tests/TaxVision.Auth.Tests --filter "FullyQualifiedName~Integration" -m:1
```

---

## 9. Riesgos

| Riesgo | Mitigación |
|---|---|
| **A1 produce 403 donde antes había 200.** Es el efecto buscado, pero es visible para los usuarios | El ownership de recurso sigue tras el flag apagado. Lo que no depende del flag (preparer, buzón, Tasks, carpetas) conviene anunciarlo a la oficina piloto antes |
| **Orden de despliegue de `portal.calls.use`** | Flag apagado por defecto; se enciende cuando el operador verificó las proyecciones |
| **Doble gate:** encender el gate de módulos y la visibilidad por asignación en la misma ventana hace indistinguible qué produjo un 403 | Ventanas separadas, y la métrica `authz.module_decision` para observar |
| **Las migraciones no se probaron contra una base real** | Las tres sin aplicar son aditivas y revisadas a mano. Aplicalas primero en staging |
| **El CRM sigue pidiendo `GET subscriptions/me`** | No se rompe: la respuesta conserva la forma, solo se vacían los campos comerciales para quien no tenga `billing.view`. Migrar a `me/status` en B7 |

---

## 10. Qué sigue

En orden de dependencia:

1. **Track B** (`FRONTENDPERMISSIONS`) — es lo que consume A4 y A5, y **A6 no se puede encender sin B7
   desplegado**. Es el camino crítico.
2. **Track C** (`CLIENTREDESIGN`) — ídem con C5.
3. **A1, segunda pasada** — los servicios que quedaron fuera del criterio de aceptación.
4. **A7** — la matriz de tests y la observabilidad, que es lo que hace que estas seis fases no se
   deshagan por una regresión.
5. **A6** al final, servicio por servicio.
6. **A8** — documentación.

---

## Reglas que se respetaron

- Se partió de `claude-trabajo` y se trabajó en rama propia: `claude/great-heisenberg-j7fnkh`.
- **Ningún** push, merge ni force push a `main` ni a `claude-trabajo`.
- **Ningún** Pull Request abierto. Cuando se abra, va hacia `claude-trabajo`.
- Un commit de código por fase, más uno de documentación.
- Ni un secreto, token ni dato real de cliente en código, tests o seeds: `example.com` y `*.test`.
- Nada irreversible: ninguna migración borra o renombra; la consulta de saneo de nombres de rol quedó
  escrita y **sin ejecutar** contra datos reales.
- `docs/claude/PROGRESO.md`, `DECISIONES.md` y `CONTRATOS_ENTRE_REPOS.md` al día en cada fase.
