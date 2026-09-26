# Decisiones tomadas sin intervención humana

Una entrada por decisión, con este formato:

```
## <fecha> — <título corto>
**Contexto:** qué situación la obligó.
**Opciones:** las que se consideraron.
**Elección:** la que se tomó.
**Por qué:** la razón, en una o dos frases. La opción más segura y reversible gana.
**Reversible:** cómo se deshace.
```

---

## 2026-09-26 — Track C va en `CLIENTREDESIGN` (D-A12)

**Contexto:** el plan dejaba abierto en qué repo del Portal se implementa el Track C.
**Opciones:** `CLIENTTAXPROFRONTEND` (el original) o `CLIENTREDESIGN` (el rediseño en curso).
**Elección:** `CLIENTREDESIGN`.
**Por qué:** es la copia que se despliega y la que se prepara para esta implementación; hacerlo en el
original obligaría a portar el trabajo dos veces.
**Reversible:** sí; el Track C no toca el backend.

## 2026-09-26 — Se retiraron del repo tres artefactos de depuración con tokens reales

**Contexto:** `gitleaks` encontró JWT reales commiteados en `scratch_portal.json`, `scratch_state.json`
y `svc_token.txt`, en la raíz del repo.
**Opciones:** dejarlos y solo reportarlos · borrarlos del árbol y añadirlos a `.gitignore` · reescribir
el historial.
**Elección:** borrarlos del árbol, añadir `scratch_*.json`, `svc_token.txt` y `*.log` al `.gitignore`, y
reportar los tokens para que el humano los rote. **No** se reescribió el historial.
**Por qué:** son artefactos de depuración sin valor para el proyecto y contienen credenciales en un repo
remoto. Reescribir el historial es destructivo y estaba prohibido.
**Reversible:** sí, `git revert` del commit los devuelve.

## 2026-09-26 — Un correo personal real en fixtures se reemplazó por uno ficticio

**Contexto:** un correo de Gmail real, capturado de una bandeja de entrada durante una depuración,
aparecía 11 veces en tres archivos de test de Connectors y Correspondence.
**Opciones:** dejarlo · reemplazarlo por un dominio de ejemplo.
**Elección:** reemplazarlo por `manuel.mena@example.com`.
**Por qué:** es dato personal real en un repositorio remoto, y el valor concreto no aporta nada a los
tests.
**Reversible:** sí, pero no hay motivo para revertirlo.

## 2026-09-26 — Los enlaces de Firebase del README se reportan, no se cambian

**Contexto:** el `README.md` enlaza dos PDF de Firebase Storage con su token de descarga en la URL.
**Opciones:** quitar los enlaces · reemplazar el token por un placeholder · dejarlos y reportarlos.
**Elección:** dejarlos y reportarlos para que el humano rote esos tokens.
**Por qué:** son documentación del propio equipo; romper los enlaces sin preguntar es peor que el riesgo
de un token de lectura de dos PDF. La decisión de rotarlos es del humano.
**Reversible:** n/a.

## 2026-09-26 — La rama de trabajo es `claude/great-heisenberg-j7fnkh`, no `claude/rbac-entitlements`

**Contexto:** `PROMPT_NUBE.md` nombra la rama `claude/rbac-entitlements`; el entorno de la sesión en
la nube creó y designó `claude/great-heisenberg-j7fnkh`, partiendo de `claude-trabajo`.
**Opciones:** crear además `claude/rbac-entitlements` · trabajar en la rama designada.
**Elección:** trabajar en `claude/great-heisenberg-j7fnkh`.
**Por qué:** cumple lo que de verdad pide la regla (rama propia, partiendo de `claude-trabajo`, PR
hacia `claude-trabajo`, nunca push a `main` ni a `claude-trabajo`). Dos ramas con el mismo trabajo
solo generan confusión.
**Reversible:** sí; `git branch claude/rbac-entitlements claude/great-heisenberg-j7fnkh` y push.

## 2026-09-26 — `IsPlatformAdmin` se decide solo por `actor_type`, sin exigir el tenant de plataforma

