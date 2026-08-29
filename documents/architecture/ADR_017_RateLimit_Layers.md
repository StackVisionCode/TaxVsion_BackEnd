# ADR-017 — Rate Limiting Multi-Capa por Tenant

**Estado**: Aceptado — **plan de 9 fases CERRADO** (2026-08-02). Fase 4 cerró los 16 servicios,
Fase 5 el load shedder de Gateway, Fase 6 el piloto de tier-aware quotas en Customer, Fase 7 el
port de rate limiting atómico en Communication/Node, Fase 8 las métricas OTel + dashboards Grafana
+ alertas, y Fase 9 las fitness functions de cierre (encontró y corrigió 4 endpoints reales de
PaymentClient sin migrar) + README + Postman. Ver
`documents/RateLimit/Plan_Implementacion_Fases.md` para el detalle de cada fase.
**Fecha**: 2026-08-01
**Contexto de la decisión**: investigación de industria (Stripe, Shopify, Zendesk, Atlassian, Salesforce, Auth0, HubSpot) + auditoría del rate-limiting actual del monorepo, solicitada para validar una recomendación previa de un senior de particionar por tenant.

---

## 1. Decisión

Se adopta el **modelo canónico de 4 capas** para todo rate-limiting del monorepo:

```
Capa 1 — Global de infra (load shedder de flota, Gateway)
Capa 2 — Per-tenant (partición primaria, escalada por plan)
Capa 3 — Per-user / per-token dentro del tenant
Capa 4 — Per-endpoint (cost-based cap)
```

Las 4 capas se evalúan en orden por request; la primera que dispara devuelve `429` con `Retry-After` y el header de qué capa disparó. El senior tenía razón en la dirección — tenant como partición primaria es correcto y es lo que usan los 10/10 SaaS B2B grandes investigados — pero **una sola capa (solo tenant) es insuficiente**: sin overlay per-user un solo script/agente runaway dentro de un tenant grande consume toda su cuota y afecta a sus compañeros de equipo; sin capa global un tenant dentro de su cuota individual puede seguir tumbando la flota si la suma agregada supera capacidad; sin capa per-endpoint, un endpoint caro (bulk import, render) comparte presupuesto con endpoints livianos.

Detalle completo de las 17 categorías de endpoint (A-Q), cuotas base, algoritmos y multiplicadores por plan: `documents/RateLimit/Plan_Implementacion_Fases.md` §4-§5 (documento hermano de este ADR, no se duplica aquí).

## 2. Alcance de Fase 1 (lo que este ADR congela ahora)

- `BuildingBlocks/RateLimiting/RateLimitCategory.cs` — enum A..Q.
- `BuildingBlocks/RateLimiting/RateLimitAlgorithm.cs` — FixedWindow/SlidingWindow/TokenBucket/LeakyBucket.
- `BuildingBlocks/RateLimiting/RateLimitPartitionDimension.cs` — flags (Ip/Email/Tenant/User/Token/AccountOrProvider).
- `BuildingBlocks/RateLimiting/RateLimitPolicyName.cs` — VO validando `<servicio>.<categoría>.<slug>`.
- `BuildingBlocks/RateLimiting/RateLimitPolicyDefinition.cs` — record de una política.
- `BuildingBlocks/RateLimiting/RateLimitPolicyCatalog.cs` — catálogo estático, sembrado inicialmente con 25 políticas representativas (al menos una por cada una de las 15 categorías con cupo — A-O; P/Q no llevan políticas propias, ver §4 de este ADR).
- Tabla `PlanRateLimits` en Subscription (`PlanCode`, `Category`, `MultiplierOverride`, `HardOverridePerMinute`), sembrada con 30 filas (3 planes reales × 10 categorías con tenant, F-O).

Lo que **no** se construye en Fase 1: el registry/resolver runtime (Fase 2), el middleware unificado (Fase 3), ni la migración servicio-por-servicio (Fase 4). El catálogo de políticas hoy es un esqueleto correcto y completo por categoría, no la lista exhaustiva de ~50-80 endpoints — esa lista se completa incrementalmente en el piloto de Customer (Fase 3) y en cada sub-fase de migración (Fase 4), donde cada servicio hace su propio inventario de endpoints reales antes de nombrar su política. Fabricar cuotas para endpoints no auditados todavía habría sido invención, no diseño.

## 2.1 Alcance de Fase 2 (registry runtime + resolver tier-aware)

