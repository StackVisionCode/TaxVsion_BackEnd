# Wallet/Ledger — ADRs

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- Deriva de `05_Master_ADR.md` (ADR-CAMP-000 §Decisión 3). IDs locales `ADR-WAL-xxx`.

---

## ADR-WAL-001 — Wallet como microservicio independiente (no módulo de Campaigns)

**Estado:** APPROVED (decisión de usuario, `05_Master_ADR.md:38` alternativa 3 rechazada).
**Contexto:** el saldo debe ser reutilizable por Campaigns, envíos SMS individuales y futuros consumidores.
**Decisión:** `TaxVision.Wallet` independiente con DB propia; consumidores lo llaman por M2M.
**Consecuencias:** (+) reutilización, aislamiento, escalado propio. (−) coordinación distribuida (saga reserve/consume/refund). BLOCKER-WAL-1: debe existir antes de que Campaigns ejecute.

## ADR-WAL-002 — Saldo real en USD minor units (long), nunca decimal/float ni TXC

**Estado:** APPROVED.
**Contexto:** el legado usaba `decimal` y una moneda ficticia TaxCoin (TXC) en ReferralService (`ReferralService/Domain/WalletTransaction.cs:12`).
**Decisión:** `long AmountCents` USD (ISO-4217), copia por-contexto del VO `Money` (`PaymentApp.Domain/ValueObjects/Money.cs:6-53`). El precio por canal y la moneda los define Wallet/Campaigns, no el frontend.
**Consecuencias:** sin errores de redondeo; integración directa con el charge de PaymentApp (que ya usa cents).

## ADR-WAL-003 — Ledger inmutable append-only como fuente de verdad; saldo cacheado con guardas

**Estado:** APPROVED (deriva de "movimientos INMUTABLES", `05_Master_ADR.md:29`).
**Contexto:** el legado mutaba `BalanceBefore/BalanceAfter` y marcaba `IsActive` (`WalletTransaction.cs:14,21`) — historia editable.
**Decisión:** `LedgerEntry` append-only (sin setters, UPDATE/DELETE revocados en BD); `PostedCents/HeldCents` como **caché derivada** actualizada en la misma TX y verificada por reconciliación.
**Alternativas:** event-sourcing puro (recálculo en cada lectura) — rechazado por costo; saldo mutable suelto — rechazado (anti-patrón legado).
**Consecuencias:** auditabilidad total; corrección solo por entries compensatorios (`Adjust`/`Refund`).

## ADR-WAL-004 — Protocolo reserve → consume/refund (no débito único al crear)

**Estado:** APPROVED.
**Contexto:** el legado debitaba una vez al crear la campaña (prepay, `CreateCampaignCommandHandler.cs:278-320`), sin poder ajustar por resultado real de entrega, y con TOCTOU no-atómico.
**Decisión:** máquina de estados de Reserva `Held→Consumed(parcial/total)/Released/Expired` (`State_Machines.md`); se reserva el estimado, se consume lo entregado, se devuelve el resto.
**Consecuencias:** el tenant solo paga por entregas reales; saga con compensación e idempotencia; requiere sweep de expiración para holds abandonados.

## ADR-WAL-005 — Idempotencia por business-inbox `ProcessedBusinessMessage` (copia por-contexto)

**Estado:** APPROVED.
**Contexto:** at-least-once (Wolverine), reintentos HTTP; el legado no tenía idempotencia (`WalletServiceClient.cs:101,198`).
**Decisión:** clave `(TenantId, Operation, ScopeId, IdempotencyKey)` con conflict-insert→replay, patrón `SqlBusinessIdempotencyExecutor.cs`. `Idempotency-Key` obligatorio en todo mutante.
**Consecuencias:** "un pago = una recarga", "un reserve = un hold"; sin doble-cobro en reintento.

## ADR-WAL-006 — Concurrencia por conditional update + RowVersion (optimistic, un ganador)

**Estado:** APPROVED.
**Contexto:** operaciones concurrentes sobre el mismo balance; sin saldo negativo jamás.
**Decisión:** `UPDATE ... WHERE RowVersion=@expected`; la perdedora recarga y **reevalúa** la guarda de negocio; `CHECK(Held<=Posted)` en BD como red.
**Consecuencias:** sin lost update ni sobre-reserva; sin locks pesimistas/deadlocks; reintento acotado con backoff.

## ADR-WAL-007 — Top-up acreditado solo por evento de PaymentApp (Recharge no es API pública)

**Estado:** APPROVED (deriva de `05_Master_ADR.md:32`, decisión 6).
**Contexto:** no crear saldo sin cobro real.
**Decisión:** único camino a `Recharge` = consumer de `WalletTopUpPaymentSucceededIntegrationEvent` (nuevo `SaaSPaymentType.WalletTopUp`). PaymentApp cobra (platform→tenant `SaaSPayment`), Wallet acredita al recibir "succeeded".
**Consecuencias:** BLOCKER-WAL-2 (falta el `SaaSPaymentType` y el evento upstream); `Adjust` admin queda como única otra vía (auditada).

## ADR-WAL-008 — Nunca persistir JWT de usuario; M2M client-credentials para todo

**Estado:** APPROVED (corrige anti-patrón §5 de `05_Master_ADR.md:48`).
**Contexto:** el legado guardaba `BackgroundAuthToken` (JWT) para refunds asíncronos (`CreateCampaignCommandHandler.cs:67`; `WalletServiceClient.cs:179-180`).
**Decisión:** Wallet usa exclusivamente M2M (audience `taxvision-wallet` + scopes por operación); refund/consume no requieren token de usuario.
**Consecuencias:** superficie de credenciales mínima; sin tokens de larga vida en BD.

## Resumen de blockers

| ID | Blocker | Bloquea |
|---|---|---|
| BLOCKER-WAL-1 | Wallet debe desplegarse antes que la ejecución de Campaigns | ejecución de campañas |
| BLOCKER-WAL-2 | Falta `SaaSPaymentType.WalletTopUp` + evento en PaymentApp | recarga de saldo vía pago |

## Tabla de evidencia consolidada

| ADR | Evidencia clave | Clasificación | Confianza |
|---|---|---|---|
| WAL-001 | `05_Master_ADR.md:29,38,57` | VERIFIED (decisión) | 95% |
| WAL-002 | `PaymentApp.Domain/ValueObjects/Money.cs:6-53`; `WalletTransaction.cs:12` | VERIFIED | 96% |
| WAL-003 | `WalletTransaction.cs:14,21` (legado mutable) | VERIFIED | 95% |
| WAL-004 | `CreateCampaignCommandHandler.cs:278-320` | VERIFIED | 95% |
| WAL-005 | `SqlBusinessIdempotencyExecutor.cs:93-216`; `ProcessedBusinessMessage.cs` | VERIFIED | 96% |
| WAL-006 | `ProcessedBusinessMessage.cs:23`; diseño | PARTIAL | 88% |
| WAL-007 | `SaaSPaymentType.cs:8-32`; `SubscriptionRenewalPaymentSucceededIntegrationEvent.cs:9-15` | VERIFIED | 94% |
| WAL-008 | `CreateCampaignCommandHandler.cs:67`; `WalletServiceClient.cs:179-180` | VERIFIED | 95% |