**Contexto:** la viñeta de A0 dice «→ claim `actor_type` (+ PlatformTenant)»; la recomendación final
de §R.5 punto 1 dice solo `ClaimNames.ActorType == nameof(ActorType.PlatformAdmin)`.
**Opciones:** exigir además `tenant_id == PlatformTenant.Id` · solo el actor type.
**Elección:** solo el actor type, como dice §R.5.
**Por qué:** mata el bypass por completo (el nombre del rol deja de importar) sin arriesgar dejar
fuera a un PlatformAdmin cuyo `tenant_id` no sea el tenant de plataforma. Exigir las dos cosas es
un cambio que puede bloquear de más y no aporta seguridad adicional sobre un claim inmutable.
**Reversible:** sí, es una línea en `BuildingBlocks/ActorTypeAuthorization/ClaimsPrincipalExtensions.cs`.

## 2026-09-26 — Consulta de saneo de nombres de rol reservados: escrita, no ejecutada contra datos reales

**Contexto:** §R.5 punto 5 pide buscar roles cuyo nombre normalizado colisione con uno reservado,
antes de activar la validación. La sesión no tiene acceso a ninguna base de datos con datos reales.
**Opciones:** omitirla · dejarla escrita para que la corra el humano.
**Elección:** dejarla escrita (abajo) y verificada contra un SQL Server 2022 vacío levantado en la
sesión, donde devuelve 0 filas.
**Por qué:** es una consulta de solo lectura, pero solo dice algo útil contra los datos reales.
**Reversible:** n/a, solo lee.

```sql
-- Roles de tenant cuyo nombre colisiona con un nombre reservado de la plataforma, o que trae
-- caracteres fuera de ASCII (candidatos a homoglifo). Solo lee.
WITH normalized AS (
    SELECT
        r.Id, r.TenantId, r.Name, r.IsSystem, r.IsActive,
        LOWER(
            REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                r.Name, ' ', ''), '-', ''), '_', ''), '.', ''), CHAR(9), ''), NCHAR(0x00AD), '')
        ) AS NormalizedName
    FROM dbo.Roles AS r
)
SELECT
    n.TenantId, n.Id AS RoleId, n.Name, n.NormalizedName, n.IsActive,
    CASE
        WHEN n.NormalizedName IN ('tenantemployee','tenantadmin','customerportal','platformadmin','employee')
            THEN 'colisiona con un nombre reservado'
        ELSE 'contiene caracteres fuera de ASCII (posible homoglifo)'
    END AS Motivo
FROM normalized AS n
WHERE n.IsSystem = 0
  AND (
        n.NormalizedName IN ('tenantemployee','tenantadmin','customerportal','platformadmin','employee')
        OR n.Name COLLATE Latin1_General_BIN LIKE '%[^ -~]%'
      )
ORDER BY n.TenantId, n.Name;
```

## 2026-09-26 — `TenantCustomers` sin destinatarios se resuelve por el dueño del archivo, y el personal no gana acceso

**Contexto:** A0.8 pide que un link privado `TenantCustomers` sin destinatarios sea «solo staff o con
`CanAccess`». Hoy el personal **no** entra a un link `TenantCustomers` (la comprobación exige un
actor de portal con `customer_id`).
**Opciones:** (a) tal cual la letra: abrirlo también al personal · (b) solo cerrar el lado del
cliente, dejando al personal como está.
**Elección:** (b).
**Por qué:** un hotfix de seguridad no debe **conceder** accesos nuevos. El agujero era que
cualquier cliente del tenant entraba; eso queda cerrado con `CanAccess`. Abrirlo al personal es un
cambio de producto, no de seguridad.
**Reversible:** sí, es una rama del `switch` en `ShareResolutionQueries.Authorize`.

## 2026-09-26 — El scope M2M de `internal/stock/commit-sale` queda preparado, no activado

**Contexto:** A0.9 pide «`internal/stock/commit-sale` con `ServiceOnly` + scope». El endpoint ya
tiene `[AllowActorTypes(ActorType.Service)]`, que es el `ServiceOnly` de la capa 2. Falta el scope
por cliente M2M.
**Opciones:** agregar el scope ahora · dejarlo documentado para una ventana coordinada.
**Elección:** dejarlo documentado, sin activarlo.
**Por qué:** el mecanismo de scopes (`HasServiceScope`) hoy existe **solo** en Growth, y exigir un
scope obliga a agregarlo en los tres sitios de configuración M2M (user-secrets local,
`docker-compose.yml` de producción y el registro del cliente en Auth). Si producción se despliega sin
ese cambio, Billing deja de poder emitir facturas con productos rastreados. Es exactamente el riesgo
de §R.7 y el tipo de cambio que `PROMPT_NUBE.md` §2 manda dejar preparado sin ejecutar.
**Reversible:** n/a, no se cambió nada. Lo que hay que hacer, en orden: (1) `InventoryServiceScopes`
con `stock.commit_sale`; (2) agregar el scope al cliente `Platform` en los tres sitios; (3) recién
entonces `[HasServiceScope(...)]` en el endpoint.