- `EffectiveQuota` — record de salida del resolver (`PermitCount`, `WindowSeconds`, `IsFallback`).
- `IRateLimitPolicyRegistry` + `RateLimitPolicyRegistry` — wrapper inyectable sobre `RateLimitPolicyCatalog`.
- `ITenantPlanCodeReader` / `IPlanRateLimitReader` + `PlanRateLimitSnapshot` — puertos puros que desacoplan el resolver de cómo cada servicio obtiene el plan del tenant y el multiplicador de `PlanRateLimits`. Deliberadamente sin implementación concreta contra Subscription real en esta fase — ver razón abajo.
- `IRateLimitQuotaResolver` + `RateLimitQuotaResolver` — algoritmo puro (sin I/O), vive en BuildingBlocks core. A-E/P/Q nunca escalan (invariante §3.6); F-O consultan los dos puertos y aplican multiplicador u override; cualquier lectura ausente (`null`) cae a la cuota base con `IsFallback = true`.
- `CachedTenantPlanCodeReader` (BuildingBlocks.Infrastructure) — decorador de caché TTL 5 min sobre `ITenantPlanCodeReader` usando `ICacheService`, con `InvalidateAsync(tenantId)` para que el consumer de evento de cada servicio lo invoque.

**Por qué no hay implementación concreta de `IPlanRateLimitReader` todavía**: `PlanRateLimits` vive en la base de datos de Subscription, y ningún servicio de este monorepo consulta la BD de otro directamente. Resolver "cómo llega ese dato a los demás servicios" (cliente M2M+caché al estilo `IPlanCatalogClient`, o una proyección local por evento) es exactamente el trabajo que el plan asigna a Fase 6 ("verificar que el evento propaga a los 13 servicios que ya tienen caché de proyección"). Construir ese mecanismo ahora, sin que ningún servicio real lo consuma todavía (Fase 3 solo pilotea el middleware en Customer, con el flag `enforceTierQuotas` en OFF), habría sido diseñar a ciegas el contrato HTTP/evento antes de tener un consumidor real que lo valide.

## 3. Correcciones de nomenclatura respecto al doc de diseño original

Dos discrepancias se descubrieron al ejecutar Fase 1 y se resuelven aquí, formalmente, en vez de silenciosamente:

### 3.1 Tiers de plan: "Free/Standard/Plus/Enterprise/Custom" → catálogo real de 3 planes

`Plan_Implementacion_Fases.md` §5 fue escrito citando una taxonomía de 5 tiers de marketing genérica. El catálogo real de Subscription (`TaxVision.Subscription.Domain.Plans.PlanCatalog`) solo tiene **3 planes comerciales**: `starter`, `pro`, `enterprise` (mapeados a `PlanTier.Standard/Pro/Enterprise` respectivamente). `PlanTier.Trial` existe en el enum pero no está en uso hoy (ningún plan lo tiene asignado, no hay flujo de trial).

**Decisión**: `PlanRateLimits` se siembra contra los 3 `PlanCode` reales, con la siguiente correspondencia con §5 del plan:

| §5 (doc de diseño) | Multiplicador | PlanCode real | Excepciones aplicadas |
|---|---|---|---|
| Standard | 1.0× | `starter` | **F (lectura) y G (escritura) a 2.0×** (ajuste 2026-08-29) |
| Plus | 3.0× | `pro` | I (bulk) y J (rendering) a 5.0×; **F y G a 5.0×** (ajuste 2026-08-29) |
| Enterprise | 10.0× | `enterprise` | K (envío) a 20.0×, H (búsqueda) a 15.0× |
| Free/Trial (0.3×) | — | *no existe hoy* | diferido — no se siembra una fila fantasma para un plan que no es seleccionable en producción |
| Enterprise Custom | negociado | — | *no existe hoy* — cuando exista un `PlanCode` negociado, se le agrega su propia fila usando `HardOverridePerMinute` (campo ya presente en el schema, sin usar en Fase 1) |

M (financiero admin) y N (reveal sensible) llevan `MultiplierOverride = 1.0` explícito en los 3 planes — nunca escalan, consistente con la regla operativa de §5, pero sembrado como fila explícita en vez de omitida (invariante §3.2 "toda partición debe ser explícita").

### 3.2 Ruta de BuildingBlocks

El plan citaba `src/BuildingBlocks/BuildingBlocks/RateLimiting/` (con `BuildingBlocks` duplicado). La estructura real del monorepo es `src/BuildingBlocks/<Feature>/*.cs` con namespace raíz `BuildingBlocks` — ahí es donde vive el código de Fase 1.

### 3.3 Evento de invalidación de caché: `SubscriptionPlanChangedIntegrationEvent` no existe

