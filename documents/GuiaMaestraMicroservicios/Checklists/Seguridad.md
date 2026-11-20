# Checklist — Seguridad de un microservicio

> Propósito: que un servicio nuevo (o un endpoint nuevo) cumpla las capas de seguridad que ya usa la flota: JWT → actor type → permiso → gate de módulo → filtro de tenant → ownership → denylist de sesión.
> Todo ítem sale del código verificado en las investigaciones; lo que falta decidir va al final como "Decisiones pendientes".

**Cuándo aplica:** al crear un servicio .NET con API, al agregar un controller o una acción, al exponer un endpoint interno (`internal/*`), público o webhook, o al cambiar `Program.cs`.

**Formato:** cada ítem trae *Verificación* (comando, prueba o archivo) y *Origen*. Los condicionales llevan la condición entre corchetes.

---

## 1. Autenticación y pipeline

- [ ] **JWT RS256 con clave pública montada.** `AddTaxVisionJwtAuthentication` y, en compose, `Jwt__PublicKeyPath: /run/secrets/jwt-public.pem`, `Jwt__Issuer: TaxVision.Auth`, `Jwt__Audience: TaxVision.Services`. Sin PEM el servicio cae a HS256 con `Jwt:Secret` sin avisar.
  - Verificación: bloque `<svc>-api` de `deploy/docker/docker-compose.yml` (no hay fitness que lo exija: revisión de PR).
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §1.2 y problema 9.
- [ ] **Orden del pipeline:** `UseAuthentication` → `JwtTenantContextMiddleware` → `SessionDenylistMiddleware` → `UseAuthorization` (guardrail G16).
  - Verificación: `Program.cs` del Api comparado con FinancialInsights `Program.cs:148-154`; `deploy/tests/TaxVision.BuildingBlocks.Tests/Tenancy/JwtTenantContextMiddlewareTests.cs`.
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §1.1; [Guardrails](../Referencias/Guardrails.md) #16.
- [ ] **El tenant sale siempre del JWT** (HTTP) o del `TenantId` del evento (consumers). Nunca del body, query, `X-Tenant-Id`, `X-Resolved-Tenant` ni `X-Forwarded-Host`.
  - Verificación: prueba del servicio "header `X-Tenant-Id` enviado por el cliente → ignorado"; revisión de controllers (`this.TryGetTenantAndUser`).
  - Origen: [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §5 y §7; [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §4.

## 2. Actor type y permiso por acción

- [ ] **Cada acción declara** `[AllowActorTypes(...)]` + `[HasPermission(...)]` + `[RateLimit("...")]` (o `[RateLimitExempt("razón")]`). Nunca `[Authorize(Roles=...)]` (G17). Sin `[AllowActorTypes]` → 403 `Authz.ActorTypeNotDeclared`.
  - Verificación: copiar `deploy/tests/TaxVision.Notes.Tests/Architecture/NotesArchitectureTests.cs` como `<Svc>ArchitectureTests` (`Controller_actions_should_declare_AllowActorTypes`, `Controller_actions_should_declare_RateLimit_or_RateLimitExempt`) y correr el gate.
  - Origen: [Testing_Security](../Investigaciones/Testing_Security.md) §9.1; [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §5.
- [ ] **Fuente de permisos en modo Projection:** `IAuthorizationPolicyProvider → PermissionPolicyProvider` (singleton) + `AddUserPermissionsSource(config, Assembly.GetExecutingAssembly())` y `Authorization:PermissionsSource=Projection` en appsettings y compose.
  - Verificación: el servicio no arranca si hay `[HasPermission]` y el modo no es Projection (`UserPermissionsSourceRegistrationTests`); revisar `Authorization__PermissionsSource: Projection` en compose.
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §1.5 y §10 pasos 4-5.
- [ ] **Constantes de permiso** en `src/BuildingBlocks/Authorization/<Svc>Permissions.cs` y **catálogo** en `src/Services/Auth/Domain/Roles/PermissionCatalog.cs` con GUID fijo y módulo.
  - Verificación: `EnforcedPermissionsFitnessTests` y `ReservedPermissionsFitnessTests` (Auth.Tests) en el gate.
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §10 pasos 1-2; [Testing_Security](../Investigaciones/Testing_Security.md) §2.1.
- [ ] **Migración de Auth solo con `InsertData` en `Permissions`.** Nunca `INSERT INTO RolePermissions` para roles de sistema: el grant lo hace `SystemRolePermissionsSyncService` y publica (GC-M1).
  - Verificación: revisar el archivo de migración de Auth; tras arrancar Auth en local, el permiso aparece en la proyección del servicio.
  - Origen: [Migraciones](../Investigaciones/Migraciones.md) "Reglas de permisos"; [Guardrails](../Referencias/Guardrails.md) GC-M1.
- [ ] **Banderas del permiso** `IsDangerous` / `PlatformOnly` / `IsReserved` cuando corresponda; bundles Employee/Portal por lista explícita en `SystemRoleDefaults` (el admin lo recibe por filtro).
  - Verificación: revisión de `PermissionCatalog.cs`; `PermissionCeilingTests` (Auth.Tests).
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §1.7; [Modulos_Planes_Addons](../Investigaciones/Modulos_Planes_Addons.md) §4.
- [ ] [si un endpoint lo comparten varios actores] `[HasPermissionForActor(actor, permiso)]` en vez de apilar dos `[HasPermission]`.
  - Verificación: `HasPermissionForActorAttributeTests` como patrón; revisión del controller.
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §1.4.
- [ ] [si el endpoint acepta tokens del Landing] `[AllowSurface]` explícito; sin él → 403 `Auth.SurfaceNotAllowed`.
  - Verificación: `SurfaceAuthorizationFilterTests` (BB) como patrón.
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §9.

## 3. Proyección de permisos (detalle en [Proyecciones_Reconciliation](Proyecciones_Reconciliation.md))

- [ ] **Consumer de `UserRolesChanged`** con upsert `ApplyIfNewer` por `PermissionsVersion`, y consumer de ciclo de vida (`UserDeactivated/Offboarded/Reactivated`). **No** recomponer permisos desde `RolePermissionsChanged` (resucita denies).
  - Verificación: pruebas del servicio "crea, aplica mayor, ignora menor, re-aplica igual" y "`RolePermissionsChanged` no altera los códigos del usuario" (patrón `ProjectionConsumerTests.User_deny_event_removes_permission_and_role_event_cannot_restore_it`).
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §1.6 y §7.
- [ ] **El lector filtra `IsActive && HasPermissionsSnapshot`.**
  - Verificación: prueba "desactivado → lector null → 403"; revisar el reader (patrón `FinancialProjectionReaders.cs:18-25`).
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) problema 5.
- [ ] **Pull-recovery** (`IPermissionsSnapshotClient` + `IUserPermissionsProjectionWriter`) con cliente M2M configurado; sin él, usuarios anteriores al servicio reciben 403.
  - Verificación: prueba "miss local + Auth responde → persiste; Auth caído → fail-closed" (patrón `PermissionsSnapshotClientTests`).
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §6 y §7.

## 4. Aislamiento por tenant

- [ ] **Filtro global fail-closed** sobre todo `ITenantOwned`: sin tenant compara contra `Guid.Empty` (G12).
  - Verificación: `TenantIsolationTests` del DbContext (patrón `deploy/tests/TaxVision.Notification.Tests/Persistence/TenantIsolationTests.cs`).
  - Origen: [Testing_Security](../Investigaciones/Testing_Security.md) §9.2; [Guardrails](../Referencias/Guardrails.md) #12.
- [ ] **`IgnoreQueryFilters()` solo con `tenantId` explícito** o una de las 4 condiciones seguras, con un comentario breve que diga cuál (G8).
  - Verificación: prueba de repositorio "contexto sin tenant encuentra la fila del tenant pasado; otro tenant → null".
  - Origen: [Guardrails](../Referencias/Guardrails.md) #8; [Migraciones](../Investigaciones/Migraciones.md) "Buenas prácticas".
- [ ] [si el servicio consume eventos o encola comandos] `IntegrationEventTenantMiddleware` (para `IIntegrationEvent`) y `LocalCommandTenantMiddleware` registrados en `UseWolverine`.
  - Verificación: prueba "evento con `TenantId = Guid.Empty` → falla cerrado"; `IntegrationEventTenantMiddlewareTests`, `LocalCommandTenantMiddlewareTests` (BB).
  - Origen: [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §1.9; [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §9.
- [ ] [si hay recursos con autor] Ownership (`AddResourceOwnershipOptions`, `AddOwnershipAuthorization<T>()`).
  - Verificación: prueba de ownership del controller (patrón `NotesControllerOwnershipTests`).
  - Origen: [Testing_Security](../Investigaciones/Testing_Security.md) §2.3 y §5; [Arquitectura](../Investigaciones/Arquitectura.md) `Program.cs` paso 9.
- [ ] **Prueba de negación con actor real**: token de staff sin el permiso → 403 `Authz.PermissionDenied` (no probar solo con PlatformAdmin, que salta permisos).
  - Verificación: T-INT con `JwtTestTokenFactory` firmado con el secreto aleatorio de la fábrica.
  - Origen: [Testing_Security](../Investigaciones/Testing_Security.md) problema 2 y §9.3.

## 5. Revocación de sesión (denylist)

- [ ] **`AddRedisCache` → `AddSessionDenylist` → `UseMiddleware<SessionDenylistMiddleware>()`** entre autenticación y autorización. Misma `ConnectionStrings:Redis` (e `InstanceName` `taxvision:`) que Auth. No usar `AddSessionDenylist` en Auth.
  - Verificación: `Program.cs`; compose `ConnectionStrings__Redis: redis:6379`; `SessionDenylistMiddlewareTests` (BB).
  - Origen: [Session_Denylist](../Investigaciones/Session_Denylist.md) §1.3 y §10.
- [ ] **La revocación de una sesión es `DELETE auth/sessions/{sessionId}`** (todas: `DELETE auth/sessions`). No existe `POST /auth/sessions/{sid}/revoke`.
  - Verificación: `src/Services/Auth/Api/Controllers/SessionsController.cs:55,77`.
  - Origen: [Session_Denylist](../Investigaciones/Session_Denylist.md) §1.2 y problema 2.
- [ ] **Prueba de integración**: JWT con `sid` denylistado en Redis real → 401 `Auth.SessionRevoked`; token M2M (sin `sid`) no consulta Redis.
  - Verificación: T-INT en namespace `.Integration.` del servicio.
  - Origen: [Session_Denylist](../Investigaciones/Session_Denylist.md) §7.
- [ ] [si el servicio tiene WebSocket propio] Validar la denylist en el handshake, cerrar el socket al `exp` del token y suscribirse al canal `auth:session-revoked`.
  - Verificación: patrón de Communication (`jwt-verifier.ts`, `session-handlers.ts`) y sus pruebas Vitest.
  - Origen: [Session_Denylist](../Investigaciones/Session_Denylist.md) §1.4 y §5.

## 6. Superficie M2M (`internal/*`)

- [ ] **Endpoints M2M bajo `internal/...`** con `[Authorize(Policy = "ServiceOnly")]`, `[AllowActorTypes(ActorType.Service)]` y `[RateLimitExempt("M2M interno ...")]`. Nunca enrutados en el Gateway.
  - Verificación: `InternalRouteConventionFitnessTests` (BB) en el gate; el Gateway responde 404 a `/internal`.
  - Origen: [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §10.6; [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §9.5.
- [ ] [si el endpoint interno es cross-tenant] Exigir `PlatformTenant.Id` en el token (`User.TryGetTenantId(out var t) && t == PlatformTenant.Id`).
  - Verificación: prueba "token Service de otro tenant → 403".
  - Origen: [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §7 y §10.6; [Reconciliation](../Investigaciones/Reconciliation.md) §5.
- [ ] [si el servicio llama a otros por M2M] **Cliente propio** `<svc>-worker` registrado en `auth-api` (`ServiceAuth__Clients__<N>__ClientId/Secret/Permissions__*`) con permisos mínimos; índice `N` único.
  - Verificación: bloque `auth-api` del compose; user-secrets de Auth en local.
  - Origen: [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §5, §6 y §10.1.
- [ ] [si llama por M2M] **Acquirer con caché** (`ExpiringValueCache` estática por tenant, patrón Calendar), claves `ServiceAuthClient:*` y warning cuando falta config; `HttpClient` con `AllowAutoRedirect = false` y timeout.
  - Verificación: pruebas "sin config → null + warning", "N llamadas → un solo POST", "token ausente → null sin llamar al destino".
  - Origen: [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §7, §10.4 y §10.5.
- [ ] [si hay secreto M2M nuevo] Secreto en el environment `production-extra`, línea en el paso *Append production-extra secrets to .env* de `deploy.yml` y nombre en `ProductionExtraSecretsDeployTests`.
  - Verificación: `ProductionExtraSecretsDeployTests` y `ServiceClientSecretsDeployWiringTests` en el gate.
  - Origen: [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §10.3; [CI_CD_Secrets](../Investigaciones/CI_CD_Secrets.md) §5.

## 7. Endpoints públicos, webhooks y secretos en base

- [ ] [si hay webhook] Verificación con `SharedSecretWebhookVerifier` (comparación en tiempo constante, rechazo si no hay secreto) (G23).
  - Verificación: una prueba de firma por webhook (patrón `StripeWebhookVerificationTests`, `WebhooksControllerTests` de Sms).
  - Origen: [Guardrails](../Referencias/Guardrails.md) #23; [Testing_Security](../Investigaciones/Testing_Security.md) §2.3.
- [ ] [si hay endpoint anónimo con riesgo de abuso] Regla en `GatewayRateLimiting:PreAuthByIp:Rules` y `MaxRequestBodySize` en una ruta propia.
  - Verificación: `appsettings.json` del Gateway; patrón `GatewayPublicBookingRouteTests`.
  - Origen: [Gateway_YARP](../Investigaciones/Gateway_YARP.md) §9.4; [Testing_Security](../Investigaciones/Testing_Security.md) §6.
- [ ] [si hay login o refresh fuera del host de la oficina (app móvil, otra app)] **Superficie de sesión propia** en `SessionSurface`: su refresh solo rota su cadena y nunca acepta tokens web (que sí pasan por el binding de host). La sesión única se cuenta por canal (`UserSession.CompetesWith`). Los frenos de fuerza bruta se reutilizan de `CentralLoginAuthentication` (IP + email por tipo de cuenta), no se copian. El caso de uso vive en Application; el controller solo entrega el mensaje. **La superficie la fija el controller, nunca el body** (si un comando se toma del body, se sobrescribe con `with { Surface = ... }`), y quién puede abrir esa superficie se decide en una política explícita (`MobileSurfacePolicy`).
  - Verificación: pruebas "token web en la ruta nueva → `Auth.InvalidRefreshToken`" y "token nuevo en el refresh web → rechazado" (patrón `RefreshAccessTokenHandlerTests`); prueba del controller que fija la superficie (`MobileAuthControllerTests`).
  - Origen: [Decisiones_Arquitectonicas](../Referencias/Decisiones_Arquitectonicas.md) C-59; revisión del servicio Mobile (2026-11-19, O2).
- [ ] [si la ruta pública lleva credencial en la URL] Partición `Token` en la política de rate limit (parámetro de ruta `token`).
  - Verificación: definición en `RateLimitPolicyCatalog.<Svc>.cs`.
  - Origen: [RateLimit_Cuotas_Gates](../Investigaciones/RateLimit_Cuotas_Gates.md) §1.2 y §5.
- [ ] [si la ruta pública con token vive bajo el host de una oficina] Validar Host↔token con `X-Resolved-Tenant` (patrón `PublicSignatureHostGuardMiddleware`); el tenant de escritura sale del token.
  - Verificación: pruebas "host A + token B → 403", "token inválido → su propio error, no 403".
  - Origen: [Tenant_Subdomains](../Investigaciones/Tenant_Subdomains.md) §5, §7 y §9.
- [ ] **Sin PII en la URL**: si se identifica por email, `POST` con el email en el cuerpo.
  - Verificación: revisión de rutas del controller.
  - Origen: [Tenant_Links](../Investigaciones/Tenant_Links.md) §5; [Offboarding](../Investigaciones/Offboarding.md) §13.7.
- [ ] [si guarda secretos en su base] `AddSecretProtection` / `AddRotatingSecretProtection`.
  - Verificación: `SecretProtectionRegistrationTests` (BB) como patrón.
  - Origen: [BuildingBlocks](../Investigaciones/BuildingBlocks.md) matriz "Security".

## 8. Rate limit por servicio

- [ ] **Políticas del servicio** en `src/BuildingBlocks/RateLimiting/Catalog/RateLimitPolicyCatalog.<Svc>.cs` con nombre `<svc>.<cat>.<accion>`; endpoint humano con partición `Tenant|User` + overlay `Tenant`. Nombre inexistente o partición no soportada → 500.
  - Verificación: prueba de cuotas acordadas (patrón `FinancialInsightsRateLimitPolicyTests.Policies_have_the_agreed_quotas`); `RateLimitPolicyCatalogTests` (BB).
  - Origen: [RateLimit_Cuotas_Gates](../Investigaciones/RateLimit_Cuotas_Gates.md) §5, §6 y §10.1.
- [ ] **Registro en `Program.cs`**: `AddTaxVisionRedis`, `IRateCounter → RedisRateCounter`, lectores de plan bajo `RateLimit:EnforceTierQuotas`, `AddTieredRateLimiting()`.
  - Verificación: T-INT con Redis real "exceder la cuota → 429 con `Retry-After` y `X-RateLimit-Policy`".
  - Origen: [RateLimit_Cuotas_Gates](../Investigaciones/RateLimit_Cuotas_Gates.md) §7 y §10.2.

---

## Síntomas de un servicio "funcional pero inaccesible"

| Síntoma | Falta probable | Origen |
|---|---|---|
| 403 `Authz.ActorTypeNotDeclared` | `[AllowActorTypes]` | [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §9 |
| 403 a todo humano | Proyección de permisos vacía y sin pull-recovery | [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §9 |
| 401 `Auth.TokenStale` en bucle | El front no refresca el token | [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §9 |
| 401 `Auth.SessionRevoked` | Sesión denylistada (correcto) | [Session_Denylist](../Investigaciones/Session_Denylist.md) §10 |
| 404/"not found" en handlers | Falta `IgnoreQueryFilters()` + tenant explícito | [Auth_RBAC](../Investigaciones/Auth_RBAC.md) §9 |

## Decisiones pendientes (no resolver por cuenta propia)

- `FailureMode` de la denylist: .NET `FailOpen` implícito vs Communication `FailClosed` ([Session_Denylist](../Investigaciones/Session_Denylist.md) problema 1).
- Allowlist por `client_id`/scope en destinos `ServiceOnly` (SEC-001) y criterio único de PlatformTenant en endpoints cross-tenant ([M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md) §3).
- Mover `TenantIsolationTests` con InMemory fuera de `Persistence/` para que entren al gate ([Testing_Security](../Investigaciones/Testing_Security.md) §5).
