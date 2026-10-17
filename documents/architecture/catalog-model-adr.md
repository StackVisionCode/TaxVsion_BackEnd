# ADR — Modelo de catálogo (planes, módulos, add-ons, permisos)

**Fecha**: 2026-10-15
**Estado**: Firmado; actualizado por CAT-F8
**Autor**: Equipo backend TaxVsion
**Decisores**: devcacg (owner), Senior (revisor)

## Contexto

El sistema tiene tres planes (Starter / Pro / Enterprise) con bundle de módulos, un catálogo de add-ons 1:1 por módulo a $29/mo, seats con pricing propio, y wallet top-up como cargo transaccional. El catálogo (planes, módulos, add-ons) vive hoy en clases estáticas de C# (`PlanCatalog`, `PlanModuleCatalog`, `ModuleAddOnCatalog`); cambiar precios o módulos requiere PR + migración. El dominio soporta componibilidad (merge strategies Sum/Max/Or/Replace, versioning), pero el input sigue siendo código.

El Senior propuso "todo debe ser add-ons" para que ventas arme paquetes a medida. Tras análisis, la rigidez vive en el seed, no en el modelo. Este ADR fija el modelo mental para 10 sprints de rediseño.

## Decisión

### Primitivas canónicas (nombres finales)

| Capa | Primitiva | Vive en | Componible? |
|---|---|---|---|
| Comercial | `Plan` | `SubscriptionPlans` + `SubscriptionPlanVersions` | ✅ desde admin API |
| Modular | `Module` | tabla **nueva** `Modules` (fuente única) | ✅ desde admin API |
| Monetario | `Module` vendible / `AddOn.Quota` | `Modules` + shadows internos / `AddOnDefinitions` | ✅ desde admin API |
| RBAC | `Permission` + `Role` | `PermissionCatalog` + `Roles` | ⛔ sólo por código |
| Átomo | `Seat` | `SubscriptionSeat` + `SeatPricing` | ⛔ se queda |
| Átomo | `WalletTopUp` | `WalletTopUp` | ⛔ se queda |

### Reglas duras (firmadas, no se cambian sin ADR nuevo)

1. Permisos **no** se venden sueltos. Grants viajan por rol. Deny-only se queda.
2. Add-ons tienen exactamente dos formas: un `Module` Commercial vendible o un `Quota` numérico. Nada más.
3. Catálogo (planes, módulos, add-ons) vive en **tablas administrables**. El código deja de sembrar.
4. Seats y WalletTopUp mantienen su primitiva propia; **no** se unifican con AddOns.
5. Hay **un solo registry de módulos** en todo el sistema (`Modules` tabla + endpoint M2M consumido por BuildingBlocks).
6. `Starter / Pro / Enterprise` es el naming canónico de los tiers; `Standard` queda retirado al Fase 5.

## Alternativas descartadas

- **"Todo es add-on" radical**: destripar el modelo de permisos+roles para componer permisos sueltos por usuario. Rechazado por permission sprawl y por decisión previa del equipo (`User_Permission_Overrides_RBAC_Phased_Plan.md`, L67).
- **Seats como `AddOnDefinition`**: pierde la semántica de pool con asignación. Ganancia marginal, costo alto.
- **WalletTopUp como add-on recurrente**: es cargo transaccional one-off. Mezclar rompe las queries de "add-ons activos" y las políticas de renovación.
- **Pricing dinámico por tenant**: pricing opaco mata ventas. Precio depende sólo de `(plan, add-on, cycle, qty)`.

## Hoja de ruta (10 sprints, ~2.5 meses con 1 dev)

| Fase | Sprints | Entregable comercial |
|---|---|---|
| 0 — Higiene | 1 | Deja de cobrar por nada |
| 1 — Un registry de módulos | 2 | Deuda técnica cerrada |
| 2 — Catálogo planes administrable | 3 | Producto crea planes sin PR |
| 3 — AddOn.Quota + Storage packs | 3 | Storage packs vendibles ($3/$6/$10 por 2/5/10 GB) |
| 5 — Cierre deudas + fitness final | 1 | PlanTier unificado |

