# Guía: cómo cablear un módulo nuevo, un plan nuevo o un microservicio nuevo

> Para cualquier dev backend o frontend que tenga que tocar el **eje comercial**: qué incluye cada
> plan, qué se vende suelto y qué le deja hacer el backend a cada oficina. Cubre los tres casos
> reales, con los ficheros exactos, el orden correcto y **las trampas en las que ya caímos**.

---

## 0. Antes de nada: los dos ejes que no hay que confundir

TaxVision decide "¿puedes hacer esto?" con **dos preguntas independientes**:

| Eje | Pregunta | Dónde vive | Si falla |
|---|---|---|---|
| **RBAC** | ¿este usuario tiene el permiso? | `PermissionCatalog` (Auth) + roles | `403 Auth.Forbidden` |
| **Entitlements** | ¿el **plan de la oficina** incluye el módulo de ese permiso? | Subscription → snapshot → proyección local | `403 Authz.ModuleUnavailable` |

Se evalúan **en ese orden y nunca al revés**: primero el permiso, después el módulo. Eso hace que
`Authz.ModuleUnavailable` solo le llegue a quien *podría* si su plan lo incluyera — que es lo que
necesita la pantalla de upgrade, y evita revelarle a un intruso qué módulos tiene contratada la
oficina.

Esta guía trata **el segundo eje**. Para el primero, README §41.

### El recorrido completo de un módulo

```
PermissionCatalog (Auth)          "campaigns.manage"  ──┐
                                                        │ prefijo
PermissionModuleMap (BuildingBlocks) "campaigns." → "campaigns"
                                                        │
PlanModuleCatalog (Subscription)   qué planes lo traen  │
ModuleAddOnCatalog (Subscription)  si se vende suelto   │
                          ↓                             │
            TenantEntitlementSnapshot  (module.campaigns = true/false)
                          ↓  TenantEntitlementsChanged
            TenantPlanCodeProjection   (una copia local por servicio)
                          ↓
            PermissionPolicyProvider → gate → 403 o pasa
```

Nada de esto hace una llamada síncrona a Subscription en el hot path: cada servicio tiene su copia
local, alimentada por evento.

---

## 1. Módulo nuevo

Ejemplo: quieres vender **Payroll** como módulo.

### 1.1 Los permisos (Auth)

`src/Services/Auth/Domain/Roles/PermissionCatalog.cs`

```csharp
public const string PayrollRead = "payroll.read";
public const string PayrollManage = "payroll.manage";

new(
    new Guid("a1000000-0000-0000-0000-000000000200"),   // GUID fijo, nunca se reusa
    PayrollRead,
    "payroll",                                          // agrupador de la UI de roles
    "View payroll runs",                                // ⚠️ EN INGLÉS — es texto de interfaz
    false,
    MinPlanTier: (int)PlanTier.Starter                  // ⚠️ ver la trampa de abajo
),
```

**⚠️ Trampa 1 — `MinPlanTier` tiene que ser ≤ el plan más bajo que incluya el módulo.**
`MinPlanTier` no gatea el runtime: decide qué puede **otorgar** una oficina a un rol propio (el techo
de §27). Si pones el módulo en Starter pero dejas los permisos en `Pro`, el resultado es absurdo: la
oficina tiene Payroll en su plan y no puede dárselo a nadie. Nos pasó exacto al meter `comms` en
Starter — había que bajar 14 permisos de Pro a Starter por migración.

**⚠️ Trampa 2 — las descripciones van en inglés.** Se pintan en el cajón de accesos del CRM, debajo
del código. Hay un test que lo fuerza: `PermissionCatalogTests.Every_description_is_written_in_english`.

Las permissions viven en `HasData`, así que **hace falta migración**:

```bash
dotnet ef migrations add AddPayrollPermissions \
  --project src/Services/Auth/Infrastructure/TaxVision.Auth.Infrastructure.csproj \
  --startup-project src/Services/Auth/Api/TaxVision.Auth.Api.csproj
```

**⚠️ Nunca con `--no-build`.** EF usaría el ensamblado anterior: o no ve tus cambios, o después
`database update` falla con `PendingModelChangesWarning` porque la migración recién escrita todavía no
está compilada.

### 1.2 El mapa prefijo → módulo

