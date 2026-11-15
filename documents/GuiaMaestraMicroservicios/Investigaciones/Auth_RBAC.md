# Auth, RBAC, actor types, gate de módulo y aislamiento por tenant

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\` salvo que se indique otra raíz.
> Etiquetas: **[Código verificado]** = leído en el código actual · **[Documentación histórica]** = lo dice un documento, puede estar desfasado · **[Recomendación]** · **[Decisión pendiente]**.
> Servicio de referencia para la guía: **FinancialInsights** (el más nuevo y el que cablea todas las piezas: proyección de permisos con pull-recovery, gate de módulo en enforce, rate limit con cuotas por plan, reconciliaciones).

---

## 1. Estado actual

### 1.1 El pipeline de autorización de un request humano [Código verificado]

Orden real en `src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/Program.cs:148-154`:

```
UseAuthentication()                       → valida JWT (firma RS256, iss, aud, exp)
UseMiddleware<JwtTenantContextMiddleware>  → sella TenantContext + bus.TenantId desde el claim tenant_id
UseMiddleware<SessionDenylistMiddleware>   → 401 si el sid está revocado (ver Session_Denylist.md)
UseAuthorization()                         → policies: [Authorize], [HasPermission] (+ gate de módulo)
  filtros MVC globales:
    ActorTypeAuthorizationFilter           → [AllowActorTypes] obligatorio (fail-closed)
    SurfaceAuthorizationFilter             → token con claim surface solo donde haya [AllowSurface]
  [RateLimit("...")] (resource filter)     → ver RateLimit_Cuotas_Gates.md
