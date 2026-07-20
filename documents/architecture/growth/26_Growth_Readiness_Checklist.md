# Growth — Checklist de readiness

Estado: **NOT_READY**

## Diseño

- [x] Growth deployment y dos bounded contexts.
- [x] Ownership y prohibiciones.
- [x] Quote/reserve/commit/cancel/reconcile.
- [x] Datos, constraints, concurrency e idempotencia.
- [x] Seguridad, observabilidad y pruebas.
- [ ] GDR-007 autoridad de precio tenant-cliente.
- [ ] GDR-008 M2M Auth.
- [ ] GDR-009 compensación comercial.
- [ ] GDR-010 programa T2T.
- [ ] GDR-011 PII taxpayer.

## Implementación

- [ ] Payment prerequisite issues.
- [ ] Contracts versionados.
- [ ] Projects/DB/migrations.
- [ ] Permissions Auth.
- [ ] Tests y security review.
- [ ] Legacy inventory/reconciliation.

## Gates de estado

| Estado | Requisito |
|---|---|
| READY_FOR_TECHNICAL_SCAFFOLDING | cero DESIGN_BLOCKER |
| READY_FOR_DOMAIN_IMPLEMENTATION | scaffolding/constraints/architecture tests |
| READY_FOR_INTEGRATION | Payment/Subscription contracts y M2M |
| READY_FOR_PRODUCTION_REVIEW | E2E/load/security/migration/reconciliation |

