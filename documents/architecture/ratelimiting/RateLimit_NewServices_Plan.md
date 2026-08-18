# Plan — Rate limiting para los servicios nuevos (Catalog, Inventory, SMS)

## Contexto

El rate-limiting del monorepo tiene **dos capas** y hoy los tres microservicios nuevos
(**Catalog**, **Inventory**, **SMS**) **no tienen ninguna de las dos**: sus `Program.cs` no
registran `IRateCounter` ni `AddTieredRateLimiting()`, sus controllers no llevan `[RateLimit]`, no
existe `RateLimitPolicyCatalog.<Servicio>.cs`, y no hay `TenantPlanCodeProjection` local.

| Capa | Pieza | Qué hace | Estado en servicios nuevos |
|---|---|---|---|
| Base | `RateLimitPolicyCatalog.<Svc>.cs` + `[RateLimit]` + `IRateCounter` (Redis) | Cuota fija por tenant/usuario y por endpoint. | ❌ ausente |
| Tier-aware | `TenantPlanCodeProjection` + `TenantPlanCodeProjectionConsumer` + readers | Escala la cuota según el **plan** del tenant (flag `RateLimit:EnforceTierQuotas`). | ❌ ausente |

La proyección de plan-code es una **copia local por servicio** del *plan code* del tenant,
alimentada por `TenantEntitlementsChangedIntegrationEvent` (publicado por Subscription). Sin ella,
el limitador hace *fail-open* a la cuota base.

**Alcance:** solo los 3 servicios nuevos + archivos aditivos en `BuildingBlocks/RateLimiting` + 1
migración por servicio (Fase 2). Se **consume** un evento que Subscription ya publica; no se
modifica Subscription, Auth, ni ningún otro servicio.

---

## Fase 1 — Capa base (protección inmediata, sin migraciones)

**Objetivo:** que cada endpoint de Catalog/Inventory/SMS tenga una cuota real por tenant/usuario.
Fail-open al tier (la escala por plan llega en Fase 2). Cero cambios de esquema.

### 1.1 Catálogos de políticas (BuildingBlocks)
`src/BuildingBlocks/RateLimiting/Catalog/RateLimitPolicyCatalog.{Catalog,Inventory,Sms}.cs`
(partial classes, patrón de `RateLimitPolicyCatalog.Notes.cs`). Partición por `Tenant | User`,
overlay para plan alto ya declarado en cada policy:

| Servicio | Policy | Endpoints | Cat. | Cuota/60s | overlay |
|---|---|---|---|---|---|
| Catalog | `CatalogRead` | GET items/categories | F | 300 | 3000 |
| Catalog | `CatalogWrite` | POST/PUT/DELETE items/categories | G | 60 | 600 |
| Inventory | `InventoryRead` | GET stock/movements/suppliers | F | 300 | 3000 |
| Inventory | `InventoryWrite` | suppliers / item-suppliers / thresholds | G | 60 | 600 |
| Inventory | `InventoryAdjust` | POST stock/{id}/adjust | G | 120 | 1200 |
| SMS | `SmsSend` | POST /sms/messages (batch) | H | 30 | 300 |

> Webhooks SMS (`/sms/webhooks/*`) son anónimos y ya pasan por el gate por-IP del Gateway
> (`AddTaxVisionGatewayRateLimiting`); no llevan `[RateLimit]` tiered (no hay tenant/user en el JWT).

### 1.2 Atributos en controllers
`[RateLimit(nameof(RateLimitPolicyCatalog.Xxx))]` en cada acción de `ItemsController`,
`CategoriesController`, `StockController`, `SuppliersController` y el endpoint de envío de SMS.

### 1.3 Wiring en cada `Program.cs`
- `IConnectionMultiplexer` (Redis) + `IRateCounter → RedisRateCounter`.
- `builder.Services.AddTieredRateLimiting();`
- `ConnectionStrings:Redis` en `appsettings`/`.env`/compose (Redis ya corre en el stack).

### 1.4 Verificación
- `dotnet build TaxVision.slnx` limpio.
- Ráfaga sobre `GET /catalog/items` y `POST /sms/messages` → **429** al superar la cuota base.

---

## Fase 2 — Capa tier-aware (proyección de plan por servicio)

**Objetivo:** que la cuota escale según el plan del tenant, detrás del flag
`RateLimit:EnforceTierQuotas` (OFF por default, igual que el resto del monorepo).

Por cada servicio (Catalog, Inventory, SMS):

1. **Domain:** `TenantPlanCodeProjection` (implementa `ITenantPlanCodeProjection`) + factory `Create`.
2. **Application:** `ITenantPlanCodeProjectionRepository` (abstracción local) +
   `TenantPlanCodeProjectionConsumer` (wrapper de 1 línea que delega en
   `BuildingBlocks.Messaging.RateLimiting.TenantPlanCodeProjectionHandler`, patrón Notes).
3. **Infrastructure:** repositorio + EF config + **migración** `AddTenantPlanCodeProjection`
   (tabla nueva en la DB del servicio).
4. **Wolverine:** `options.Discovery.IncludeType(typeof(TenantPlanCodeProjectionConsumer))` y bind
   del `TenantEntitlementsChangedIntegrationEvent` en la cola `<svc>-events`.