## 2026-09-26 — La presencia de usuarios sigue en el room del tenant, no en el de personal

**Contexto:** A0.7 manda los broadcasts de tenant a una sala de personal. `chat.presence.changed`
es uno de esos broadcasts.
**Opciones:** moverlo al room de personal · dejarlo en el room del tenant.
**Elección:** dejarlo en el room del tenant.
**Por qué:** el portal del cliente **consume** `presence.changed` (`CLIENTREDESIGN`
`customer-taxtalk` y `customer-dashboard`) para saber si su preparador está en línea. Moverlo rompe
una función viva del portal, que es justo lo que prohíbe §R.7. Los tres broadcasts que sí llevan
metadata de la oficina (correo entrante, cliente cambiado, solicitud de firma cambiada) sí se
movieron.
**Reversible:** sí, es el método del emisor que usa cada consumidor.

## 2026-09-26 — El SDK instalado es 10.0.112 y `global.json` pide 10.0.300

**Contexto:** el entorno no tenía .NET y el único paquete disponible (archivo de Ubuntu 24.04) es
`dotnet-sdk-10.0` 10.0.112. `global.json` fija 10.0.300, que no existe para instalar desde acá.
**Opciones:** cambiar `global.json` · resolver el SDK desde fuera del repo.
**Elección:** **no** se tocó `global.json`. El build y los tests se lanzan con el cwd fuera del repo
y la ruta absoluta de la solución (`dotnet build /ruta/TaxVision.slnx`), que es donde .NET busca el
`global.json`. Para `dotnet-ef` se oculta el archivo temporalmente y se restaura en el mismo comando.
**Por qué:** `global.json` es configuración del repo y del CI; cambiarla por una limitación del
entorno de la sesión sería un cambio invisible y de alcance global.
**Reversible:** n/a, no se cambió nada del repo.

## 2026-09-26 — El fallback pre-RBAC de `UserAccessResolver` se queda

**Contexto:** A2 pedía «fallback solo para usuarios pre-RBAC marcados, o eliminarlo».
**Opciones:** marcarlos con una bandera nueva y limitar el fallback a esos · eliminarlo · dejarlo.
**Elección:** dejarlo como está.
**Por qué:** el agujero real (G2: el fallback ignoraba los denies) ya está cerrado — hoy exige
`permissions.Count == 0 && activeCustomRoles.Count == 0`, así que un usuario con roles y todo
denegado se queda vacío, que es lo correcto. Lo que queda es un usuario **sin ningún rol**, y para
él el fallback es lo único que le da acceso: quitarlo lo deja fuera del sistema. Marcar quiénes son
«pre-RBAC» exige una migración de datos que solo el humano puede validar contra producción.
**Reversible:** n/a, no se cambió nada.

## 2026-09-26 — `PUT /auth/users/{id}/permission-overrides` acepta dos formas del cuerpo

**Contexto:** los denies ganaron razón y vencimiento, y el contrato actual del endpoint es una lista
plana de ids que el CRM desplegado ya usa.
**Opciones:** cambiar la forma del cuerpo · aceptar las dos.
**Elección:** aceptar las dos: `deniedPermissionIds` (ids, sin metadata) y `denies`
(`{permissionId, reason?, expiresAtUtc?}`). Si viene `denies`, manda ese.
**Por qué:** cambiar la forma rompe el CRM que hay hoy en producción, y el drawer que va a usar la
forma nueva es B9, que todavía no existe. Dos formas es fea pero es la única que no rompe nada.
**Reversible:** sí; cuando B9 esté desplegado se puede retirar `deniedPermissionIds`.

## 2026-09-26 — `POST billing/invoices/{id}/email` queda sin construir