`src/BuildingBlocks/Authorization/PermissionModuleMap.cs`

```csharp
("payroll.", "payroll"),
```

**⚠️ Trampa 3 — el orden importa si los prefijos se solapan.** Se evalúa en orden y **gana el primero
que calza**. Hoy el único par solapado es `communication.meeting.` (módulo `meetings`) y
`communication.` (módulo `comms`): el específico va ANTES. Invertirlos no rompe ninguna compilación —
simplemente manda las reuniones a `comms` en silencio y las regala en el plan más barato. Lo fija
`PermissionModuleMapTests.The_more_specific_prefix_wins_over_the_general_one`.

**Si tu módulo no tiene endpoints**, no lo pongas en el mapa y no lo vendas. Un módulo que no gatea
nada pero que el plan cobra es exactamente lo que había con `reports`, `marketing`, `builder`, `irs` y
`miles`: cinco módulos a 29-49 USD/mes sin una sola pantalla detrás.

### 1.3 El espejo de Node

Solo si **Communication** emite permisos de ese módulo:
`src/Services/Communication/src/domain/shared/permission-module-map.ts`. Es una copia manual (Node no
puede referenciar BuildingBlocks). Hay un fitness en .NET que compara las dos listas de **exenciones**
leyendo el fichero TS: `ModuleGateWiringFitnessTests.The_node_exemption_list_mirrors_the_dotnet_one`.

### 1.4 El catálogo comercial

`src/Services/Subscription/TaxVision.Subscription.Domain/Plans/PlanModuleCatalog.cs` — qué planes lo traen.

`src/Services/Subscription/TaxVision.Subscription.Domain/AddOns/ModuleAddOnCatalog.cs` — si se vende suelto:

```csharp
new(new Guid("d1000000-0000-0000-0000-000000000010"), "addon-payroll", "Payroll", "payroll", 29m,
    Availability: AddOnAvailability.Offered),
```

`Availability` es un enum y no un booleano a propósito: hay **dos razones distintas** para no ofrecer
algo y mezclarlas confunde.

| Valor | Significa |
|---|---|
| `Offered` | En la tienda. |
| `NotBuilt` | No existe: ni endpoint ni pantalla. Vuelve cuando se construya. |
| `IncludedInEveryPlan` | Existe, pero su módulo está en TODOS los planes → no hay a quién vendérselo. |

Regla: **lo que trae un plan superior y no trae el inferior, se puede comprar suelto.** Lo fija
`SubscriptionCatalogReconcilerTests.Every_module_sold_in_a_plan_is_offered_as_an_add_on`.

### 1.5 Cómo llega a producción

**No edites solo el seeder.** `SubscriptionPlanCatalogSeeder` y `SubscriptionAddOnCatalogSeeder`
empiezan con `if (await db.X.AnyAsync(ct)) return;` — solo actúan contra una base **vacía**. En
producción ya hay filas, así que el seeder no volverá a correr nunca.

Lo que sí actúa es **`SubscriptionCatalogReconciler`** (`TaxVision.Subscription.Api/Bootstrap/`), un
hosted service que en cada arranque compara el catálogo del código contra la base y aplica las
diferencias. Manda el **mismo `SetPlanModulesCommand`** que manda el endpoint de administración: misma
puerta de dominio, mismo handler, misma publicación del recálculo. El endpoint es una puerta; el
reconciliador es otra que se abre sola tras el despliegue.

Tampoco vale una migración SQL: un `UPDATE` cambiaría las filas **sin publicar versión nueva del plan**
(las suscripciones apuntan a un `PlanVersionId` concreto) y **sin disparar el recálculo**, así que los
24 servicios se quedarían con la proyección vieja y el gate seguiría aplicando los módulos anteriores.

**⚠️ Trampa 4 — `ReviseModules` NO es idempotente.** Publica una versión nueva
(`published.VersionNumber + 1`) sin comparar. Por eso el reconciliador **compara conjuntos antes de
actuar** (`NeedsRevision`). Si algún día escribes otro camino que revise planes, compara primero: sin
eso, cada reinicio publica v2, v3, v4… y cada una dispara un recálculo masivo de todos los tenants.

El reconciliador **sí** crea add-ons que falten y publica los que estén en `Draft`. **No** crea planes
nuevos (ver §2).