5. **Program.cs (bajo el flag):** registrar `ScopedTenantPlanCodeReader` (`ITenantPlanCodeReader`)
   y `ScopedPlanRateLimitReader` (`IPlanRateLimitReader`).

### Verificación
- `dotnet build` limpio + migraciones aplicadas (`apply-migrations.sh`).
- Publicar/replayear `TenantEntitlementsChangedIntegrationEvent` → fila en la proyección de cada
  servicio (idempotente ante replays).

---

## Fase 3 — Verificación E2E + documentación

**Objetivo:** probar en vivo por el Gateway y dejar registro.

1. **E2E cuota base:** ráfaga por `:5047` → 429 con headers de rate-limit; confirmar partición por
   tenant (dos tenants no se pisan).
2. **E2E tier:** flag ON + tenant con plan alto → cuota escalada (overlay) vs. plan base.
3. **Docker/Gateway:** confirmar criticidad de las rutas nuevas y `ConnectionStrings:Redis` en
   `catalog-api`/`inventory-api`/`sms-api`.
4. **Docs/memoria:** marcar fases DONE aquí; actualizar la nota de rate-limit en
   `postman/README.md`; registrar en memoria del proyecto.

---

## Reglas que no se rompen

```text
✅ Solo se tocan Catalog, Inventory, SMS + archivos aditivos en BuildingBlocks/RateLimiting.
✅ Se consume TenantEntitlementsChangedIntegrationEvent; NO se modifica Subscription.
✅ Tier-aware detrás de flag OFF por default (fail-open a cuota base).
✅ Webhooks SMS siguen anónimos, protegidos por el gate por-IP del Gateway.
❌ No se toca Auth, CloudStorage, Communication ni otros servicios.
❌ Sin secretos hardcoded (Redis/creds por .env).
```

## Estado

- [x] **Fase 1 — Capa base** — `RateLimitPolicyCatalog.{Catalog,Inventory,Sms}.cs` + `[RateLimit]`
  en los 5 controllers (Items/Categories/Stock/Suppliers+ItemSuppliers/Messages) + Redis
  (`IConnectionMultiplexer`) + `AddTieredRateLimiting()` en los 3 `Program.cs`. Los 3 `.Api`
  compilan limpio (0 errores). Falta verificación en vivo (Fase 3).
- [x] **Fase 2 — Capa tier-aware** — por servicio (Catalog/Inventory/SMS): `TenantPlanCodeProjection`
  (Domain) + `ITenantPlanCodeProjectionRepository` + `TenantPlanCodeProjectionConsumer` (App) + repo EF
  + `TenantPlanCodeProjectionConfiguration` + `EfTenantPlanCodeReader` + `CachedTenantPlanCodeReader`
  + `TenantPlanCodeCacheInvalidator` + `ServiceTokenAcquirer` (Infra) + `AddRateLimitTierQuotas` en el DI
  + `IncludeType` del consumer en Wolverine + bloque del flag en `Program.cs` + **1 migración por servicio**
  (`AddTenantPlanCodeProjection`, solo crea la tabla + índice único). Los 3 `.Api` compilan verde. El
  consumer mantiene la proyección al día SIEMPRE; la escala real por plan queda detrás del flag
  `RateLimit:EnforceTierQuotas` (OFF por default → fail-open a la cuota base). `apply-migrations.sh` ya
  aplica las 3 migraciones nuevas (bloques Catalog/Inventory/Sms existentes).

  > **Para ACTIVAR la escala por plan** (Fase 3, por entorno): el flag OFF hoy no basta — encenderlo
  > requiere además (a) credenciales M2M `ServiceAuthClient` (ClientId/Secret por servicio, registrados
  > en Auth) para que el acquirer obtenga token, y (b) la URL del catálogo `PlanRateLimits` de Subscription
  > (`SubscriptionClient:BaseUrl`). Sin ambos, aunque el flag esté ON, `IPlanRateLimitReader` no devuelve
  > multiplicador y el resolver hace fail-open a la cuota base. Estos tocan secretos/Auth y se deciden aparte.
- [x] **Fase 3 — Verificación E2E + docs** — rebuild de las 3 imágenes (`catalog/inventory/sms-api`) +
  recreate; arranque limpio con el `IConnectionMultiplexer` nuevo. **429 base verificado en vivo por el
  Gateway** (`:5047`): 65+ POST `/catalog/items` con token de usuario → 61 pasan (base 60 + 1 refill del
  token bucket), la #62 en adelante **429** con headers `X-RateLimit-Policy: catalog.g.write`,
  `X-RateLimit-Limit: 60`, `X-RateLimit-Layer: user`, `Retry-After: 60` y body
  `{"code":"RateLimit.Exceeded",...}`. Las 3 migraciones `AddTenantPlanCodeProjection` aplicadas a las
  DBs vivas (`TaxVision_Catalog/Inventory/Sms`) — la tabla de proyección existe, así que el consumer no
  falla ante `TenantEntitlementsChanged`. Nota: partición por usuario confirmada (`Layer: user`).

  > **NO verificado (requiere secretos/Auth, fuera de alcance de esta tanda):** la escala real por plan
  > con `RateLimit:EnforceTierQuotas=ON` — necesita provisionar credenciales M2M `ServiceAuthClient` por
  > servicio + `SubscriptionClient:BaseUrl`. El flag sigue OFF en el stack; hoy todos los tenants usan la
  > cuota base.
