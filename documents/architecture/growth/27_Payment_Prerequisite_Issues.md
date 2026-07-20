# Issues prerequisite en Payment

No se modifica código. Todos son defectos o capacidades requeridas, no implementación Growth.

| ID | Severidad | Issue/evidencia | Aceptación |
|---|---|---|---|
| PAY-GR-001 | BLOCKER | `ApplyPayload` ignora `Result` de `MarkSucceeded/Failed/Cancel/ChargedBack/RefundPartial` en `ProcessTenantWebhookHandler.cs:202` | transición fallida no marca Applied; resultado clasificado |
| PAY-GR-002 | HIGH | `webhookEvent.MarkApplied` ocurre tras ApplyPayload sin comprobar cambio, mismo handler `:154-157` | statuses Applied/Duplicate/Stale/Rejected explícitos |
| PAY-GR-003 | HIGH | dedupe check-then-insert `:84-105` | unique conflict tratado como replay exitoso |
| PAY-GR-004 | HIGH | DbContexts no normalizan `DbUpdateConcurrencyException` en pagos | retry acotado/409 consistente |
| PAY-GR-005 | BLOCKER | provider events sin ordering semántico/aggregate version | stale/out-of-order no revierte terminal state |
| PAY-GR-006 | BLOCKER | comandos Payment carecen `PromotionSnapshotId` | persistido y retornado |
| PAY-GR-007 | BLOCKER | comandos Payment carecen `ReservationId` | unique source/payment y trazabilidad |
| PAY-GR-008 | BLOCKER | no verifica gross/discount/net/hash contra quote | rechazo antes de provider call |
| PAY-GR-009 | HIGH | refund contract Growth requiere acumulado y delta | evento versionado incluye ambos+currency |
| PAY-GR-010 | HIGH | chargeback solo mapea created→ChargedBack | contrato dispute opened/won/lost/version |
| PAY-GR-011 | HIGH | PaymentApp handler equivalente debe auditarse | paridad de fixes en ambos servicios |

`src/Services/PaymentClient/TaxVision.PaymentClient.Application/TenantPayments/Commands/ProcessTenantWebhook/ProcessTenantWebhookHandler.cs:84` y `:154` son evidencia **VERIFIED**. Los campos Growth son **NOT_IMPLEMENTED**, ausencia normal pero prerequisite bloqueante.