### 1.6 Los frontends

| Fichero | Qué añadir |
|---|---|
| `landing/src/app/core/plans/module-labels.ts` | `MODULE_LABELS` (es/en), `MODULE_ICONS` y `OFFERED_MODULES` |
| `landing/src/app/landing/ui/features-grid/features-grid.component.ts` | `MODULE_DESCRIPTIONS` (es/en) |
| `TaxVsion_Front/src/app/core/access/features.ts` | entrada de feature con `module:` + su `MODULE_LABELS` local (el nombre que usa el aviso de "tu plan no incluye…") |
| `TaxVsion_Front/src/app/features/auth/ui/plan-picker-modal/plan-picker-modal.component.ts` | etiqueta del selector de plan |
| `TaxVsion_Front/src/app/features/user-management/ui/edit-access-drawer/access-view.ts` | etiqueta del cajón de accesos |
| `CLIENTTAXPROFRONTEND/src/app/core/access/portal-features.ts` | solo si el CLIENTE lo usa |
| `CLIENTTAXPROFRONTEND/src/app/core/access/authorization-denial.ts` | `portalModuleLabel` — el nombre que reconoce el cliente |

**⚠️ Trampa 5 — `OFFERED_MODULES` no es lo mismo que `MODULE_LABELS`.** El primero es lo que se le
enseña a quien todavía no es cliente (la página pública); el segundo es un **diccionario** y conserva
módulos que ya no se ofrecen, porque una suscripción vieja puede seguir nombrándolos. El grid público
recorría `MODULE_LABELS` y por eso anunciaba features que no existían.

**⚠️ Trampa 6 — gatea por el módulo correcto, no por el "de al lado".** El widget `video-calls` del
dashboard pedía permisos `communication.meeting.*` y gateaba por `comms`: con el split, un Starter veía
el widget y se comía un 403. Si los permisos de una pantalla son de un módulo, el `module:` de esa
entrada es ese.

### 1.7 Checklist

- [ ] Permissions en `PermissionCatalog` + `MinPlanTier` ≤ plan más bajo que trae el módulo
- [ ] Descripciones **en inglés**
- [ ] Migración de Auth generada **sin `--no-build`** y aplicada
- [ ] Línea en `PermissionModuleMap` (orden correcto si solapa)
- [ ] Espejo en Node, si Communication emite esos permisos
- [ ] `PlanModuleCatalog` + `ModuleAddOnCatalog`
- [ ] Etiquetas en los tres frontends
- [ ] `[HasPermission("payroll.…")]` en los endpoints — **sin esto el gate no corre** (§3.4)
- [ ] Tests: `PermissionModuleMapTests`, `SubscriptionCatalogReconcilerTests`

---

## 2. Plan nuevo

Ejemplo: un plan **Team** entre Pro y Enterprise.

### 2.1 Los ficheros

1. `PlanCatalog.cs` (Subscription.Domain) — código + GUID del plan + GUID de la v1.
2. `PlanModuleCatalog.cs` — sus módulos y su entrada en `All`.
3. `SubscriptionPlanCatalogSeeder.cs` — `BuildPlan(...)`: precio, `seatsMax`, `storageQuotaBytes`,
   `maxPendingInvitations`, `PlanTier`.

### 2.2 ⚠️ Los DOS enums `PlanTier`

Son **distintos y no se llaman igual**:

| Dónde | Valores |
|---|---|
| `TaxVision.Subscription.Domain.Plans.PlanTier` | `Standard`, `Pro`, `Enterprise`, `Trial` |
| `TaxVision.Auth.Domain.Tenants.PlanTier` | `Starter`, `Pro`, `Enterprise` |

El de Auth es un **espejo local deliberado** (Auth no depende de Subscription) y lo usa el techo de
permisos. Un plan nuevo hay que añadirlo a los dos, y a `PlanTierResolver.FromPlanCode`, que mapea el
`planCode` proyectado. Un código desconocido resuelve al tier **más restrictivo**: falla cerrado.

### 2.3 ⚠️ El reconciliador NO crea planes

`SubscriptionCatalogReconciler` solo **revisa los módulos de planes que ya existen**; si el plan no
está en la base, lo salta. Un plan nuevo llega a producción por una de estas dos vías:

