# Subscription

Subscription owns plans, plan versions, modules, add-ons, seats and tenant entitlement snapshots.

## Module catalog

`Modules` is the only module catalog.

- `Core`: included automatically in every plan.
- `Commercial`: selected explicitly when a plan is configured.
- A commercial module is sellable as an add-on only when both add-on prices are present.
- `module-addon-<module-code>` rows are internal purchase handles. They are not a public catalog.

Quota add-ons remain in `AddOnDefinitions` with category `quota`.

## API contracts

- `GET /addons/available`: tenant-scoped union of sellable modules not included in the current plan and published quota add-ons.
- `GET /admin/subscription/modules`: PlatformAdmin view of the canonical module catalog.
- `PUT /admin/subscription/plans/{planId}/modules`: accepts commercial module codes only. Core modules are added automatically.
- Draft replacement accepts commercial module codes only and applies the same core-module rule.

The gateway exposes tenant add-ons under `/subscription/addons/*`.

## Consistency

Changing plan modules publishes `RecalculateEntitlementsForPlanCommand` and `CatalogChangedIntegrationEvent`.
The startup reconciler repairs plan core modules and internal module add-on shadows idempotently.

The old `GET /addons` catalog and the static `PlanModuleCatalog` and `ModuleAddOnCatalog` no longer exist.