Handler (Wolverine) → TenantContext restaurado por LocalCommandTenantMiddleware → HasQueryFilter fail-closed
```

### 1.2 JWT [Código verificado]

| Aspecto | Valor real | Evidencia |
|---|---|---|
| Firma | RS256 si hay `Jwt:PublicKeyPem` o `Jwt:PublicKeyPath`; si no, **HS256 con `Jwt:Secret`** (≥32 bytes) | `src/BuildingBlocks/BuildingBlocks.Web/Security/JwtAuthenticationRegistration.cs:71-112` |
| `kid` | Derivado del SHA-256 de la clave pública (16 bytes, base64url); debe coincidir con Auth | `JwtAuthenticationRegistration.cs:93-99,115-120`; `src/Services/Auth/Infrastructure/Security/SigningKeyProvider.cs:47,79` |
| Validación | `ValidateIssuer/Audience/Lifetime/IssuerSigningKey = true`, `ClockSkew = 30 s`, `Jwt:ValidAudiences` opcional | `JwtAuthenticationRegistration.cs:28-46` |
| Issuer / Audience | `TaxVision.Auth` / `TaxVision.Services` (humanos y M2M comparten audiencia) | `src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/appsettings.json:9-12`; compose `Jwt__Issuer`, `Jwt__Audience` |
| Audiencias especiales | `TaxVision.Account` (superficie Landing), `TaxVision.MfaEnrollment` (token solo-MFA), `taxvision-growth`/`taxvision-documents`/`taxvision-payments` (clientes M2M con scopes) | `src/Services/Auth/Api/appsettings.json` (sección Jwt), `src/BuildingBlocks/ActorTypeAuthorization/MfaEnrollmentToken.cs:10`, compose `ServiceAuth__Clients__9..13__Audience` |
| Vida | access token humano 15 min (`JwtOptions.AccessMinutes`); token solo-MFA 10 min; token M2M 10 min | `src/Services/Auth/Infrastructure/Security/JwtTokenGenerator.cs:24,27`; `src/Services/Auth/Application/ServiceTokens/ServiceAuthOptions.cs:14` |
| Clave pública en servicios .NET | archivo montado `/run/secrets/jwt-public.pem` (`Jwt__PublicKeyPath`), local `../../../../dev-keys/jwt-public.pem` | compose `financial-insights-api`; `appsettings.Development.json` de FI |
| JWKS | `GET auth/.well-known/jwks.json` (lo usa Communication/Node; los .NET leen el PEM) | `src/Services/Auth/Api/Controllers/AuthController.cs:303-309` |
| Rotación | Una sola clave/`kid` (no hay lista de claves previas) | `SigningKeyProvider.cs:19-60` |

Claims del token humano (`JwtTokenGenerator.Generate`, `JwtTokenGenerator.cs:52-91`): `sub`, `email`, `jti`, `sid`, `tenant_id`, `actor_type`, `zoneinfo`, `perm_v`, opcionales `customer_id`, `is_owner`, `role` (uno por rol), `amr`, `surface`, `mfa_enroll`, `reauth_at`. **No lleva `perm`** desde la Fase 7.5.10.

Claims del token M2M (`GenerateScopedServiceToken`, `JwtTokenGenerator.cs:119-152`): `sub` = GUID derivado de `MD5("service-principal:{clientId}")` (`:179-180`), `jti`, `tenant_id` (el que pidió el llamador), `actor_type = "Service"`, `client_id`, `perm` (uno por permiso configurado), `scope` (uno por scope). **Sin `sid` ni `perm_v`.**

### 1.3 Actor types y helpers de claims [Código verificado]

- `enum ActorType { TenantEmployee, TenantAdmin, CustomerPortal, PlatformAdmin, Service }` — `src/BuildingBlocks/ActorTypeAuthorization/ActorType.cs:12-19`.
- Nombres de claim compartidos — `src/BuildingBlocks/ActorTypeAuthorization/ClaimNames.cs:7-20`.
- `GetActorType()` devuelve `null` si el claim falta o no parsea (fail-closed) — `ClaimsPrincipalExtensions.cs:98-102`.
- `IsPlatformAdmin()` se decide **solo** por `actor_type`, nunca por el claim de rol (un custom role llamado "PlatformAdmin" ya habilitó el bypass) — `ClaimsPrincipalExtensions.cs:55-62`.
- `HasPermission()` = claim `perm` **o** PlatformAdmin — `ClaimsPrincipalExtensions.cs:64-69`. TenantAdmin **no** tiene bypass.
- `GetPermissionsVersion()` lee `perm_v` (0 si falta) — `ClaimsPrincipalExtensions.cs:71-76`.

### 1.4 Capa actor type (`[AllowActorTypes]`) [Código verificado]

- `ActorTypeAuthorizationFilter` corre para toda acción MVC. Sin `[AllowActorTypes]` en método o controller → **403 `Authz.ActorTypeNotDeclared`** (fail-closed). `[AllowAnonymous]` y `[AuthorizedByCapabilityToken]` se saltan. `PlatformAdmin` siempre pasa — `src/BuildingBlocks/BuildingBlocks.Web/ActorTypeAuthorization/ActorTypeAuthorizationFilter.cs:27-50,82-84`.
- Se registra con `.AddActorTypeAuthorization()` sobre `AddControllers()`, que además registra `AuthorizationMetrics`, el `ProblemDetailsAuthorizationResultHandler` (403 con cuerpo RFC 9457) y `SurfaceAuthorizationFilter` — `ActorTypeAuthorizationFilter.cs:95-114`.
- `[HasPermissionForActor(actor, permiso)]` exige un permiso solo a un actor concreto en endpoints compartidos (apilar dos `[HasPermission]` exigiría ambos) — `HasPermissionForActorAttribute.cs:21-47`.

### 1.5 Capa permiso (`[HasPermission]`) [Código verificado]

- `[HasPermission("x")]` = `AuthorizeAttribute` con policy `"perm:x"` — `HasPermissionAttribute.cs:14-20`.
- `PermissionPolicyProvider` (registrado como `IAuthorizationPolicyProvider` singleton en cada `Program.cs`) construye la policy contra `IUserPermissionsSource` — `PermissionPolicyProvider.cs:26-105`.
- `IUserPermissionsSource` tiene dos implementaciones:
  - `JwtEmbeddedPermissionsSource`: lee el claim `perm` → **inservible para humanos** (el claim ya no se emite) — `JwtEmbeddedPermissionsSource.cs:10-14`.
  - `ProjectionPermissionsSource`: lee la proyección local (`UserPermissionsProjection`) — `ProjectionPermissionsSource.cs:51-121`:
    - PlatformAdmin → `true` (`:53-54`); actor `Service` → lee el claim `perm` del token M2M (`:56-57`).
    - Sin `sub`/`tenant_id` → `false` (`:59-60`).
    - Cache en memoria 30 s por `(tenant,user)` (`:63-73`).
    - Miss local → **pull-recovery opcional** (`IPermissionsSnapshotClient` + `IUserPermissionsProjectionWriter`): pide a Auth `internal/tenants/{t}/users/{u}/permissions-snapshot` y persiste (`:79-95`).
    - Sin snapshot → **fail-closed** con warning (`:99-111`).
    - `perm_v` del JWT < versión de la proyección → `UnauthorizedAccessException("Auth.TokenStale")` → **401** (`:114-118`; mapeo en `src/BuildingBlocks/BuildingBlocks.Web/Middleware/ExceptionHandlingMiddleware.cs:43-49`). El frontend refresca el token.
- `AddUserPermissionsSource(config, assembly)` elige la fuente por `Authorization:PermissionsSource` y **revienta al arrancar** si el ensamblado tiene algún `[HasPermission]` y el modo no es `Projection` — `UserPermissionsSourceRegistration.cs:38-67`.

### 1.6 Proyecciones de permisos y versionado [Código verificado]

- Auth publica `UserRolesChangedIntegrationEvent` con `PermissionsVersion`, `RoleIds`, `PermissionCodes` **efectivos** (unión de roles activos menos denies por usuario) y `ActorType` requerido — `src/BuildingBlocks/Messaging/AuthIntegrationEvents/UserRolesChangedIntegrationEvent.cs:10-39`.
- `RolePermissionsChangedIntegrationEvent` trae el set completo del rol y su versión — `RolePermissionsChangedIntegrationEvent.cs:18-28`. **Ya no se usa para recomponer permisos de usuarios**: Auth hace fan-out y publica un `UserRolesChanged` por cada titular, subiendo su `PermissionsVersion` — `src/Services/Auth/Application/Common/RolePermissionsFanOut.cs:9-122`. Motivo documentado ahí: recomponer la unión en el consumidor resucitaba permisos denegados.
- Consumer de referencia (FI): `UserRolesChanged` → upsert con `ApplyIfNewer`; `RolePermissionsChanged` → solo guarda la fila del rol ("nunca suma permisos") — `src/Services/FinancialInsights/TaxVision.FinancialInsights.Application/Projections/Consumers/ProjectionConsumer.cs:21-74`.
- Entidad: `ApplyIfNewer` descarta `version < PermissionsVersion` (versiones iguales se re-aplican: idempotente) — `src/Services/FinancialInsights/TaxVision.FinancialInsights.Domain/Projections/AccessProjections.cs:54-63`.
- Ciclo de vida: `UserDeactivated`/`UserOffboarded`/`UserReactivated` → `SetActive` ordenado por `OccurredOn`, independiente de la versión de permisos — `UserLifecyclePermissionsProjectionConsumer.cs:14-100`, `AccessProjections.cs:65-72`.
- El lector de FI exige `IsActive && HasPermissionsSnapshot` — `src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/Persistence/FinancialProjectionReaders.cs:18-25`.
- Reparación en Auth (push): `PermissionsBackfillService` (al arrancar, cursor `PermissionsBackfilledAt`), `SystemRolePermissionsSyncService` (recalcula roles de sistema contra el catálogo y publica), `PermissionsReconciliationService` (cada 6 h re-publica `UserRolesChanged` de todos los activos, lotes de 50, **no** sube versión) — `src/Services/Auth/Api/Bootstrap/PermissionsBackfillService.cs:38`, `SystemRolePermissionsSyncService.cs:12-40`, `src/Services/Auth/Api/Jobs/PermissionsReconciliationService.cs:12-58`.

### 1.7 Catálogo de permisos, `PlatformOnly`, "god roles" y techo [Código verificado]

- Las constantes viven en `src/BuildingBlocks/Authorization/*Permissions.cs` (p. ej. `FinancialInsightsPermissions.cs`: `financial.import`, `financial.view`, `financial.portal.import`).
- El catálogo con GUID fijo vive en Auth: `src/Services/Auth/Domain/Roles/PermissionCatalog.cs` (FI: líneas 538-559; bundle Employee 2471-2474; bundle Customer Portal 2500).
- Banderas de `Permission` (`src/Services/Auth/Domain/Roles/Permission.cs`): `IsAssignableByTenant` (:34), `PlatformOnly` (:42, el rol de sistema Tenant Admin nunca lo incluye), `IsDangerous` (:52, fuera del bundle automático: roles.manage, billing.*, etc.), `IsReserved` (:61, declarado sin endpoint), `AllowedActorTypes` (:72, inferido en `:110-113`).
- "God role": desde RBAC Fase 2 `SystemTenantAdmin` ya **no** es god-role (excluye `IsDangerous`/`PlatformOnly`). El único bypass total es `PlatformAdmin` por `actor_type` [Código verificado: `ProjectionPermissionsSource.cs:53`, `ActorTypeAuthorizationFilter.cs:83`; Documentación histórica: `documents/RBAC_Endurecimiento_Defensa_Senior.md` §FASE 2].
- Techo de delegación `PermissionCeiling`: nunca concedible si `!IsAssignableByTenant || PlatformOnly || IsDangerous || IsReserved`; mitad comercial = módulo habilitado — `src/Services/Auth/Application/Common/PermissionCeiling.cs:72-91`.

### 1.8 Gate de módulo (entitlements en runtime) [Código verificado]

- Vive **dentro** de `PermissionPolicyProvider`, después de conceder el permiso: si `PermissionModuleMap.ModuleFor(permiso)` no es null y el servicio registró `ITenantModuleEntitlementsSource`, consulta el módulo; si no está y `ModuleGateSettings.ShouldEnforce` → lanza `ModuleUnavailableException` → **403 `Authz.ModuleUnavailable`**; si no, log-only — `PermissionPolicyProvider.cs:55-97`.
- Mapa prefijo → módulo (el primero que calza gana; `communication.meeting.` antes de `communication.`) y exentos (`notification.read`, `support.open`, `support.agent`) — `src/BuildingBlocks/Authorization/PermissionModuleMap.cs:48-69,94-121`. `KnownModules` deriva del mapa (:136-137).
- Flags: `Authorization:ModuleGate:Enforce` y `Authorization:ModuleGate:EnforcedModules` (escalón por módulo; lista vacía con Enforce=true = todos) — `ModuleGateSettings.cs:31-47`. Con Enforce=true, un módulo desconocido en la lista **revienta el arranque** — `:72-97`.
- `AddModuleGate(config)` registra la fuente y valida en el mismo acto — `ModuleGateRegistration.cs:25-44`.
- Política de la fuente: PlatformAdmin y Service siempre habilitados; sin tenant → no gatea; **sin fila (`null`) → no gatea (fail-open)**; **lista vacía `[]` → deniega** (suscripción vencida) — `TenantModuleEntitlementsSource.cs:20-48`.
- Lector con caché Redis 1 min (`null` no se cachea, `[]` sí) — `src/BuildingBlocks/BuildingBlocks.Infrastructure/RateLimiting/CachedTenantEntitlementModulesReader.cs:30-56`; registro obligatorio vía `AddCachedTenantEntitlementModulesReader<T>()` — `TenantEntitlementModulesRegistration.cs:24-41`.
- Estado de despliegue: 14 servicios con `${X_MODULE_GATE_ENFORCE:-false}` en compose; **FinancialInsights con `"true"` literal** (`deploy/docker/docker-compose.yml` líneas 1659-1660 vs 836, 1106, 1183…).

### 1.9 TenantContext, query filters y consumers sin HTTP [Código verificado]

- `ITenantContext` (núcleo) y `TenantContext` mutable scoped (`TenantId` lanza si no está) — `src/BuildingBlocks/Tenancy/ITenantContext.cs:9-20`, `src/BuildingBlocks/BuildingBlocks.Web/Tenancy/ITenantContext.cs:10-19`.
- HTTP: `JwtTenantContextMiddleware` toma `tenant_id` del JWT, 401 si el claim está malformado, y estampa `bus.TenantId` — `JwtTenantContextMiddleware.cs:40-59`.
- Consumers: `IntegrationEventTenantMiddleware` toma el tenant del payload del evento; `Guid.Empty` → excepción (fail-closed) — `IntegrationEventTenantMiddleware.cs:38-45`.
- Comandos locales: `LocalCommandTenantMiddleware` lee una propiedad `Guid TenantId` del mensaje (reflexión) o el `Envelope.TenantId` — `LocalCommandTenantMiddleware.cs:65-90`. Su propio comentario advierte que el `TenantContext` poblado **no siempre llega al scope del handler** en comandos encolados; de ahí el patrón `IgnoreQueryFilters()` + `TenantId` explícito en repositorios (`:42-59`).
- DbContext de referencia: filtro global fail-closed `TenantId == EffectiveTenantId` (Guid.Empty si no hay tenant) sobre todo `ITenantOwned` — `src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/Persistence/FinancialInsightsDbContext.cs:31,140-154`.
- Consumers de FI validan además el tenant del contrato (`evt.TenantId != evt.NewTenantId → return`) — `ProjectionConsumer.cs:122-123,145-146,166-167`.
- `PlatformTenant.Id = 8f58a521-4c25-4d91-9f4e-7ad5df14c001` — `src/BuildingBlocks/Tenancy/PlatformTenant.cs:11`.

---

## 2. Evidencia adicional (tests y documentos)

- Documento senior: `documents/RBAC_Endurecimiento_Defensa_Senior.md` (fases 1-10 y 7.5, mini-doc §6) [Documentación histórica].
- Guía de cableado de módulo: `documents/architecture/Guia_Cablear_Modulo_Plan_Microservicio.md` §3 [Documentación histórica, mayormente vigente].
- Vault `C:\Users\devcacg\Desktop\Implementaciones\RABC\Guia_Arquitectura_de_Accesos.md` §3 y §9 [Documentación histórica].
- Memoria: `project_a6_module_gate_estado_medido.md` (semántica `null` ≠ `[]`), `feedback_permissionmodulemap_cross_service_no_duplicate.md`, `feedback_no_conceder_permisos_sistema_por_migracion.md`, `feedback_comprobacion_de_recurso_no_es_comprobacion_de_permiso.md`, `feedback_permisos_viajan_version_no_lista_y_realtime_socket.md`.

---

## 3. Problemas detectados

1. **Contradicción doc ↔ código (consumer de roles).** El documento senior §6.5 paso 3 y el comentario de cabecera de `src/Services/CloudStorage/TaxVision.CloudStorage.Application/Permissions/Consumers/PermissionsProjectionConsumers.cs:10-15` dicen "union-recompute en RolePermissionsChanged". El código de CloudStorage (`:67-94`) y de FI ya **no** recomponen (y no deben: `RolePermissionsFanOut.cs:15-19`). Copiar la doc reintroduciría el bug de denies.
2. **Doc desfasada en `Program.cs`.** El senior §6.5 paso 5 muestra un `if/else` manual; el código actual usa `AddUserPermissionsSource` que falla al arrancar (`UserPermissionsSourceRegistration.cs:38-67`). El senior §6.6 pone `JwtTenantContextMiddleware` **antes** de `UseAuthentication`; el orden correcto es después (FI `Program.cs:148-151`).
3. **Contador del fitness desfasado.** `ModuleGateWiringFitnessTests.The_fourteen_gated_services_are_still_wired` afirma `15` (`deploy/tests/TaxVision.BuildingBlocks.Tests/ActorTypeAuthorization/ModuleGateWiringFitnessTests.cs:109-121`); la guía de módulo habla de `The_thirteen_…` = 13.
4. **FI enciende el gate sin palanca de rollback.** `Authorization__ModuleGate__Enforce: "true"` literal (compose 1659) contradice la regla "booleano por secret `${VAR:-false}`" de `Guia_Cablear_Modulo_Plan_Microservicio.md` §3.5 y el patrón de los otros 14. **[Decisión pendiente]**: la memoria "Producción sin datos reales: encender por defecto" sugiere que es intencional; falta dejarlo escrito.
5. **`ProjectionPermissionsSource` no conoce `IsActive`.** Depende de que cada lector filtre usuarios inactivos (FI sí: `FinancialProjectionReaders.cs:22`). No verifiqué los ~24 lectores restantes. **[Decisión pendiente / verificar]**.
6. **Pull-recovery sin validación cruzada de tenant.** `InternalPermissionsSnapshotController` acepta cualquier token `actor_type=Service` y usa el `tenantId` de la ruta (`src/Services/Auth/Api/Controllers/InternalPermissionsSnapshotController.cs:18-35`): un cliente M2M de cualquier tenant lee los permisos de cualquier usuario de cualquier tenant. Ver M2M_ServiceAuth.md.
7. **`PermissionModuleMap` vive duplicado en Node** (`src/Services/Communication/src/domain/shared/permission-module-map.ts`, según memoria); lo cubre `ModuleGateWiringFitnessTests.The_node_exemption_list_mirrors_the_dotnet_one` solo para exentos.
8. **Fail-open por diseño del gate cuando falta la fila**: un servicio nuevo sin reconciliación de entitlements no gatea a tenants existentes hasta que su plan cambie (ver Proyecciones.md §3).
9. **Modo dual RS256/HS256.** Un servicio desplegado sin `Jwt__PublicKeyPath` pero con `Jwt:Secret` caería a HS256 en silencio (`JwtAuthenticationRegistration.cs:102-111`). En compose todos llevan PEM; no hay test que lo exija. **[Recomendación]** fitness de compose.

---

## 4. Buenas prácticas (observadas en el código)

- Fail-closed en cada capa: actor type no declarado, actor type desconocido, proyección ausente, tenant ausente en DbContext.
- Fallos de configuración que revientan al arrancar en vez de 403 silencioso (`AddUserPermissionsSource`, `AddModuleGate`).
- `tenant_id` siempre del token; nunca del body/query (`ClaimsPrincipalExtensions.cs:14-17`).
- Respuestas 401/403 con código estable (`Auth.TokenStale`, `Authz.ActorTypeNotDeclared`, `Authz.PermissionDenied`, `Authz.ModuleUnavailable`, `Auth.SurfaceNotAllowed`) — `AuthorizationDenial.cs:59-93`.
- Métrica `authz.decision{result,layer}` y `RecordModuleDecision` (`AuthorizationMetrics`).
- Permisos nuevos: solo `InsertData` en `Permissions`; el grant a roles de sistema lo hace `SystemRolePermissionsSyncService` y publica (memoria `feedback_no_conceder_permisos_sistema_por_migracion`).

---

## 5. Recomendación (para la guía)

- Todo servicio con endpoints humanos: `[Authorize]` + `[AllowActorTypes(...)]` + `[HasPermission(...)]` + `[RateLimit(...)]` (o `[RateLimitExempt(razón)]`) en **cada** acción.
- Modo `Projection` obligatorio + consumers de `UserRolesChanged` (upsert por versión) y de ciclo de vida (`UserDeactivated/Offboarded/Reactivated`) + pull-recovery (`IPermissionsSnapshotClient` + `IUserPermissionsProjectionWriter`).
- Si el servicio vende una feature: prefijo en `PermissionModuleMap`, `AddModuleGate`, lector con caché, invalidador de las dos cachés, flag en compose con `${VAR:-false}` + `deploy.yml`.
- No recomponer permisos a partir de `RolePermissionsChanged`.
- Repositorios: `IgnoreQueryFilters()` + `TenantId` explícito; el filtro global es la red de seguridad.

---

## 6. Riesgos

| Riesgo | Efecto | Severidad estimada |
|---|---|---|
| Servicio con `[HasPermission]` sin proyección poblada ni pull-recovery | 403 para todos los humanos | Alta (funcional) |
| Pull-recovery sin `ServiceAuthClient` configurado | Igual que arriba, en silencio (`snapshotClient` devuelve null) | Alta |
| Gate en enforce con proyección de entitlements derivada | 403 a tenants que sí pagan, o acceso a vencidos si se confunde `null` con `[]` | Alta |
| Snapshot de permisos cross-tenant por cualquier cliente M2M | Lectura de permisos de otro tenant | Media (requiere secreto M2M) |
| HS256 por mala configuración | Aceptaría tokens firmados con el secreto compartido | Media |
| Clave de firma única sin rotación | Rotar exige corte coordinado | Media (operacional) |

---

## 7. Pruebas

**Existentes [Código verificado]**

- `deploy/tests/TaxVision.BuildingBlocks.Tests/ActorTypeAuthorization/PermissionsSourceTests.cs` — perm_v, TokenStale, fail-closed sin snapshot, bypass PlatformAdmin y Service, cache 30 s.
- `…/ActorTypeAuthorization/UserPermissionsSourceRegistrationTests.cs` — el arranque falla si hay `[HasPermission]` sin `Projection`.
- `…/ActorTypeAuthorization/TenantModuleEntitlementsSourceTests.cs` — `An_empty_projection_denies`, `A_missing_projection_is_NOT_the_same_as_an_empty_one`.
- `…/ActorTypeAuthorization/ModuleGateSettingsTests.cs`, `ModuleGateEnforcePolicyTests.cs`, `ModuleGateWiringFitnessTests.cs`, `HasPermissionForActorAttributeTests.cs`, `AuthorizationMatrixTests.cs`, `AuthorizationDenialTests.cs`.
- `…/Security/ActorTypeAuthorizationFilterTests.cs`, `SurfaceAuthorizationFilterTests.cs`, `RequireRecentAuthenticationAttributeTests.cs`.
- `…/Authorization/PermissionModuleMapTests.cs`, `ClaimsPrincipalExtensionsTests.cs`.
- `…/Tenancy/JwtTenantContextMiddlewareTests.cs`, `IntegrationEventTenantMiddlewareTests.cs`, `LocalCommandTenantMiddlewareTests.cs`, `PlatformTenantIdContractTests.cs`.
- `…/Deploy/ModuleGateDeployWiringTests.cs` (flag en compose y deploy.yml, lista literal).
- `deploy/tests/TaxVision.Auth.Tests/Application/PermissionCeilingTests.cs`, `RolePermissionsFanOutTests.cs`, `Infrastructure/JwtTokenGeneratorTests.cs`, `Infrastructure/PermissionsReconciliationServiceTests.cs`.
- FI: `deploy/tests/TaxVision.FinancialInsights.Tests/Application/ProjectionConsumerTests.cs` (`User_deny_event_removes_permission_and_role_event_cannot_restore_it`, `Tenant_module_projection_keeps_newest_revision_and_empty_means_disabled`), `Architecture/FinancialInsightsControllerArchitectureTests.cs` (`Action_declares_its_policy_permission_and_actor`, `Every_action_declares_actor_types_and_rate_limit`), `Infrastructure/PermissionsSnapshotClientTests.cs`.

**Que debe tener un servicio nuevo [Recomendación]**

1. Fitness: toda acción declara `[AllowActorTypes]`, `[HasPermission]` (o justificación) y `[RateLimit]`/`[RateLimitExempt]`.
2. Consumer `UserRolesChanged`: crea, aplica versión mayor, ignora versión menor, re-aplica igual.
3. `RolePermissionsChanged` no altera los códigos efectivos de un usuario.
4. Ciclo de vida: desactivado → lector devuelve null → 403; evento viejo no reactiva.
5. Pull-recovery: miss local + Auth responde → persiste; Auth caído → fail-closed.
6. Repositorios: un tenant no lee filas de otro aunque el `TenantContext` esté vacío.
7. Gate: `null` no deniega, `[]` deniega, módulo presente permite (si el servicio vende módulo).
8. Integración (namespace `.Integration.`): request con JWT humano real a un endpoint `[HasPermission]` → 200; sin fila → 403.

---

## 8. Dependencias

Auth (catálogo, eventos, snapshot), Subscription (entitlements), Redis (caché de módulos, denylist), RabbitMQ (`taxvision-events`), SQL Server (proyecciones), `BuildingBlocks.Web` (`ActorTypeAuthorization`, `Tenancy`, `Security`), `BuildingBlocks.Authorization` (constantes y mapa), Communication/Node (espejo del mapa).

---

## 9. Por qué un servicio funcional puede quedar inaccesible

| Falta | Síntoma |
|---|---|
| `[AllowActorTypes]` en la acción o controller | 403 `Authz.ActorTypeNotDeclared` para todos (salvo PlatformAdmin) |
| `Authorization:PermissionsSource=Projection` | El servicio no arranca (hoy) / 403 universal (antes) |
| Tabla + consumer de `UserPermissionsProjection` | 403 a todo humano (fail-closed) |
| Pull-recovery o su secreto M2M | Usuarios anteriores al servicio: 403 hasta el próximo cambio de rol o la re-publicación de 6 h |
| Permiso en `PermissionCatalog` + migración `InsertData` | Nadie lo tiene → 403 |
| Permiso en el bundle del rol de sistema (`SystemRoleDefaults`) | Admin/empleado sin el permiso → 403 |
| Grant insertado por migración (anti-patrón) | El sync no ve diferencia, no publica → proyecciones sin el permiso → 403 |
| Módulo en el plan / proyección de entitlements | Con enforce: 403 `Authz.ModuleUnavailable` |
| Prefijo en `PermissionModuleMap` | La feature se regala (nadie la gatea) |
| Frontend que no refresca ante `Auth.TokenStale` | 401 en bucle tras un cambio de permisos |
| `[AllowSurface]` | Token del Landing → 403 `Auth.SurfaceNotAllowed` |
| `IgnoreQueryFilters()` + tenant explícito en el repo | Handlers que ven 0 filas (404/"not found") porque el `TenantContext` del scope está vacío |

---

## 10. Material para la guía (servicio de referencia: FinancialInsights)

### Paso 1 — Constantes de permiso en BuildingBlocks
`src/BuildingBlocks/Authorization/FinancialInsightsPermissions.cs`:
```csharp
public static class FinancialInsightsPermissions
{
    public const string Import = "financial.import";
    public const string View = "financial.view";
    public const string PortalImport = "financial.portal.import";
}
```

### Paso 2 — Catálogo en Auth (GUID fijo, módulo, portal sí/no)
`src/Services/Auth/Domain/Roles/PermissionCatalog.cs:538-559`:
```csharp
new(
    new Guid("a1000000-0000-0000-0000-0000000001a0"),
    FinancialImport,
    "financial_insights",
    "Import client bank statements into Financial Insights",
    false
),
```
Y en el bundle del rol de sistema (`:2471-2474`): `FinancialImport, FinancialView,`. Migración: solo `InsertData` en `Permissions`.

### Paso 3 — Módulo en el mapa
`src/BuildingBlocks/Authorization/PermissionModuleMap.cs:51`: `("financial.", "financial_insights"),`

### Paso 4 — `Program.cs`
```csharp
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();
// ...
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
ModuleGateRegistration.AddModuleGate(builder.Services, builder.Configuration);
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());
// Wolverine:
options.Policies.ForMessagesOfType<IIntegrationEvent>()
    .AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.IntegrationEventTenantMiddleware));
options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));
// Pipeline:
app.UseAuthentication();
app.UseMiddleware<BuildingBlocks.Web.Tenancy.JwtTenantContextMiddleware>();
app.UseMiddleware<SessionDenylistMiddleware>();
app.UseAuthorization();
```
(`Program.cs:39-59,115-118,148-154`)

### Paso 5 — Configuración
`appsettings.json` (FI `:27-30`):
```json
"Authorization": {
  "PermissionsSource": "Projection",
  "ModuleGate": { "Enforce": true, "EnforcedModules": ["financial_insights"] }
}
```
Compose (patrón de los otros 14; FI usa `"true"` literal, ver Problema 4):
```yaml
Authorization__PermissionsSource: Projection
Authorization__ModuleGate__Enforce: ${TUSERVICIO_MODULE_GATE_ENFORCE:-false}
Authorization__ModuleGate__EnforcedModules__0: tumodulo
```

### Paso 6 — Controller
`src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/Controllers/ImportBatchesController.cs:23-35`:
```csharp
[ApiController]
[Authorize]
[Route("financial/import-batches")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class ImportBatchesController(...) : ControllerBase
{
    [HttpPost]
    [HasPermission(FinancialInsightsPermissions.Import)]
    [RateLimit("financial.i.import_batch_create")]
    public async Task<IActionResult> Create([FromBody] CreateImportBatchRequest request, CancellationToken ct)
```

### Paso 7 — Proyección + consumers + lector/escritor
Ver Proyecciones.md (entidad `UserPermissionsProjection`, `ProjectionConsumer`, `UserLifecyclePermissionsProjectionConsumer`, `FinancialUserPermissionsReader`).

### Paso 8 — DbContext fail-closed
`FinancialInsightsDbContext.cs:140-154` (filtro `TenantId == EffectiveTenantId` sobre `ITenantOwned`).

### Paso 9 — Verificación
Log de arranque `Module gate registered: ENFORCING (modules: …)` o `log-only`; login real + endpoint `[HasPermission]` → 200; usuario sin el permiso → 403 `Authz.PermissionDenied`.