**Contexto:** A3.4 pide un endpoint dedicado para enviarle la factura al cliente, bajo
`invoicing.manage`, en vez de que el CRM use el `notification.email.send` genérico (que el empleado
no tiene, de ahí el 403 que reporta §22).
**Opciones:** construirlo ahora · dejarlo documentado.
**Elección:** dejarlo documentado; sí se hizo la otra mitad de A3.4 (el emisor legal).
**Por qué:** no es un arreglo de autorización, es una función nueva: hace falta (1) una plantilla de
correo para la factura, que no existe en Notification, y (2) registrar a Billing como cliente M2M de
Notification en los **tres** sitios de configuración, incluido `deploy/docker/docker-compose.yml` de
producción. Inventar la plantilla y el contrato del envío sin el humano es justo lo que el prompt
prohíbe, y dejarlo a medio cablear rompería el botón en vez de arreglarlo.
**Reversible:** n/a, no se cambió nada. Lo que hay que hacer, en orden: (1) plantilla de factura en
Notification; (2) registrar el cliente M2M `Billing` con el scope de envío en user-secrets y en el
compose de producción; (3) `POST billing/invoices/{invoiceId}/email` con
`[HasPermission(InvoicingPermissions.Manage)]` que llame a Notification con esa plantilla.

## 2026-09-26 — El emisor legal sale de `invoicing.manage` a su propio permiso

**Contexto:** A3.4 pide que el PUT de `IssuerProfile` quede bajo un permiso administrativo
(`billing.issuer.manage` o `settings.manage`).
**Opciones:** reutilizar `settings.manage` · crear `invoicing.issuer.manage`.
**Elección:** crear `invoicing.issuer.manage`.
**Por qué:** `settings.manage` es "configuración de la oficina" en general; el emisor legal es una
cosa concreta y auditable, y con su propio permiso el administrador puede delegarlo sin entregar
toda la configuración. Least privilege real en vez de agrupar.
**Reversible:** sí; el permiso es una fila del catálogo y un atributo. **Consecuencia deliberada:**
un empleado con `invoicing.manage` ya no edita el emisor legal. Los roles custom que lo tenían
conservan la capacidad: la migración les concede el permiso nuevo.

## 2026-09-26 — `portal.calls.use` se aplica detrás de un flag apagado

**Contexto:** §R.6 recomienda aplicarlo (opción a) con backfill, y advierte que sin backfill
**todos** los clientes existentes pierden las llamadas.
**Opciones:** aplicarlo directo en el mismo despliegue · aplicarlo detrás de un flag apagado.
**Elección:** flag `COMMUNICATION_PORTAL_CALLS_PERMISSION_ENFORCE`, default `false`.
**Por qué:** el backfill lo hace `SystemRolePermissionsSyncService` al arrancar Auth, y las
proyecciones de los 24 servicios convergen por evento. Eso no es instantáneo ni está garantizado que
ocurra antes de que Communication se despliegue. Con el flag apagado, el orden de despliegue deja de
ser una condición de carrera: se enciende cuando el operador verificó las proyecciones. Es el mismo
patrón que ya usa `COMMUNICATION_ASSIGNMENT_VISIBILITY_ENABLED` en este servicio.
**Reversible:** sí, es una variable de entorno.

## 2026-09-26 — Los gates de actor se abren en vez de volver los permisos no delegables

**Contexto:** A3.6 da dos salidas para `sms.manage`, `notification.log.view`, `users.invite` y el
`audit.view` de Subscription: que el permiso deje de ser delegable, o que el endpoint admita
`TenantEmployee`.
**Opciones:** cerrar el permiso · abrir el gate de actor.
**Elección:** abrir el gate de actor.
**Por qué:** cerrar el permiso rompe los roles custom que ya lo tuvieran (el guard de actor type
empezaría a rechazar esos roles al editarlos). Abrir el gate no concede nada: el permiso sigue
siendo la barrera y ninguno de los cuatro está en el bundle por defecto del empleado, así que el
único efecto es que una delegación deliberada por fin funcione.
**Reversible:** sí, es un atributo por endpoint. Para `users.invite` hizo falta además extender
`CreateInvitationHandler.CanInvite`: un empleado con el permiso da de alta empleados y clientes,
nunca otro administrador (eso ya exige `roles.manage` efectiva por A2).


