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

