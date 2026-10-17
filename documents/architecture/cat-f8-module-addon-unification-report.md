# CAT-F8 Module and Add-on Unification

**Date**: 2026-10-16  
**Status**: Implemented; automated gates passed

## Baseline

The local Subscription database did not contain a `Modules` table. The legacy source was split between static plan/module catalogs and module-shaped `AddOnDefinitions`.

No tenant had purchased a legacy module add-on, so local cleanup did not remove purchase history.

## Canonical catalog

| Code | Category | Monthly USD | Yearly USD | Sellable |
|---|---|---:|---:|---|
| customers | Core | - | - | No |
| signatures | Core | - | - | No |
| documents | Core | - | - | No |
| planner | Core | - | - | No |
| comms | Core | - | - | No |
| wallet | Core | - | - | No |
| email | Commercial | 29 | 290 | Yes |
| meetings | Commercial | 29 | 290 | Yes |
| campaigns | Commercial | 29 | 290 | Yes |
| reports | Commercial | - | - | No |

`planner` represents calendar, reminders, tasks and notes in the current architecture. This is why the resulting catalog has 10 modules instead of the 13 examples in the plan.

## Decisions

1. `Modules` is the source of truth for module metadata, category and add-on pricing.
2. Every plan persists all Core modules plus its selected Commercial modules.
3. Available module add-ons are calculated from `Modules` minus the active plan modules.
4. Quota add-ons remain in `AddOnDefinitions`.
5. Sellable modules use internal `module-addon-*` shadows only to preserve the existing `TenantAddOn` aggregate and payment flow.
6. Auth's duplicate `PlanModules` table and static catalogs were removed.

## Data changes

- `20261016162904_CreateUnifiedModuleCatalog` creates and seeds `Modules`.
- `20261016163011_RemoveLegacyModuleAddOnCatalog` converts current sellable rows to internal shadows and removes other legacy module rows.
- `20261016164318_RemoveDuplicatePlanModulesCatalog` removes Auth's duplicate table.

All three migrations were applied to the local databases immediately after generation.

## Contracts

- Tenant catalog: `GET /subscription/addons/available` through the gateway.
- Admin catalog: `GET /admin/subscription/modules`.
- Plan module writes accept Commercial codes only; Core modules are implicit.
- The legacy mixed add-on catalog endpoint was removed.

The CRM and landing currently have no caller of the removed mixed catalog endpoint. No TypeScript contract change was required.

## Consistency and recovery

- Plan module changes enqueue entitlement recalculation for every tenant on that plan.
- Catalog changes keep the existing dedicated catalog event path.
- The startup reconciler repairs implicit Core modules and module purchase shadows idempotently.
- Purchase handlers validate every module shadow against the current `Modules` row before charging.

## Manual verification left to the owner

After starting the fleet from current Release binaries:

1. Recalculate all tenant entitlement snapshots from the admin endpoint.
2. Run the plan/add-on browser flow with one Commercial module included and another purchased.
3. Confirm the purchased module appears with `Source=AddOn` in the snapshot.
4. Check downstream projection revisions and service logs.

The fleet was not restarted as part of CAT-F8.

## Automated verification

- Subscription Release build: 0 warnings, 0 errors.
- Auth Release build: 0 warnings, 0 errors.
- Subscription tests: 405 passed.
- Auth tests: 1035 passed.
- Full filtered solution gate: passed in Release.
- Communication: typecheck passed, 502 tests passed.
- CommunicationTranscriptWorker: typecheck passed, 34 tests passed.
- Subscription and Auth EF models: no pending model changes.
- Postman collection: valid JSON.
- Runtime C# references to both legacy catalogs: zero.

The database has 17 entitlement snapshots. Three still miss at least one new Core entry and must be repaired through `RecalculateEntitlementsForAllPlansCommand` after the Release fleet starts.
