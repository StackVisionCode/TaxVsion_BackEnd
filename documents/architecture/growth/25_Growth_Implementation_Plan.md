# Growth — Plan de implementación

No autoriza código en esta ejecución.

| Fase | Resultado | Gate |
|---|---|---|
| 0 Decisions | cerrar GDR-007..011 | cero DESIGN_BLOCKER |
| 1 Technical scaffolding | seis proyectos, solution, test project, DI, health, telemetry | architecture tests |
| 2 Persistence | DbContext, schemas, mappings, constraints, outbox/inbox | migration review |
| 3 Codes core | code/rules/scopes/quote | domain+integration tests |
| 4 Reservation protocol | reserve/commit/cancel/reconcile | concurrency/replay tests |
| 5 Payment prerequisites | refs/snapshot, webhook fixes, events | contract tests |
| 6 Compensation | refunds/chargebacks | failure injection |
| 7 Referrals T2T | program/attribution/qualification/reward | fraud/refund tests |
| 8 Grants | Subscription command/confirmation | idempotency contract |
| 9 Security/API | permissions, tenant filters, M2M, Gateway públicos | authorization review |
| 10 Migration/rollout | legacy inventory, shadow, cohort | reconciliation signoff |

Extracción futura se evalúa por equipos, carga, compliance, storage/retención y estabilidad de contratos; no por preferencia estética.