Fase 4 (admin UI de "armador") se difiere — no hay admin UI frontend; se expone API lista para cuando se construya.

## Fase 0 — aplicada (este ADR)

- 30 permisos `IsReserved: true` eliminados de `PermissionCatalog.cs` (ninguno estaba asignado a algún rol — pre-check SQL verificado).
- Migration `20261015221226_RemoveReservedPermissions` con 30 `DeleteData` + 30 `InsertData` en Down.
- 5 add-ons `NotBuilt` (`reports/marketing/builder/irs/miles`) **ya** son deprecados al arranque por `SubscriptionCatalogReconciler` — no se tocó código, sólo se protegió con fitness.
- Fitness tests: `ReservedPermissionsFitnessTests` (Auth) + `ModuleAddOnCatalogAvailabilityFitnessTests` (Subscription) impiden regresión.

## Fase 3 — pricing storage (acordado)

| SKU | Code | Capacidad | $/mes | $/año | $/GB/mo |
|---|---|---|---|---|---|
| Small | `addon.storage.small` | +2 GB | $3 | $30 | $1.50 |
| Medium | `addon.storage.medium` | +5 GB | $6 | $60 | $1.20 |
| Large | `addon.storage.large` | +10 GB | $10 | $100 | $1.00 |

Mutuamente exclusivos. Entitlement `storage.max_bytes` con `MergeStrategy.Sum`. CloudStorage ya consume la cuota — zero cambio downstream.

## Guardrails de ejecución

- Cambios en el catálogo que dispare recálculo masivo de entitlements van detrás de feature flag `Catalog:AdminPublish:Enabled` para evitar fan-out de 24+ consumers de `TenantEntitlementsChangedIntegrationEvent`.
- `SubscriptionPlans.Tier` string no se renombra hasta Fase 5 (migración de datos coordinada).
- `TenantAdminCommunicationExtras` hack queda hasta Fase 5 — se reemplaza por regla declarativa.
- Dos vocabularios de módulo (`PermissionDefinition.Module` UI vs `PermissionModuleMap` gate) se unifican en Fase 1.

## Consecuencias

Positivas:
- Producto arma planes nuevos sin tocar código al llegar a Fase 2.
- Storage se vende con pricing escalonado al llegar a Fase 3.
- Dos vocabularios de módulo convergen en uno al llegar a Fase 1.
- Deuda IsReserved (30 permisos) cerrada en Fase 0.

Negativas:
- Agregar un nuevo tipo de AddOn (ni Module ni Quota) requiere ADR nuevo.
- Un cambio de módulos en un plan existente sigue disparando fan-out — se mitiga con flag + rate limit.

## Enmienda CAT-F8

- `Modules` es el único catálogo de módulos.
- Los módulos Core se agregan implícitamente a cada versión de plan.
- Los módulos Commercial se seleccionan de forma explícita.
- Los add-ons de módulo disponibles se calculan desde `Modules`; no se leen desde `AddOnDefinitions`.
- `module-addon-*` son handles internos para conservar el flujo de compra existente.
- Los catálogos estáticos y la tabla duplicada de Auth fueron eliminados.
- `planner` agrupa calendar, reminders, tasks y notes en el catálogo actual.

## Referencias

- `User_Permission_Overrides_RBAC_Phased_Plan.md` — deny-only firmado.
- `Seats_Billing_Phased_Plan.md` — seats fuera de add-ons por ADR-2 YAGNI.
- `Authorization_Frontend_RBAC_Entitlements_Audit_And_Implementation_Plan.md` — dos vocabularios de módulo documentados como deuda.
- `Guia_Cablear_Modulo_Plan_Microservicio.md` — guía existente (actualizada al terminar Fase 5).
