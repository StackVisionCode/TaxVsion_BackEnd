# Growth — Estrategia de pruebas

| Nivel | Cobertura |
|---|---|
| Unit | reglas, stacking, money, clocks, states, fraud policy |
| Integration | EF/SQL Server real, constraints, RowVersion, transactions, Wolverine persistence |
| Contract | Growth↔Payment/Subscription/Catalog/Auth; version compatibility |
| Concurrency | último uso, same key/payment, commit/cancel/expiry, reward/refund races |
| Replay | misma key payload igual/distinto, duplicate EventId |
| Out-of-order | success/refund/chargeback invertidos y aggregate versions |
| E2E | once escenarios obligatorios |
| Migration | empty DB, upgrade, rollback plan, legacy backfill/reconciliation |
| Authorization | matriz positiva/negativa tenant/resource/M2M |
| Load | seasonal PaymentClient quote/reserve, hot campaign contention |
| Failure injection | RabbitMQ/SQL/Payment/Subscription timeout, crash after DB commit |

## E2E mínimos

1. Quote→Reserve→PaymentSuccess→Commit.
2. Quote→Reserve→PaymentFailed→Cancel.
3. Quote→Reserve→Timeout→Reconcile.
4. Commit→PartialRefund→Compensation.
5. Commit→FullRefund→Compensation.
6. Commit→Chargeback→Compensation.
7. attributed→payment→qualify→reward.
8. rewarded→refund→clawback.
9. rewarded→chargeback→fraud review.
10. trial grant→Subscription confirms.
11. duplicate feature grant→one effective result.

## Gates

- Domain: 100% de invariantes/state transitions con tests.
- Integration: todos los índices/constraints y carreras BLOCKER.
- Contract: consumer-driven compatibility.
- Security: cero bypass/IDOR.
- Load: capacidad objetivo y error budget aprobados antes de producción.

Los tests existentes de Payment/Subscription son evidencia **PARTIAL**; no existe suite Growth.