`Plan_Implementacion_Fases.md` §8 (Fase 2) nombra un `SubscriptionPlanChangedIntegrationEvent` para invalidar la caché de plan por tenant. Ese evento no existe — los eventos puntuales de cambio de plan (`SubscriptionActivated`/`PlanChanged`/`Suspended`) fueron retirados en un refactor anterior a favor de un único evento catch-all: **`TenantEntitlementsChangedIntegrationEvent`** (`src/BuildingBlocks/Messaging/SubscriptionIntegrationEvents/TenantEntitlementsChangedIntegrationEvent.cs`), publicado desde `RecalculateEntitlementsHandler` (Subscription) en cada cambio que afecta entitlements — activación, cambio de plan, suspensión/reactivación, compra de seats. Trae `PlanCode`, `RevisionNumber` (versión monotónica, útil para descartar entregas fuera de orden) y `TenantId`/`CorrelationId` heredados de `IntegrationEvent`.

**Decisión**: cuando Fase 6 conecte el resolver a un servicio real, el consumer que invalida `CachedTenantPlanCodeReader` (§2) debe suscribirse a `TenantEntitlementsChangedIntegrationEvent`, no al evento inexistente del doc original.

## 2.2 Alcance de Fase 3 (middleware unificado + piloto Customer)

- `RateLimitPolicyDefinition.OverlayQuotaPerMinute` (int?) — extensión sobre Fase 1: la cuota propia de la Capa 2 (overlay per-tenant) para las categorías del Bloque II (F/G/H/I/L) vive como segundo campo nullable en la misma política, no como "política hermana" (idea original del doc de diseño, descartada por más simple de operar — un solo registro por endpoint sigue siendo la unidad auditable de §6.1). Categoría K no usa este campo — su overlay ya lo maneja `IProviderRateLimiter` (F26), un mecanismo previo y distinto.
- `EffectiveQuota.OverlayPermitCount` — mismo criterio, en el record de salida del resolver.
- `ITieredRateLimitEvaluator` / `TieredRateLimitEvaluator` (`BuildingBlocks.Infrastructure.RateLimiting`) — evaluador de referencia: incrementa `IRateCounter` (F26) para la capa primaria siempre, y para la capa overlay solo si la política define `OverlayQuotaPerMinute`; evalúa primaria antes que overlay (primera capa que dispara, invariante §0); fail-open si `IRateCounter` lanza (Redis caído nunca bloquea tráfico). Soporta hoy las dos particiones que el piloto necesita: `Tenant|User` combinado y `User` solo — otra combinación lanza `NotSupportedException` en vez de construir una clave incorrecta en silencio.
- `RateLimitAttribute` / `RateLimitExemptAttribute` (`BuildingBlocks.Web.RateLimiting`) — filtro de acción (`IAsyncActionFilter`, mismo patrón que `HasPermissionAttribute`) que resuelve tenant/usuario del JWT, evalúa la política nombrada y responde `429` con los headers y el body de §6.3 si dispara. `RateLimitExemptAttribute` es un marcador para Fase 9 (NetArchTest que exige uno de los dos atributos en todo endpoint público) — no está conectado a ninguna lógica todavía.
- `TieredRateLimitingRegistration.AddTieredRateLimiting()` — registra registry/resolver/evaluador. Nombre deliberadamente distinto de `RateLimitingRegistration` (mismo folder físico, namespace `BuildingBlocks.RateLimiting`) — esa es `AddTaxVisionGatewayRateLimiting()`, el rate limiter previo del Gateway (Capa 1/infra, ASP.NET Core `AddRateLimiter` nativo), sin relación con este mecanismo de Fase 3. Las dos clases coexisten sin tocarse.
- **Piloto en Customer**: `POST /customers` (`customer.g.create`), `GET /customers/{id}` (`customer.f.get`, política nueva — no existía en el catálogo de Fase 1, que solo tenía `customer.f.list`) y `GET /customers/{id}/fiscal-profile/tax-identifier` (`customer.n.fiscal_reveal`, reemplaza el `FixedWindowRateLimiter` local de ASP.NET Core que existía ahí desde antes de este plan). Customer registra `IRateCounter`/`RedisRateCounter` con su propio `IConnectionMultiplexer` (F26) y llama `AddTieredRateLimiting()` en `Program.cs`.
- **Verificado con `WebApplicationFactory<Program>` real** (SQL Server + Redis + RabbitMQ locales, sin mocks de infraestructura) — no un test sintético: 61 `POST /customers` reales (60 filas creadas y luego borradas), 301 `GET /customers/{id}` y 6 `GET .../fiscal-profile/tax-identifier` contra el host real, confirmando el 429 con headers/body exactos de §6.3 en el request que excede cada cupo (`customer.g.create` a 60, `customer.f.get` a 300, `customer.n.fiscal_reveal` a 5).