## 2026-09-26 — El techo tiene dos mitades: la dura se exige siempre, la comercial solo al escribir

**Contexto:** §27 pide aplicar `Grantable(...)` también al **asignar** un rol, al **invitar** con
roles y al **aceptar** la invitación, caminos que hoy no revalidan nada del techo. Pero la misma §27
manda que la configuración anterior a un downgrade quede **dormida**, no borrada: "No se borra de
roles, asignaciones ni denies".
**Opciones:** aplicar el techo completo en esos tres caminos · aplicar solo la parte que no depende
del plan · no aplicar nada (dejarlo como está).
**Elección:** dividirlo. `IsNeverGrantable` (no asignable por el tenant, `PlatformOnly`,
`IsDangerous`, `IsReserved`) se exige en **todos** los caminos; el tier y el módulo habilitado se
exigen solo al crear o editar los permisos de un rol.
**Por qué:** las dos reglas de §27 se contradicen si se aplica el techo entero al asignar. Un tenant
que baja de Pro a Starter tendría de golpe roles enteros **inasignables**, no dormidos — y eso es
justo el bug que §27 describe. La mitad dura, en cambio, no depende de nada que el tenant pueda
comprar: si un permiso pasó a ser de plataforma después de crear el rol, repartirlo a usuarios nuevos
es escalada, no configuración dormida.
**Reversible:** sí, son dos funciones separadas en `PermissionCeiling`; ampliar o reducir lo que se
exige en cada camino es cambiar una llamada.
**Detalle importante:** los roles de **sistema** quedan fuera del techo en esos tres caminos
(`ValidateRolesNeverGrantable` filtra `IsSystem`). El bundle raíz de Tenant Admin incluye permisos
`IsDangerous` por diseño; medirlo contra el techo del tenant dejaría el rol "Tenant Admin"
inasignable, que es un lock-out del tenant entero.

## 2026-09-26 — `∩ Effective(caller)` de §27 se documenta y no se implementa

**Contexto:** la fórmula de §27 termina con `∩ Effective(caller)`, anotado como "solo relevante si
`roles.manage` llegara a delegarse".
**Opciones:** implementarlo ya · dejarlo documentado sin implementar.
**Elección:** documentado y sin implementar, explícito en el doc-comment de `PermissionCeiling`.
**Por qué:** exigirlo hoy rompe la gestión de roles del portal. El bundle del rol de sistema Tenant
Admin excluye los permisos `IsCustomerPortal` (`SystemTenantAdminRootPermissions`), así que un Tenant
Admin no tiene —ni debe tener— `portal.folders.view`. Con la intersección activa no podría crear ni
editar un rol de clientes, que es exactamente lo que A4 viene a habilitar (G8). Hoy además no aporta
nada: solo el rol raíz tiene `roles.manage` y ese rol ya está por encima de todo lo delegable.
**Reversible:** sí, es un parámetro que no se pasa.

## 2026-09-26 — El destino del rol se persiste en vez de adivinarse por sus permisos

**Contexto:** G8 pide que un rol de portal se pueda editar. Hasta ahora `SetRolePermissionsHandler`
validaba siempre contra "staff", así que al reguardar un rol de clientes sus propios permisos se
rechazaban: quedaba inmutable.
**Opciones:** inferir el destino de los permisos que el rol ya tiene · persistir el destino declarado
al crearlo.
**Elección:** persistirlo — `Role.TargetActorType`, columna nullable nueva.
**Por qué:** inferirlo es ambiguo justo cuando importa. Un rol vacío o uno que mezcla permisos no
tiene destino deducible, y "todos sus permisos son de portal ⇒ es de portal" convierte cada edición
en una reinterpretación del rol: quitar el último permiso de portal lo volvería de staff en silencio.
El destino es una decisión del administrador, así que se guarda cuando la toma. La creación ya
recibía `TargetActorType`; solo faltaba no tirarlo.
**Reversible:** sí. La columna es nullable y `null` conserva **exactamente** el comportamiento
anterior. El backfill de la migración es conservador: solo marca roles custom que ya tenían al menos
un permiso y **todos** de portal, que es precisamente el conjunto que hoy está roto.

## 2026-09-26 — `GET /auth/roles/{id}/users` devuelve solo titulares activos