- **Base nueva**: el seeder lo crea.
- **Base existente**: hay que crearlo por el panel de plataforma / endpoints de administración
  (`/admin/subscription/plans/...`, `PlatformAdmin`).

No está automatizado a propósito: crear un plan es una decisión comercial con precios, no un dato que
un servicio deba materializar solo al arrancar.

### 2.4 Lo que NO hay que tocar

Los frontends. Los planes salen de la API (`GET /plans`) y las etiquetas de módulo ya existen. Si
añades un plan y el front necesita cambios, probablemente estés metiendo el nombre del plan a fuego en
algún sitio — eso es el bug, no la falta de documentación.

### 2.5 ⚠️ Precio contratado vs módulos vigentes

Cuando revisas los módulos de un plan, las oficinas **ya suscritas** reciben el cambio: el snapshot se
recalcula desde la versión **publicada**. Pero el **precio** se mantiene el de la versión que firmaron
(`PlanVersionId`).

Esa distinción es real y costó un bug: `GetAccountSubscriptionHandler` y `AddOnPurchaseEligibility`
leían **los módulos** de la versión contratada, así que el backend concedía el módulo nuevo (el gate
dejaba entrar) mientras la pantalla de Plan no lo listaba y el add-on aparecía **en venta para quien ya
lo tenía**. Regla: **el precio se congela con lo firmado; los módulos no.**

---

## 3. Microservicio nuevo con gate de módulo

Tu servicio ya tiene `[HasPermission]`. Para que además respete el plan:

### 3.1 La proyección local

Tu servicio necesita su `TenantPlanCodeProjection` (tabla + migración) y el consumer de una línea que
delega en el handler compartido — copia el de cualquier servicio gateado, p. ej.
`TaxVision.Campaigns.Application/RateLimiting/Consumers/TenantPlanCodeProjectionConsumer.cs`.

### 3.2 Infrastructure (DI)

```csharp
// Lector de módulos CON su caché e invalidación por evento. No registres el lector EF directo:
// un fitness lo rechaza.
services.AddCachedTenantEntitlementModulesReader<EfTenantEntitlementModulesReader>();
```

Y el invalidador limpia **las dos** cachés que viven sobre la misma fila:

```csharp
internal sealed class TenantPlanCodeCacheInvalidator(
    CachedTenantPlanCodeReader planCode,
    CachedTenantEntitlementModulesReader modules
) : ITenantPlanCodeCacheInvalidator
{
    public async Task InvalidateAsync(Guid tenantId, CancellationToken ct = default)
    {
        await planCode.InvalidateAsync(tenantId, ct);
        await modules.InvalidateAsync(tenantId, ct);
    }
}
```

### 3.3 Program.cs

```csharp
builder.Services.AddModuleGate(builder.Configuration);
```

Registra la fuente de entitlements **y valida la configuración del escalón** en el mismo acto. Van
juntas a propósito: cuando estaban separadas, el registro se copió en 13 servicios y la validación solo
llegó a uno, así que los otros 12 podían arrancar con un módulo mal escrito en `EnforcedModules`,
quedarse en log-only y hacer creer lo contrario.

Al arrancar, el servicio deja su modo en el log. Compruébalo siempre tras un despliegue:

```
Module gate registered: ENFORCING (modules: campaigns).
Module gate registered: log-only (all modules).
```

### 3.4 ⚠️ El gate cuelga de `[HasPermission]`

**Un endpoint sin comprobación de permiso tampoco pasa por el gate.** No es una capa aparte: el hook
vive dentro de `PermissionPolicyProvider`, después de conceder el permiso. Un endpoint "autorizado" solo
por pertenencia al recurso (*¿soy participante de esta conversación?*) es un agujero invisible en el
enforcement de entitlements — encontramos 25 así en Communication.

En Node el equivalente es `checkPermission` (y el `preHandler` `requirePermission` para rutas HTTP).

### 3.5 Despliegue

En `deploy/docker/docker-compose.yml`, junto al resto del `environment` del servicio:

```yaml
Authorization__ModuleGate__Enforce: ${TUSERVICIO_MODULE_GATE_ENFORCE:-false}
Authorization__ModuleGate__EnforcedModules__0: tumodulo
```

Y en `.github/workflows/deploy.yml`, la línea `TUSERVICIO_MODULE_GATE_ENFORCE=${{ secrets.… }}`.