**Capa 4 (cap por endpoint, categorías H/I) sigue sin implementar** — ninguno de los 3 endpoints del piloto (categorías F/G/N) la necesita, y `RateLimitPolicyDefinition` no tiene ese campo todavía para no inventar un número sin auditar un endpoint real que lo use. Se agrega cuando Fase 4 migre la primera categoría H/I.

### 3.4 `AllowedActorTypes: Any` no bypasea usuarios humanos sin tenant en Fase 3

Fail-open explícito de `RateLimitAttribute`: si el `ClaimsPrincipal` no trae `tenant_id`/`sub` válidos (p. ej. un endpoint público sin `[Authorize]` que igual lleve `[RateLimit]` por error de wiring), el filtro deja pasar el request sin contar — no hay garantía de que `[Authorize]` corrió antes en el pipeline para todo actor. Esto es una limitación conocida, no un bug: el catálogo de políticas de Fase 1-3 solo cubre endpoints autenticados (Bloque II-IV); Capa 1 (Gateway, Bloque I/A no autenticado) ya cubre esos casos por IP.

## 2.3 Alcance de Fase 4 (migración de los 16 servicios) — CERRADA 2026-08-01

Las 16 sub-fases (4.1 Customer cierre de loose ends, 4.2 Tenant, 4.3 Notification, 4.4 Postmaster,
4.5 Scribe, 4.6 CloudStorage, 4.7 Signature, 4.8 Connectors, 4.9 Correspondence, 4.10 Subscription,
4.11 Billing, 4.12 Auth, 4.13 PaymentApp, 4.14 PaymentClient, 4.15 Growth, 4.16 Documents) migraron
todo endpoint público de cada servicio a `[RateLimit]`/`[RateLimitExempt]`, eliminaron cualquier
`AddRateLimiter native` residual (donde el actor protegido calificaba para el sistema tiered), y
cerraron con build+test completo del monorepo en verde. Detalle servicio-por-servicio —
decisiones de categorización, exenciones M2M, bugs encontrados — en
`documents/RateLimit/Plan_Implementacion_Fases.md` (nota de cierre bajo la tabla de Fase 4), no
duplicado aquí. Dos hallazgos afectan el diseño de este ADR y sí se registran acá:

- **`RateLimitAttribute` tenía un bug de negociación de contenido** (encontrado en 4.14): devolver
  un `ObjectResult` JSON en la respuesta 429 rompía en cualquier acción con `[Produces("text/csv")]`
  u otro content-type no-JSON declarado, devolviendo 406 en vez de 429. Corregido escribiendo el
  JSON directo al body (mismo patrón que `ExceptionHandlingMiddleware`) — afecta a los 17 servicios
  que usan `[RateLimit]`, no solo al que lo encontró.
- **Categoría J (Rendering/cómputo caro) cubre M2M, no solo triggers humanos** — confirmado en 4.16
  (Documents): un JWT de servicio (`GenerateScopedServiceToken`) siempre trae `sub` (derivado
  determinísticamente de `client_id`, no un usuario real) y `tenant_id` (real o el sentinel
  `PlatformTenant.Id` pre-tenant), así que `[RateLimit]` NO fail-open para M2M por defecto — el
  fail-open de 3.4 solo aplica cuando el handler nunca resuelve esos claims en absoluto (caso
  Growth, Fase 4.15). Esto significa que las 17 categorías ya cubren tráfico M2M sin necesitar una
  categoría "servicios internos" dedicada, como especulaba la nota original de la tabla de Fase 4
  para Documents — se prefirió reutilizar J en vez de ampliar el enum congelado en §2.

## 4. Consecuencias

- Cualquier fase posterior que necesite resolver una cuota efectiva debe consultar `PlanRateLimits` por `(PlanCode, RateLimitCategory)`, no por un tier inventado.
- Si el negocio decide introducir un tier gratuito o un plan Enterprise Custom, ese trabajo agrega un `PlanCode` nuevo al catálogo de Subscription primero — el rate limiting lo sigue automáticamente vía el mismo mecanismo, sin cambios de schema.
- El catálogo de políticas seguirá creciendo en Fase 5+; el criterio de "una política por endpoint real, nombrada tras auditar ese endpoint" se mantiene como práctica estándar (ver `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md`).