**Contexto:** la UI necesita saber a quién afecta desactivar un rol. El repositorio ofrece
`CountUsersInRoleAsync` (cuenta todos) y `GetActiveByRoleAsync` (los activos).
**Opciones:** todos los titulares · solo los activos.
**Elección:** solo los activos, reutilizando `GetActiveByRoleAsync`.
**Por qué:** es la misma lista que recibe el fan-out de permisos (A2), así que lo que la UI muestra y
a quién se le avisa del cambio no pueden divergir. Un usuario dado de baja no es "gente con este rol"
para quien está decidiendo si lo desactiva. Además evita un método nuevo de repositorio y sus ~15
dobles de test.
**Reversible:** sí; agregar los inactivos es un método más y un campo más en la respuesta.

## 2026-09-26 — `GET subscriptions/me` redacta los datos comerciales en vez de responder 403

**Contexto:** A6.4 pide "lecturas completas bajo `billing.view`". Ese endpoint no exigía ningún
permiso, así que cualquier empleado veía el precio del plan, el nombre comercial, los límites, el
motivo de suspensión y el último fallo de cobro de la oficina.
**Opciones:** ponerlo entero detrás de `billing.view` · detrás del permiso pero con un flag de
configuración para el rollout · dejar el endpoint abierto al staff y vaciar los campos comerciales.
**Elección:** la tercera — misma forma de respuesta, campos comerciales vacíos sin el permiso, y un
endpoint nuevo y limpio (`GET subscriptions/me/status`) para lo que el banner necesita.
**Por qué:** lo verifiqué en `FRONTENDPERMISSIONS`, no lo supuse. El shell del CRM
(`app-shell.component.ts` → `SubscriptionStatusStore.load()`) pide este endpoint **en cada sesión de
cualquier empleado**, y de toda la respuesta solo lee tres campos: `status`, `billingAccessBlocked` y
`gracePeriodEndsAtUtc`. Un 403 no daría un error visible: el store se lo come en silencio, así que el
banner de ciclo de vida simplemente **dejaría de aparecer** para todos los empleados — una regresión
peor que la fuga, porque nadie la notaría. `billing.view` además es `IsDangerous`, así que el bundle
del rol de sistema Employee no lo tiene ni lo va a tener. §R.7: ningún chequeo nuevo puede quitar
acceso que hoy funciona.
**Reversible:** sí. Cuando el CRM migre a `me/status` (B7), poner el endpoint completo detrás del
permiso es agregar un atributo.
**Nota:** `GET subscriptions/plan-change` sí pasó a exigir `billing.view` — nombra el plan destino y su
precio, y ningún frontend lo consume todavía (verificado con grep en los dos repos), así que cerrarlo
no le quita acceso a nadie.

## 2026-09-26 — El cuerpo del 403 es RFC 9457 pero conserva `code` y `message`

**Contexto:** A5.3 pide `{code, reason, module?}` en formato RFC 9457. El 403 de las capas 1 y 2 salía
con el **cuerpo vacío** (`ForbidResult`), y los frontends desplegados leen `{code, message}` (la forma
de `Error`).
**Opciones:** RFC 9457 puro (`type`, `title`, `status`, `detail` + extensiones) · el superconjunto con
`code` y `message` además de `detail`.
**Elección:** el superconjunto.
**Por qué:** RFC 9457 llama `detail` a lo que este sistema viene llamando `message` en todas sus demás
respuestas de error. Emitir solo `detail` obligaría a cambiar el parser de los dos frontends **en el
mismo despliegue** que el backend, y el plan (B1) todavía no está hecho. Duplicar un string corto es
barato; una ventana donde los errores no se pueden leer, no.
**Reversible:** sí, es un campo más en las extensiones del ProblemDetails.

## 2026-09-26 — `access.changed` va a un room nuevo de miembros, no al room del tenant

