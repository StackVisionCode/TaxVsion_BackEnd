# Endurecimiento RBAC / Authorization — Defensa técnica

**Fecha:** 2026-07-23
**Alcance:** 10 fases + Fase 7.5 (perm claim removal)
**Estado:** completo — 1963/1963 tests, 0 regresiones
**Plan fuente:** `Implementaciones/RABC/RBAC_Hardening_Plan.md` (2210 líneas)

---

## 1. Problema

El mecanismo de autorización que ya teníamos (4 capas: `[HasPermission]` + `[AllowActorTypes]` + `ActorTypeRoleGuard` + NetArchTest) era **correcto en diseño** — coincide con la industria (WorkOS, GitHub Enterprise, AuthKit) — pero tenía **10 huecos concretos** en la implementación:

| # | Hueco | Severidad | Impacto |
|---|---|---|---|
| 1 | `[Authorize(Roles="TenantEmployee,TenantAdmin")]` AND-eado con `[AllowActorTypes(..., PlatformAdmin)]` en 22 endpoints de Customer | 🔴 Crítico | PlatformAdmin rechazado en customers — bug funcional |
| 2 | `SystemTenantAdmin` = **god-role dinámico** (cualquier permiso nuevo del catálogo entraba automático) | 🔴 Crítico | Escalada silenciosa — el chequeo defensivo inline en `SignatureAdminController` lo probaba |
| 3 | `ActorTypeRoleGuard` no corría en `CreateRole` / `SetRolePermissions` | 🟠 Alto | Rol malformado (mezcla staff+customer) persistía en DB |
| 4 | **Cero** implementaciones de resource ownership → cualquier miembro del tenant revocaba shares/signatures de otros | 🔴 Crítico | OWASP A01 / CWE-639 real |
| 5 | Solo Signature+Growth tenían `HasQueryFilter` global de tenant — los otros 12 confiaban 100% en el handler | 🟠 Alto | Un olvido humano = leak cross-tenant silencioso |
| 6 | Session denylist en Redis solo aplicado en 3 servicios (Auth/PaymentApp/PaymentClient) | 🟠 Alto | Revoke tardaba hasta 15 min en los otros 11 |
| 7 | `perm_v` (versión de permisos) se emitía pero **nunca se enforcaba** en .NET | 🟡 Medio | Cambios de permisos tardaban 15 min en efectivizarse |
| 8 | JWT llevaba ~130 claims `perm` embebidos — al borde del límite 4KB | 🟡 Medio | Bloat + acoplamiento a formato del token |
| 9 | 43 endpoints con `[Authorize(Roles="…")]` — anti-patrón role-string-as-authorization | 🟡 Medio | Convivencia old/new causa bugs latentes (bug #1 lo probó) |
| 10 | 12+2 helpers `TryGetTenantAndUser` / `TryResolveTenantId` privados duplicados en controllers | 🟢 Bajo | Cada duplicación es una oportunidad de bug futuro |

---

## 2. Por qué se hizo

**El disparador fue el bug #1** — auditamos por qué el PlatformAdmin no podía operar sobre customers en producción y encontramos la combinación tóxica. Al investigar bien (auditoría de 4 agentes en paralelo en Fase 0), aparecieron los otros 9 huecos. Todos están documentados con archivo/línea en el plan fuente §6.

**No se rediseñó nada.** El modelo `ActorType + Role + Permission + AllowedActorTypes` quedó intacto. Solo se cerraron huecos. Cada fase declara explícitamente qué **no** rompe (plan §4).

---

## 3. Solución (fase por fase)

### FASE 1 — Fix bug crítico Customer
- **Qué:** eliminar `[Authorize(Roles=…)]` de los 22 endpoints de `CustomerController`, dejar solo `[AllowActorTypes]` + `[HasPermission]`.
- **Verificado:** test de reflexión que barre `CustomerController` y falla si aparece cualquier `[Authorize(Roles=…)]`.

### FASE 2 — `SystemTenantAdmin` deja de ser god-role
- **Qué:** nueva flag `Permission.IsDangerous`. `SystemRoleDefaults(SystemTenantAdmin)` ahora excluye permisos peligrosos (7 marcados: `roles.manage`, `users.disable`, `billing.*`, `subscription.manage`, `domains.manage`, `signature.plan_constraints.manage`, `cloudstorage.legal.manage`, `cloudstorage.dmca.counternotice`).
- **Backfill:** hosted service one-shot que recomputa el set del `SystemTenantAdmin` por tenant y publica `RolePermissionsChangedIntegrationEvent`.
- **Verificado:** el chequeo defensivo inline en `SignatureAdminController.UpdateConstraints:45` ya se pudo eliminar — la raíz está cortada.

### FASE 3 — `ActorTypeRoleGuard` en Create/SetRolePermissions
- **Qué:** nuevo método `ValidatePermissionsForActorType(TargetActorType?, permissionIds, catalog)` invocado antes de `SetPermissions`. Rol malformado ya no persiste.
- **API:** `CreateRoleCommand` recibe `TargetActorType` opcional (defense-in-depth = staff).
- **Verificado:** 9 tests nuevos.

### FASE 4 — Resource-based authorization (patrón oficial ASP.NET Core)
- **Qué:** `IAuthorizationService.AuthorizeAsync(user, resource, Operations.Revoke)` + `AuthorizationHandler<Op, Resource>` para 3 aggregates: `ShareLink`, `SignatureRequest`, `Draft` (Correspondence).
- **BuildingBlocks:** `Operations`, `IHasOwner`, `IsOwnerOrHasManageHandler<T>` (genérico — 1 handler, N recursos).
- **Reglas:** PlatformAdmin siempre pasa · Actor con `*.manage` pasa · Owner (`CreatedByUserId == userId`) pasa · Resto falla.
- **Feature flag:** `Authorization:ResourceOwnership:Enabled = false` por default. Se activa por servicio.
- **Verificado:** OWASP A01 / CWE-639 cerrado. README §41.8 lo documenta.

### FASE 5 — `HasQueryFilter` global en los 13 servicios .NET
- **Qué:** safety net EF Core en `OnModelCreating` de cada `*DbContext`. Toda entidad `ITenantOwned` gana el filtro global fail-closed.
- **Casos especiales:** Scribe (system-scoped o tenant-scoped — variante nullable-aware). Correspondence (retrofit `Draft`/`EmailThread`/`TenantBackfillState` a `ITenantOwned`).
- **Overrides legítimos:** jobs cross-tenant con `IgnoreQueryFilters()` + comentario justificando por qué.
- **Verificado:** 3 servicios migraron del middleware header-trust inseguro al `JwtTenantContextMiddleware` compartido. Tests de aislamiento por servicio.

### FASE 6 — Session denylist en los 14 servicios
- **Qué:** `SessionDenylistMiddleware` consolidado a `BuildingBlocks.Web/Session/`, wireado en los 11 servicios que faltaban.
- **Fail-open:** si Redis se cae, log warning y sigue (no bloquea todo el sistema por Redis).
- **Verificado:** 7 servicios ganaron Redis por primera vez.

### FASE 7 — `perm_v` enforcement + proyección local
- **Qué:** nueva abstracción `IUserPermissionsSource` en BuildingBlocks.Web con 2 impls:
  - `JwtEmbeddedPermissionsSource` (default — lee del claim `perm`)
  - `ProjectionPermissionsSource` (lee de tabla local `UserPermissionsProjection` + compara `perm_v` → si el JWT está atrás → 401 `Auth.TokenStale`)
- **Cache in-memory** 30s para no golpear DB en el hot path.
- **Flag:** `Authorization:PermissionsSource = "Jwt" | "Projection"` por servicio.
- **Wolverine consumers** replican la proyección a los 9 servicios que la necesitan.

### FASE 7.5 — Sacar el claim `perm` del JWT humano (breaking change controlado)
- **Qué:** después de flippar los 13 servicios a modo `Projection` y verificar end-to-end, se eliminó el claim `perm` de `JwtTokenGenerator.Generate()`.
- **M2M excepción:** `GenerateScopedServiceToken()` sigue embebiendo `perm` (los permisos de un client M2M son estáticos, no cambian dinámicamente). `ProjectionPermissionsSource` tiene bypass temprano: si `actor_type == Service`, lee del claim `perm` directo.
- **Communication (Node):** `hasPermission` pasó de sync a async, consulta la proyección Prisma con el mismo staleness-check.
- **Verificado:** login real + 4 endpoints reales en distintos servicios + cambio de rol → 401 stale → refresh → 200. Bug encontrado durante la verificación: Growth `CodesController` leía el claim raw fuera del pipeline `[HasPermission]` → arreglado.

### FASE 8 — Migrar 22 `[Authorize(Roles=…)]` a `[HasPermission]`
- **Qué:** 22 endpoints (no 43 como decía el plan — CustomerController ya se había migrado en Fase 1; verificado por grep antes de tocar). 7 permisos nuevos en el catálogo. Subscription ganó `[HasPermission]` por primera vez.
- **NO reusé `subscription.manage`** (habría regresado el bug de TenantAdmin bypass eliminado en Fase 2).

### FASE 9 — Consolidar helpers duplicados
- **Qué:** `ControllerIdentityExtensions` en `BuildingBlocks.Web`. Los 12 `TryGetTenantAndUser` + 2 `TryResolveTenantId` privados eliminados.
- **Nota:** Postmaster ganó overload nullable adicional (firma real vs plan). Growth **sí** se consolidó (a diferencia de lo que decía el plan — la extensión vive en `ControllerBase`, no colisiona con la clase local de Growth).

### FASE 10 — Hardening final (audit + observabilidad + docs)
- **AuthorizationMetrics:** counter `authz.decision` con dimensions `{result: allow|deny, layer: 1|2|3a|3b}`.
- **Audit trail:** `AuthAuditLog` ya wireado en handlers relevantes (0 cambios).
- **README §41:** diagrama de 5 capas + `IsDangerous` + `perm_v` + 3b/denylist.
- **Limpieza:** chequeos defensivos `IsPlatformAdmin()` inline revisados.

---

## 4. Por qué esta es la mejor forma

### 4.1 Cada capa cubre un ángulo de ataque distinto

```
Request → [Layer 1] AuthN JWT válido
        → [Layer 2] AllowActorTypes (tipo de identidad)
        → [Layer 3] HasPermission (capacidad granular, verificada contra proyección)
        → [Layer 4a] Tenant boundary (EF query filter global fail-closed)
        → [Layer 4b] Resource ownership (IAuthorizationService per aggregate)
        → [Layer 5] Session denylist (revoke inmediato en 14 servicios)
```

Si cualquier capa falla en un servicio nuevo, las otras 4 lo detienen. **Defense-in-depth real, no teórico.**

### 4.2 Alternativas descartadas — con razón concreta

| Alternativa | Por qué no |
|---|---|
| **Rediseñar todo el catálogo** | El diseño coincide con WorkOS/GitHub Enterprise. Rediseñar por rediseñar es riesgo puro. |
| **Confiar solo en `[Authorize(Roles=…)]`** | Anti-patrón OWASP + Spatie. Bug #1 lo probó: rol-string-as-authorization no compone con actor type. |
| **`SystemTenantAdmin` sigue siendo dinámico total** | Cada permiso nuevo entraba automático — escalada silenciosa. `IsDangerous` es un opt-out explícito. |
| **`HasQueryFilter` solo donde ya estaba** | Aceptar que 12 servicios dependan 100% de un handler humano. Un olvido = leak cross-tenant. Safety net = obligatorio. |
| **Meter resource ownership como `if` en el handler** | Repetible, sin fitness function, sin `manage` override, no compatible con auditoría. El patrón oficial de ASP.NET Core es `IAuthorizationService` + handler tipado — es lo que usamos. |
| **Enforcement de `perm_v` como parte del JWT verify** | Rompe el contrato de tokens auto-contenidos. La proyección local + cache 30s es más rápido y más flexible. |
| **Sacar `perm` del JWT sin proyección primero** | Bricking real — servicios sin proyección se quedan sin permisos. Fase 7 → 7.5 en orden es lo único que funciona sin outage. |

### 4.3 Reversibilidad garantizada por fase

- **Feature flags** en Fase 4 y 7 (activar por servicio, apagar sin redeploy).
- **Migraciones additive-only** durante todo el plan (cero `DROP COLUMN`, cero `ALTER` que reduce).
- **Convivencia deliberada** entre Fase 1 (limpieza puntual) y Fase 8 (limpieza masiva) — aceptable, no urgente.
- **Cada fase compila+testea+deploya por separado.** Cero "esta fase depende del deploy de la siguiente para no romper".

---

## 5. Fuentes / investigación

- **OWASP Top 10 A01:2021 — Broken Access Control** (CWE-284, CWE-639, CWE-863). Bug #4 (resource ownership) es exactamente esto.
- **ASP.NET Core — Resource-based authorization:** https://learn.microsoft.com/aspnet/core/security/authorization/resourcebased — patrón oficial que copiamos literal en Fase 4.
- **ASP.NET Core — Policy-based authorization:** https://learn.microsoft.com/aspnet/core/security/authorization/policies — base de `PermissionPolicyProvider`.
- **EF Core — Global query filters:** https://learn.microsoft.com/ef/core/querying/filters — Fase 5.
- **WorkOS AuthKit — RBAC model:** https://workos.com/docs/user-management/rbac — validación externa de `ActorType + Role + Permission + AllowedActorTypes`.
- **GitHub Enterprise — Roles model:** https://docs.github.com/en/enterprise-cloud@latest/admin/managing-accounts-and-repositories/managing-users-in-your-enterprise/roles-in-an-enterprise.
- **Spatie Laravel Permission — Best practices:** https://spatie.be/docs/laravel-permission/v6/basic-usage/best-practices — el "avoid role-string-as-authorization" que citamos en Fase 8.
- **Precedente interno:** ActorType plan (Fases 1-7.5 previas al RBAC hardening) implementó las 4 capas base. Este plan las **completa**, no las reinventa.

---

## 6. Mini-doc para otros devs

### 6.1 ¿Cómo protejo un nuevo endpoint?

```csharp
[HttpPost("customers/{id}/preparer")]
[AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)] // Capa 2
[HasPermission(PermissionCodes.CustomersPreparerAssign)]           // Capa 3
public async Task<IActionResult> AssignPreparer(Guid id, ...)
{
    if (!this.TryGetTenantAndUser(out var tenantId, out var userId)) // Capa 4a (helper)
        return Unauthorized();

    // Capa 4b: si el recurso tiene owner, autorización basada en recurso
    var draft = await _repo.GetByIdAsync(tenantId, id);
    var authz = await _authorizationService.AuthorizeAsync(User, draft, Operations.Update);
    if (!authz.Succeeded) return Forbid();

    // ... lógica del handler
}
```

**NUNCA** uses `[Authorize(Roles="TenantAdmin")]`. **NUNCA** leas `User.HasClaim("perm", "x")` directo — pasa por `[HasPermission]` o por `IUserPermissionsSource`.

### 6.2 ¿Cómo agrego un permiso al catálogo?

1. En `PermissionCatalog.cs`:
   ```csharp
   public static readonly PermissionDefinition CustomersPreparerAssign = new(
       Code: "customers.preparer.assign",
       AllowedActorTypes: [ActorType.TenantAdmin, ActorType.PlatformAdmin],
       IsAssignableByTenant: true,
       PlatformOnly: false,
       MinPlanTier: PlanTier.Basic,
       IsDangerous: false // ⚠️ si es "cross-cutting" o "irreversible", marcar true
   );
   ```
2. Migración EF con `UpdateData` que inserta la fila en `Permissions`.
3. Si es `IsDangerous: true` + querés que llegue a algún rol del sistema, agregarlo **explícito** en `SystemRoleDefaults(...)`.
4. Postman: agregar test negativo (actor sin el permiso → 403).

### 6.3 ¿Cómo protejo un aggregate por owner?

1. Implementar `IHasOwner` en el aggregate (`Guid CreatedByUserId { get; }`).
2. Registrar el handler tipado en `Program.cs`:
   ```csharp
   services.AddScoped<IAuthorizationHandler, IsOwnerOrHasManageHandler<ShareLink>>();
   ```
3. Crear un permiso `<scope>.manage` (`IsDangerous: false`) para el override de TenantAdmin operativo.
4. En el handler:
   ```csharp
   var authz = await _authorizationService.AuthorizeAsync(userClaims, resource, Operations.Revoke);
   if (!authz.Succeeded) return Result.Failure(new Error("ShareLink.NotOwner", "..."));
   ```

### 6.4 ¿Cómo agrego un nuevo servicio al mecanismo completo?

Checklist mínimo (copiar de CloudStorage como referencia limpia):

1. `Program.cs`:
   ```csharp
   services.AddActorTypeAuthorization();
   services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
   services.AddMemoryCache();
   services.AddScoped<IUserPermissionsSource>(sp =>
       config["Authorization:PermissionsSource"] == "Projection"
           ? new ProjectionPermissionsSource(...)
           : new JwtEmbeddedPermissionsSource());
   app.UseMiddleware<JwtTenantContextMiddleware>();     // Capa 4a — DEBE ir antes de UseAuthorization
   app.UseAuthentication();
   app.UseMiddleware<SessionDenylistMiddleware>();       // Capa 5 — entre AuthN y AuthZ
   app.UseAuthorization();
   ```
2. `DbContext.OnModelCreating`: registrar `HasQueryFilter` global sobre `ITenantOwned` (copiar de CloudStorage).
3. NetArchTest en el proyecto `.Tests`: la regla `Controller_actions_should_declare_AllowActorTypes` falla si te olvidás.
4. Wolverine: consumers de `UserRolesChangedIntegrationEvent` y `RolePermissionsChangedIntegrationEvent` para mantener la proyección local sincronizada.

### 6.5 ¿Cómo revoco una sesión?

`POST /auth/sessions/{sid}/revoke` en Auth. El middleware `SessionDenylistMiddleware` en los 14 servicios rechaza cualquier request con ese `sid` en el JWT (401 `Auth.SessionRevoked`) hasta que expira el TTL de Redis.

### 6.6 ¿Cómo hago "cambio de rol se refleja YA"?

Con `PermissionsSource = "Projection"` (default post-Fase 7.5):
1. Handler cambia el rol → publica `RolePermissionsChangedIntegrationEvent` con `PermissionsVersion++`.
2. Consumers Wolverine en los 13 servicios actualizan `UserPermissionsProjection` local.
3. Próximo request con JWT viejo: `ProjectionPermissionsSource` compara `perm_v` del JWT vs proyección → si atrás → 401 `Auth.TokenStale`.
4. Frontend hace refresh → nuevo JWT con `perm_v` actualizado → funciona.

Sin `refresh flow` en el frontend, el usuario ve 401 al cambio de permisos. **Documentado.**

---

## 7. Métricas de cierre

- **10 fases + 7.5** implementadas.
- **1963 / 1963 tests** verdes en el monorepo completo.
- **0 regresiones** funcionales.
- **README §41** completo — diagrama de 5 capas + tablas de flags + `IsDangerous` + `perm_v` + guía de troubleshooting.
- **Postman:** tests negativos por permiso y por actor type en todas las colecciones.
- **Auditoría independiente:** 4 agentes en paralelo confirmaron cierre de las 9 fases (encontraron 1 bug preexistente — 3 eventos CloudStorage sin ruta Wolverine → arreglado).

---

## 8. TL;DR para el senior

- **Problema:** 10 huecos concretos en un mecanismo de authz que en diseño era correcto pero en implementación tenía escapes (bug funcional en Customer, god-role dinámico, cero resource ownership, tenant boundary confiando en el handler, revoke lento, `perm_v` no enforced, JWT bloat, role-string-as-authorization).
- **Solución:** cerrar los 10 huecos en 10 fases + una fase 7.5 de corte controlado del claim `perm` embebido. Cero rediseño. Feature flags donde el cambio es visible al usuario. Migraciones additive-only.
- **Por qué así:** el patrón oficial de ASP.NET Core (`IAuthorizationService` + `AuthorizationHandler<Op, Resource>`) + el patrón oficial de EF Core (`HasQueryFilter` global) + validación cruzada contra WorkOS/GitHub Enterprise/Spatie. Nada inventado.
- **Fuentes:** OWASP A01, ASP.NET Core docs, EF Core docs, WorkOS AuthKit, GitHub Enterprise RBAC, Spatie best practices.
- **Riesgo residual:** JWKS rotation manual (issue operacional, no de seguridad — fuera de alcance). `perm_v` enforcement asume que el frontend maneja el refresh flow (documentado).
- **Reversibilidad:** cada fase tiene plan de rollback documentado. Los flags permiten apagar sin redeploy.
