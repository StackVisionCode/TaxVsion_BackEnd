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