**Contexto:** A6.2 dice "usuario y tenant autenticado" y §R.4.1 remata: el evento **nunca** a Guests.
El room `t:{tenantId}` que ya existía tiene a todos: staff, clientes del portal **y** los invitados de
meeting, que entran con un ticket de un solo uso.
**Opciones:** emitir a `t:{tenantId}` · emitir solo a `:staff` · emitir N veces, una por usuario de
portal activo · agregar un room de miembros autenticados.
**Elección:** un room nuevo `t:{tenantId}:members`, al que se une todo principal que llegó con un
token real.
**Por qué:** `t:{tenantId}` incluiría a los invitados, que no tienen cuenta ni acceso que refrescar.
Solo `:staff` dejaría afuera a los clientes del portal, cuyas áreas dependen de los módulos
`documents`, `planner` y `comms` — un cambio de plan les cambia el portal. Emitir por usuario es O(n)
consultas por cada cambio de plan. El room lo resuelve en un `emit`, y es el mismo patrón que ya usa
`:staff`.
**Reversible:** sí. El room se llena en el siguiente handshake de cada socket; hasta entonces el evento
simplemente no llega a nadie, que es el comportamiento de hoy.

## 2026-09-26 — El corte de acceso es denylist → anuncio → revocación, en ese orden

**Contexto:** G10 del plan: la suspensión del tenant y el bloqueo por facturación revocaban **solo en
la base**, así que el access token ya emitido seguía sirviendo hasta 15 minutos. R12: la baja y el
offboard sí denylisteaban, pero no anunciaban nada, así que la pestaña abierta se quedaba con la
sesión muerta.
**Opciones:** repetir los tres pasos en los cuatro llamadores · un helper compartido.
**Elección:** `SessionAccessCutoff`, un helper con dos métodos (por usuario y por tenant).
**Por qué:** los cuatro sitios tienen que hacer lo mismo y el orden importa: primero se cierra la
puerta, después se avisa. Al revés, un cliente avisado podría alcanzar a usar el token viejo antes de
que la entrada de la denylist exista. Un helper con el orden y un test que lo fija evita que el quinto
llamador lo haga al revés.
**Reversible:** sí; el anuncio es best-effort por contrato (un Redis caído no impide revocar) y la
denylist tiene TTL.

## 2026-09-26 — El consumer de roles de Node deja de recomponer la unión (deuda de A2)

**Contexto:** al tocar los consumers de Communication para `access.changed` encontré que
`auth.role.permissions_changed.v1` **todavía recomponía la unión de permisos del usuario** a partir de
los permisos cacheados de sus roles.
**Por qué importa:** es G3, el mismo bug que se cerró en los 24 consumers equivalentes de .NET en A2.
La capa de denies vive solo en Auth, así que una unión armada con los roles le devuelve al usuario
justo el permiso que un administrador le quitó. En Node había quedado afuera del barrido.
**Elección:** quitar el recompute. El handler cachea rol → permisos (que el gate de módulo y el
diagnóstico usan) y avisa a los titulares conectados; los códigos efectivos los aplica el handler de
`auth.user.roles_changed.v1`, que es el fan-out por titular que Auth publica desde A2.
**Reversible:** sí, pero no debería revertirse: el test que afirmaba el comportamiento viejo se
reescribió para afirmar el nuevo, con el deny en el set para que la regresión falle.

## 2026-09-26 — El preparer se liga a un usuario, porque no existe el "perfil con PTIN" que el plan supone

**Contexto:** A1 pide "ligar `preparer/sign` al usuario del JWT (PTIN/EFIN del perfil del caller)".
Busqué ese perfil: **no existe**. `PTIN` aparece solo en Signature, dentro de `PreparerInfo`, que es un
value object del propio request; `SignatureProfile` es la imagen de la firma, no la credencial
profesional. No hay de dónde leer el PTIN del caller.
**Opciones:** dejar el hallazgo abierto · crear un registro de PTIN por usuario (nuevo agregado, nueva
API, nuevo CRUD) · ligar el hueco del preparer a un usuario concreto.
**Elección:** la tercera. `PreparerInfo.UserId` (columna nullable nueva); `SetPreparer` lo toma del JWT,
nunca del cuerpo; `MarkPreparerSigned` exige que quien firma sea ese usuario.
**Por qué:** el riesgo real no es que el PTIN sea inventado —eso lo valida el formato y, en última
instancia, el IRS— sino que **un empleado firme con el PTIN de un colega**. Ligar el hueco al usuario lo
cierra con una columna, sin inventar un agregado ni una pantalla que nadie pidió. Crear el registro de
PTIN es una decisión de producto (¿lo administra el dueño? ¿se verifica?), no de esta fase.
**Reversible:** sí, y el default es seguro: `UserId` nulo (todos los requests que ya existen) conserva
exactamente el comportamiento anterior. Sin eso, una firma en curso quedaría imposible de completar.