**⚠️ Trampa 7 — la lista va LITERAL, nunca como variable.** Con `Enforce: true` y la lista vacía el
gate aplica **todos** los módulos de golpe, así que un secret vacío saltaría el escalón completo en
silencio. El único grado de libertad es el booleano. Lo vigilan 5 fitness en
`ModuleGateDeployWiringTests`, incluida una que falla si alguien convierte la lista en variable.

### 3.6 El fitness que vas a romper (a propósito)

`ModuleGateWiringFitnessTests.The_thirteen_gated_services_are_still_wired` fija el número en **13**.
Tu servicio lo pondrá en 14 y el test fallará: actualiza el número. Está así para que quitar el gate de
un servicio obligue a decirlo en voz alta, no para molestar.

### 3.7 Checklist

- [ ] `TenantPlanCodeProjection` + migración + consumer
- [ ] `AddCachedTenantEntitlementModulesReader<…>()` en Infrastructure
- [ ] `TenantPlanCodeCacheInvalidator` limpiando las dos cachés
- [ ] `AddModuleGate(builder.Configuration)` en Program.cs
- [ ] `[HasPermission]` en los endpoints que el plan debe gatear
- [ ] compose + `deploy.yml` (booleano por secret, lista literal)
- [ ] Subir el contador de `The_thirteen_gated_services_are_still_wired`
- [ ] Verificar el log de arranque

---

## 4. Encender el gate en producción

Nunca se enciende de golpe. El orden es:

1. **Desplegar con todos los secrets sin poner** → el gate mide y registra sin bloquear.
2. Comprobar que los frontends ya saben traducir `Authz.ModuleUnavailable` (CRM B7, Portal C5).
3. **Observar unos días.** En los logs: `Module gate (log-only)`. Si no aparece nada, ningún tenant
   está pidiendo algo fuera de su plan y encenderlo no romperá a nadie.
4. **Medir el radio antes de cada escalón**: qué tenants tienen ese módulo. Si el módulo lo tienen
   todos los planes, encenderlo no deniega a nadie; si no, sabrás a cuántos afecta antes de hacerlo.
5. Poner **un** secret en `true` y redesplegar. Observar. Siguiente.

Rollback: el secret a `false` y redesplegar. Sin cambios de código.

### `null` y `[]` NO significan lo mismo

| Estado de la proyección | Significa | El gate |
|---|---|---|
| **Sin fila** (`null`) | Todavía no llegó el evento (consistencia eventual) | **no deniega** |
| **Lista vacía** (`[]`) | Se sabe que no tiene ningún módulo (suscripción vencida) | **deniega** |

Confundirlas le daría acceso **completo** a una oficina vencida. Está fijado con tests en
`TenantModuleEntitlementsSourceTests` y en `ModuleGateEnforcePolicyTests`.

---

## 5. Exenciones

Algunos permisos caen bajo un prefijo con módulo **sin ser** la feature que ese módulo vende. Viven en
`PermissionModuleMap.Exempt`:

| Permiso | Por qué |
|---|---|
| `communication.notification.read` | Las notificaciones in-app son transversales: avisan de documentos, firmas y tareas. Gatearlas por `comms` apagaría la campanita de **todo**. |
| `communication.support.open` | Es la vía para SALIR de un problema de plan o de pago. |
| `communication.support.agent` | El otro lado del mismo chat, que atiende la plataforma. No se vende. |

Se resuelven **en el mapa y no en el gate** para que las tres lecturas coincidan sin coordinarse: el
gate en runtime, el techo de plan al otorgar permisos, y el bootstrap `/auth/me/access` que leen los
frontends. Un `null` significa lo mismo en los tres.

Si añades una, **espéjala en Node** — hay un fitness que compara los dos ficheros y falla si divergen.

---

## 6. Referencias

| Qué | Dónde |
|---|---|
| Las 4 capas de autorización | README §41 |
| El gate de módulo, resumen | README §41.14 |
| Modelo completo de los 4 ejes | `Implementaciones/RABC/Guia_Arquitectura_de_Accesos.md` |
| Endpoints de administración del catálogo | `Postman_Collection/TaxVision_Subscription.postman_collection.json` |
| Arquitectura del microservicio Subscription | README §32.1 |
