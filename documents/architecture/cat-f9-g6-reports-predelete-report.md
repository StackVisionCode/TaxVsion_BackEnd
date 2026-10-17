# CAT-F9 G6 - Reports pre-delete inventory

Date: 2026-10-16

## Decision

Remove `reports` from the executable module catalog. The module has no implemented endpoint or UI and must not be offered, gated, or seeded as a commercial capability.

## Database inventory

- Auth permissions with `GatedModule = reports`: 0.
- Auth permission codes under `reports.*`: 0.
- Subscription add-on definitions for reports: 0.
- Entitlement snapshots containing `module.reports`: 0.
- Purchases for a reports module add-on: 0.
- Current module row: one catalog seed row, safe to delete.
- Historical plan features named `module.reports`: 4, all in `Superseded` plan versions.

The four superseded features remain unchanged as immutable billing history. They are not active tenant promises and do not make the module available at runtime.

## Code inventory

The executable references were limited to the Subscription seed, permission maps, enforcement tests, and frontend display dictionaries. Historical migrations and architecture reports remain unchanged so the repository continues to explain how the retired value entered the catalog.

## Safety conclusion

Deleting the current catalog row does not revoke an active permission, purchase, add-on, or entitlement snapshot. A rollback can restore the catalog row without modifying historical plan versions.