## 2026-09-26 — La dependencia entre tareas se valida sobre la sucesora, no sobre las dos

**Contexto:** agregar o quitar una dependencia toca dos tareas: la sucesora (la bloqueada) y la
predecesora (la que bloquea).
**Opciones:** exigir permiso de mutación sobre las dos · solo sobre la sucesora.
**Elección:** solo sobre la sucesora.
**Por qué:** `TaskAccessPolicy` ya establece la regla del servicio — "leerlas es de toda la firma,
moverlas no". La predecesora no cambia: nadie le mueve el estado, la fecha ni el responsable; solo se la
lee para saber si está cerrada. La que cambia —y la que puede quedar trabada para siempre— es la
sucesora. Exigir permiso sobre las dos impediría el caso normal: "mi tarea espera a que termine la de
otro", que es la razón de existir de la feature.
**Reversible:** sí, es una llamada más al mismo guard.

## 2026-09-26 — Borrar una carpeta con contenido exige el permiso de borrar archivos

**Contexto:** `DELETE /folders/{id}` pedía solo `cloudstorage.folder.manage` y mandaba a la papelera
**todos** los archivos del subárbol.
**Opciones:** dejarlo como está · pedir `file.delete` siempre · pedirlo solo si hay archivos dentro.
**Elección:** la tercera.
**Por qué:** "administrar carpetas" era un borrado de archivos encubierto: con un permiso de
organización se podía vaciar el contenido de la oficina. Pero exigir `file.delete` también para una
carpeta **vacía** rompería el caso legítimo de quien solo ordena el árbol y no puede borrar nada de
nadie — y no hay nada que proteger ahí. El chequeo cuelga de si hay archivos, que es exactamente la
condición que hace peligrosa la operación.
**Reversible:** sí. El comando recibe el permiso ya resuelto con `CanDeleteFiles = true` por defecto, así
que un llamador que no lo informe se comporta como antes.

## 2026-09-26 — El schedule de campaña congela la visibilidad, y las filas existentes quedan abiertas

**Contexto:** el scheduler dispara con un actor de sistema, así que cada corrida agendada corría con
`CanViewAllCustomers = true`: un preparador que solo ve sus clientes asignados agendaba una campaña y el
envío salía a la cartera completa de la oficina (§20, "P2 evadido").
**Opciones:** resolver la visibilidad en cada disparo (pedirla a Auth por el creador) · congelarla al
agendar.
**Elección:** congelarla, junto con el `CreatedByUserId`.
**Por qué:** la decisión de audiencia se toma al agendar; el disparo solo la ejecuta. Resolverla en cada
corrida haría que una campaña recurrente cambiara de audiencia en silencio cuando al creador le tocan los
permisos —o que dejara de enviarse si el creador se va de la oficina—, y metería una llamada a Auth en un
job de fondo.
**Reversible:** sí, son dos columnas aditivas.
**Detalle importante:** el scaffolding de EF generó `defaultValue: false` para
`CreatorCanViewAllCustomers` (el default del tipo `bool`). **Lo cambié a mano a `true`.** Con `false`,
cada campaña recurrente ya agendada pasaría de golpe a enviarse a menos clientes: un cambio de a quién se
le manda un correo, en silencio y sin que nadie lo pidiera (§R.7).

## 2026-09-26 — Un borrador ajeno responde 404, no 403

**Contexto:** leer y listar borradores no filtraba por autor. Un borrador es un correo a medio escribir.
**Opciones:** 403 (existe pero no es tuyo) · 404 (como si no existiera).
**Elección:** 404, y el listado simplemente no lo incluye.
**Por qué:** el 403 confirma que ese id existe en el tenant y, con el `customerId` de la URL, sobre qué
cliente está escribiendo un colega. Es el mismo criterio que el servicio ya aplica a
`ClientRequest.NotFound` y `Reminder.NotFound`. El filtro del listado va en la **consulta**, no después
de paginar: filtrar en memoria dejaría los totales y las páginas mintiendo.
**Reversible:** sí; el filtro es un parámetro opcional del repositorio.
