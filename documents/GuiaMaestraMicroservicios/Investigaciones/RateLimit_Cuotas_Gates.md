# Rate limiting de seguridad, cuotas por plan, feature gates y entitlements

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\` salvo otra raíz indicada.
> Etiquetas: **[Código verificado]** · **[Documentación histórica]** · **[Recomendación]** · **[Decisión pendiente]**.

---

## 1. Estado actual: cinco mecanismos distintos que no hay que confundir

| Mecanismo | Pregunta que responde | Dónde vive | Respuesta | Ante fallo de infraestructura |
|---|---|---|---|---|
| **Rate limit de seguridad (pre-auth)** | ¿Esta IP está martillando login/reset/registro? | Gateway, `GatewayRateLimiting` (limiter nativo de ASP.NET) | 429 | conocido (config por defecto) |
| **Load shedding** | ¿La flota está saturada? | Gateway, `LoadSheddingMiddleware` | 503 `LoadShedding.Active` | — |
| **Rate limit por tenant/usuario (tiered)** | ¿Este usuario/tenant excede su ritmo en este endpoint? | Cada servicio, `[RateLimit("…")]` + `TieredRateLimitEvaluator` (Redis) | 429 `RateLimit.Exceeded` | **fail-open** |
| **Cuota por plan** (escala el rate limit) | ¿Cuánto ritmo le corresponde a su plan? | `PlanRateLimits` en Subscription → `RateLimitQuotaResolver` | (ajusta el número) | cae a cuota base |
| **Feature gate / entitlement de módulo** | ¿Su plan incluye este módulo? | `PermissionPolicyProvider` + `PermissionModuleMap` + proyección de módulos | 403 `Authz.ModuleUnavailable` | sin fila → no gatea |
| **Cuota de uso de negocio** (entitlement numérico) | ¿Le quedan unidades este mes (p. ej. documentos con IA)? | Código de dominio del servicio, desde la proyección de entitlements | regla de negocio (`Result`) | según el servicio |
| **Gate de asientos** (seat slot) | ¿Este usuario tiene asiento dentro del cupo pagado? | Gateway, `SeatSlotGateMiddleware` | 4xx (modo `Enforce`) | registra fallo |

### 1.1 Gateway [Código verificado]

- Pipeline: `InternalSurfaceGuardMiddleware` (404 a `/internal` y al grant `/auth/service-token`) → CORS → `UseAuthentication` → `MfaEnrollmentGuardMiddleware` → `SeatSlotGateMiddleware` → `UseRateLimiter` → `UseAuthorization` → `LoadSheddingMiddleware` → `TenantPropagationMiddleware` → `TenantHostGuardMiddleware` → YARP — `src/Gateway/TaxVision.Gateway/Program.cs:155-192`.
- `GatewayRateLimitOptions` (sección `GatewayRateLimiting`): `PreAuthByIp` 30/60 s por `IP + path` para `/auth/login`, `/auth/mfa/verify`, `/auth/password/*`, `/auth/me/email/confirm`, `/auth/invitations/accept`, `POST /tenants` (+ reglas de reservas públicas de Calendar en `appsettings.json`); `StorageUploadByTenant` sin reglas por defecto — `src/BuildingBlocks/BuildingBlocks.Web/RateLimiting/GatewayRateLimitOptions.cs:16-60`, `src/Gateway/TaxVision.Gateway/appsettings.json:46-110`.
- Gate de asientos: `Seats:SlotGating:Mode` = `Disabled | Shadow | Enforce` (default `Disabled`); exentos `/auth/me`, `/auth/logout`, `/health`, `/internal` — `src/BuildingBlocks/Authorization/SlotGating/SlotGatingOptions.cs:3-16`, `src/BuildingBlocks/BuildingBlocks.Web/Session/SeatSlotGateMiddleware.cs:18-24,122`.

### 1.2 Rate limit tiered por servicio [Código verificado]

- Atributo: `[RateLimit("nombre.de.politica")]` es un `IAsyncResourceFilter` (corre **antes** del model binding, como `[Authorize]`) — `src/BuildingBlocks/BuildingBlocks.Web/RateLimiting/RateLimitAttribute.cs:13-48`.
  - Partición `Token`: la clave sale del route value `token` (rutas públicas con credencial en la URL) (`:57-83`).
  - Resto: necesita `tenant_id` y `sub`; si faltan **deja pasar sin contar** y suma `RecordMissingClaims` (`:85-90,108-117`).
  - Categoría M (dinero que sale): audita también el 429 vía `IRateLimitAuditSink` (`:102-103,119-159`).
  - Respuesta: headers `X-RateLimit-Policy/Layer/Limit/Remaining/Reset` + cuerpo único (`RateLimitRejection`) (`:168-194`).
- `[RateLimitExempt("razón")]` para lo que no se limita (M2M interno, health, emisión de token) — `RateLimitExemptAttribute.cs:12`.
- Registro: `services.AddTieredRateLimiting()` registra registry, resolver, evaluador, contador Redis algoritmo-consciente, métricas y sinks/lectores "null" por defecto con `TryAdd` (un servicio con lector real lo registra **antes**) — `src/BuildingBlocks/BuildingBlocks.Web/RateLimiting/TieredRateLimitingRegistration.cs:37-52`.
- Evaluador: `TieredRateLimitEvaluator` — `src/BuildingBlocks/BuildingBlocks.Infrastructure/RateLimiting/TieredRateLimitEvaluator.cs`:
  - Resolución de cuota que falla → cuota base (fail-open, `:51-60`).
  - Contador Redis que falla → deja pasar (`:96-98`; overlay `:130-132`; endpoint cap `:155-157`).
  - Particiones soportadas: `Tenant|User`, `User`, `Tenant`, `Token`; overlay solo `[Tenant]`; cualquier otra combinación lanza `NotSupportedException` (`:109-112,188-209`).
  - Clave: `<svc>:rl:<policy>:<parts>…` (`:215-216`; test `TieredRateLimitEvaluator_keys_follow_the_canonical_svc_rl_policy_format`).
- Catálogo central (BuildingBlocks, un `partial` por servicio): `RateLimitPolicyCatalog.Define(name, category, primaryPartition, overlayLayers, quota, windowSeconds, algorithm, overlayQuota)`; I lleva además cap por endpoint = overlay × 20 — `src/BuildingBlocks/RateLimiting/Catalog/RateLimitPolicyCatalog.cs:19-76`. `GetByName` lanza `KeyNotFoundException` si el nombre no existe (`:39-42`). ADR_020 explica por qué sigue en BuildingBlocks.
- Taxonomía A..Q (A-E pre-auth/público/webhook, F-L tenant, M money-out, N reveal, O sockets, P health, Q load shedder) — `src/BuildingBlocks/RateLimiting/RateLimitCategory.cs:9-64`. Particiones — `RateLimitPartitionDimension.cs:10-19`. Definición — `RateLimitPolicyDefinition.cs:9-51`.

### 1.3 Cuotas por plan [Código verificado]

- `RateLimitQuotaResolver`: solo F..O escalan (`ScalesByPlan`, `:20-32`); plan desconocido → cuota base con `IsFallback=true` (`:44-45`); fila de `PlanRateLimits` ausente → base; `HardOverridePerMinute` reemplaza; si no, `MultiplierOverride` sobre base y overlay (`:54-62,71-72`) — `src/BuildingBlocks/RateLimiting/Quota/RateLimitQuotaResolver.cs`.
- Plan del tenant: `ITenantPlanCodeReader` sobre la proyección local (`TenantPlanCodeProjection` o, en FI, `TenantEntitlementsProjection`) con caché Redis 5 min (`CachedTenantPlanCodeReader.cs:19-41`, clave `ratelimit:plancode:{tenant}`) invalidada por el consumer de `TenantEntitlementsChanged`.
- Multiplicadores: `HttpPlanRateLimitReader` trae **todo** el catálogo `GET internal/plan-rate-limits` de Subscription con token M2M (`IServiceTokenAcquirer` del servicio, tenant PlatformTenant), caché 5 min, último catálogo bueno 24 h, back-off 30 s si Subscription no responde — `src/BuildingBlocks/BuildingBlocks.Infrastructure/RateLimiting/HttpPlanRateLimitReader.cs:11-40,118`.
- Lectores singleton con scope propio para evitar dependencia cautiva — `ScopedRateLimitReaders.cs:12-34`.
- Activación por servicio: flag `RateLimit:EnforceTierQuotas`; FI lo pone `true` y registra los dos lectores reales antes de `AddTieredRateLimiting` — `src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/Program.cs:61-72`. En compose: Customer `${CUSTOMER_ENFORCE_TIER_QUOTAS:-true}`, tres servicios `"true"` literal (incluido FI), uno `"false"` (`deploy/docker/docker-compose.yml:818,1598,1656,1709,2373`).

### 1.4 Feature gates y entitlements [Código verificado]

- **Gate de módulo** (detalle en Auth_RBAC.md §1.8): cuelga de `[HasPermission]`; un endpoint sin permiso **no pasa por el gate** (`PermissionPolicyProvider.cs:55-97`; memoria `feedback_comprobacion_de_recurso_no_es_comprobacion_de_permiso.md`).
- **Entitlements** = snapshot de Subscription `EntitlementValues` (`module.*` → `True/False`, `seats.max`, `storage.max_bytes`, `financial.documents_per_month`…) en `TenantEntitlementsChangedIntegrationEvent` — `src/BuildingBlocks/Messaging/SubscriptionIntegrationEvents/TenantEntitlementsChangedIntegrationEvent.cs`. Módulos extraídos con `ExtractEnabledModules` (`TenantEntitlementModuleExtensions.cs`).
- **Cuota de uso de negocio (ejemplo FI)**: `FinancialEntitlements.DocumentsPerMonthKey = "financial.documents_per_month"`; la proyección guarda `DocumentsPerMonth` y el worker aplica `MonthlyDocumentLimit(MaxDocumentsPerTenantMonth)` con fallback de configuración — `src/Services/FinancialInsights/TaxVision.FinancialInsights.Application/Imports/FinancialEntitlements.cs`, `TaxVision.FinancialInsights.Domain/Projections/AccessProjections.cs:171`, `TaxVision.FinancialInsights.Infrastructure/Processing/FinancialOcrWorker.cs:155`. El acceso por módulo en el worker: `ImportProcessingQueue.cs:206` (`plan.HasModule(...) ? Allowed : Revoked`).
- **Cupo de usuarios staff**: Auth proyecta `MaxStaffUsers`/`seats.max` y lo aplica en `PlanGuard` (`src/Services/Auth/Application/Common/PlanGuard.cs:7`; doc del campo en `TenantEntitlementsChangedIntegrationEvent.cs`).

---

## 2. Evidencia documental

- `documents/architecture/ADR_017_RateLimit_Layers.md` §1 (modelo de 4 capas) y §2-§2.2 (fases) [Documentación histórica].
- `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md` §6 (12 invariantes) y §7 (anti-patrones) [Documentación histórica, vigente].
- `documents/RateLimit/Plan_Implementacion_Fases.md` §4-§5 (taxonomía y cuotas), `Plan_Flexibilizacion_RateLimit_y_LoadShedding.md` (cuotas aprobadas, load shedding sin falsos positivos).
- Vault `C:\Users\devcacg\Desktop\Implementaciones\RateLimit\PLAN_RateLimit_Flexibilizacion.md`.
- Memoria: `feedback_particion_token_ratelimit.md` (declarar una partición no la implementa; endpoint anónimo falla abierto), índice de RateLimit (fases 1-9 cerradas, `project_ratelimit_fase9_cierre_plan_completo.md`), `project_ratelimit_auditoria_hallazgo2_m2m_completo.md`.

---

## 3. Problemas detectados

1. **Doc ↔ código en el ADR**: el ADR (§2.1) dice que no hay implementación concreta de `IPlanRateLimitReader`; hoy existe `HttpPlanRateLimitReader` compartido. Histórico, no erróneo, pero la guía debe citar el código.
2. **`[RateLimit]` en endpoint anónimo sin partición `Token` no limita nada** (fail-open por claims faltantes, `RateLimitAttribute.cs:85-90`). Solo se ve en la métrica `missing_claims`.
3. **Flags `EnforceTierQuotas` heterogéneos** (variable, literal `true`, literal `false`). **[Decisión pendiente]**: patrón único.
4. **Servicio nuevo sin proyección de plan poblada** (ver Proyecciones.md §3.3) → todos sus tenants caen a la cuota base (la de menor plan) hasta que cambie su plan: un tenant grande puede recibir 429 que su plan no justificaría.
5. **Nombre de política inexistente** en `[RateLimit("…")]` → `KeyNotFoundException` en runtime (500). Lo previenen las fitness de catálogo/servicio, no el compilador.
6. **El gate de módulo y la cuota de uso no comparten semántica de "sin fila"**: el gate abre (null), FI cierra el procesamiento hasta reparar el plan (pull). Coherente con su riesgo, pero hay que explicarlo en la guía.

---

## 4. Buenas prácticas observadas

- Separación clara: seguridad pre-auth en Gateway (IP), ritmo por tenant/usuario en servicio (Redis, distribuido), negocio en dominio.
- Fail-open del limitador ante Redis caído + métricas (`ratelimit.fallback_open_total`, `missing_claims`).
- Resource filter (429 antes de deserializar el body).
- Contrato único de 429 y 503 que los frontends saben mostrar sin cerrar sesión (guía §6.11).
- Cuotas en catálogo y multiplicador por plan, nunca números en el controller.
- Fitness por servicio: toda acción declara `[AllowActorTypes]` y `[RateLimit]`/`[RateLimitExempt]` (FI `FinancialInsightsControllerArchitectureTests.Every_action_declares_actor_types_and_rate_limit`).

---

## 5. Recomendación

- Endpoint humano: `[RateLimit("<svc>.<cat>.<slug>")]` con partición `Tenant|User` + overlay `Tenant`; categoría según el árbol de la guía (F lectura, G escritura, H listados, I bulk, J cómputo caro…).
- Endpoint público con credencial en la ruta: partición `Token` y parámetro de ruta `token`.
- Endpoint M2M interno: `[RateLimitExempt("M2M interno …")]`.
- Venta de feature: permiso con prefijo mapeado a módulo + gate. Límite de unidades: entitlement numérico leído de la proyección y aplicado en el dominio con `Result`, nunca con el rate limiter.
- `RateLimit:EnforceTierQuotas=true` solo cuando la proyección de plan y el acquirer M2M existen.

---

## 6. Riesgos

| Riesgo | Efecto |
|---|---|
| Política no registrada en el catálogo | 500 en el endpoint |
| Partición no soportada | `NotSupportedException` → 500 |
| Redis caído | Sin límite (fail-open), métricas al alza |
| Subscription caído sin catálogo previo | Cuota base para todos (back-off 30 s) |
| Endpoint anónimo con partición de tenant | Sin límite efectivo |
| Confundir rate limit con cuota de negocio | 429 donde debía haber un error de dominio con mensaje de plan |

---

## 7. Pruebas

**Existentes [Código verificado]**
- `deploy/tests/TaxVision.BuildingBlocks.Tests/RateLimit/RateLimitFitnessFunctionsTests.cs`: `No_service_uses_StringIncrementAsync_outside_RedisRateCounter`, `AddRateLimiter_native_only_appears_in_the_frozen_allowlist`, `Every_native_AddRateLimiter_uses_the_shared_rejection_contract`, `Native_limiters_report_the_real_wait`, `TieredRateLimitEvaluator_keys_follow_the_canonical_svc_rl_policy_format`.
- `…/RateLimit/RateLimitPolicyCatalogTests.cs`, `RateLimitPolicyRegistryTests.cs`, `RateLimitQuotaResolverTests.cs`, `TieredRateLimitEvaluatorTests.cs`, `HttpPlanRateLimitReaderTests.cs`, `CachedTenantPlanCodeReaderTests.cs`, `CachedTenantEntitlementModulesReaderTests.cs`, `RateLimitRejectionTests.cs`, `GatewayRateLimitRuleTests.cs`, `AlignedFixedWindowRateLimiterTests.cs`, `RedisRateCounterTests.cs`, `RateLimitMetricsTests.cs`.
- FI: `deploy/tests/TaxVision.FinancialInsights.Tests/Architecture/FinancialInsightsRateLimitPolicyTests.cs` (`Policies_have_the_agreed_quotas`), `FinancialInsightsControllerArchitectureTests.cs` (`Every_action_declares_actor_types_and_rate_limit`).
- Auth: `deploy/tests/TaxVision.Auth.Tests/Integration/RateLimitIntegrationTests.cs`.

**Que debe tener un servicio nuevo [Recomendación]**
1. Fitness de controller: `[RateLimit]` o `[RateLimitExempt]` en cada acción; el nombre existe en el catálogo; la política es del propio servicio.
2. Test de cuotas acordadas (patrón `Policies_have_the_agreed_quotas`).
3. Integración (`.Integration.`, Redis real): exceder la cuota → 429 con `Retry-After` y `X-RateLimit-Policy`.
4. Si hay cuota de negocio: test de dominio "N+1 en el período → error de negocio", con ancla de período en la zona de la oficina.
5. Si vende módulo: tests del gate (`null`/`[]`/presente) — ver Auth_RBAC.md.

---

## 8. Dependencias

Redis (contadores, caché de plan y módulos), Subscription (`PlanRateLimits`, `internal/plan-rate-limits`, snapshot de entitlements), proyección de plan local, acquirer M2M, Gateway (pre-auth, load shedding, seats), `BuildingBlocks.RateLimiting` (catálogo), `BuildingBlocks.Web.RateLimiting`, `BuildingBlocks.Infrastructure.RateLimiting`.

---

## 9. Por qué un servicio funcional puede quedar inaccesible

- Módulo no incluido en el plan (lista `[]` o sin el módulo) con enforce → 403 `Authz.ModuleUnavailable`.
- `Seats:SlotGating:Mode=Enforce` y usuario fuera del cupo → bloqueado en el Gateway antes de llegar al servicio.
- Cuota base por proyección de plan ausente → 429 prematuros en tenants grandes.
- Load shedding activo → 503 en peticiones no críticas.
- Política mal nombrada o partición no soportada → 500.

---

## 10. Material para la guía (servicio de referencia: FinancialInsights)

### 10.1 Políticas en el catálogo central
`src/BuildingBlocks/RateLimiting/Catalog/RateLimitPolicyCatalog.FinancialInsights.cs:3-53`:
```csharp
public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition FinancialImportBatchRead = Define(
        "financial.f.import_batch_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition FinancialImportBatchCreate = Define(
        "financial.i.import_batch_create",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 600
    );
}
```

### 10.2 `Program.cs` (`:61-72`)
```csharp
builder.Services.AddTaxVisionRedis(
    builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing."));
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();
if (builder.Configuration.GetValue<bool>("RateLimit:EnforceTierQuotas"))
{
    builder.Services.AddSingleton<ITenantPlanCodeReader, ScopedTenantPlanCodeReader>();
    builder.Services.AddSingleton<IPlanRateLimitReader, ScopedPlanRateLimitReader>();
}
builder.Services.AddTieredRateLimiting();
```

### 10.3 Infrastructure (`DependencyInjection.cs:90-106`)
```csharp
services.AddCachedTenantEntitlementModulesReader<FinancialTenantModulesReader>();
services.AddScoped<FinancialTenantPlanCodeReader>();
services.AddScoped(sp => new CachedTenantPlanCodeReader(
    sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
    sp.GetRequiredService<FinancialTenantPlanCodeReader>()));
services.AddScoped<ITenantPlanCodeCacheInvalidator, TenantPlanCodeCacheInvalidator>();
services.AddHttpClient<HttpPlanRateLimitReader>(http =>
{
    http.BaseAddress = BaseUri(configuration, "SubscriptionClient:BaseUrl");
    http.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(NoRedirects);
```

### 10.4 Uso en el controller
```csharp
[HttpPost]
[HasPermission(FinancialInsightsPermissions.Import)]   // permiso + gate de módulo
[RateLimit("financial.i.import_batch_create")]         // ritmo por usuario/tenant, escalado por plan
public async Task<IActionResult> Create(...)
```

### 10.5 Configuración
```json
"RateLimit": { "EnforceTierQuotas": true },
"SubscriptionClient": { "BaseUrl": "http://localhost:5360" }
```
Compose: `RateLimit__EnforceTierQuotas`, `SubscriptionClient__BaseUrl: http://subscription-api:8080`, `ConnectionStrings__Redis: redis:6379`.

### 10.6 Cuota de negocio (no rate limit)
Leer `financial.documents_per_month` del snapshot en el consumer (`FinancialEntitlements.ParseDocumentsPerMonth`), guardarlo en la proyección y aplicarlo en el dominio/worker con fallback de configuración (`MonthlyDocumentLimit(settings.MaxDocumentsPerTenantMonth)`), con el período anclado al día de inicio de la suscripción en la zona de la oficina (`UsagePeriodStartUtc`, `AccessProjections.cs:165-166`).
