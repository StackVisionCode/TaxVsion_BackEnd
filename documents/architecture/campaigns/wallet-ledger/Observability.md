# Wallet/Ledger — Observability

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Coherente con:** `00_Plan_And_Architecture.md §3`, `Data_Model.md`, `Domain_Design.md`.

> **Modelo prepago (ADR-WAL-005).** No hay métricas de holds/reservas/refunds. Las señales giran en torno a **cobros de run** (`CampaignCharge`), **recargas** (`TopUp`), **denegaciones por saldo** y la **reconciliación** de `PostedCents` (único saldo; sin `HeldCents`).

---

## 1. Principios

Dinero real → observabilidad **auditable y reconciliable**. Todo movimiento deja un `LedgerEntry` inmutable con snapshot de saldo (`BalanceAfterCents`, `Data_Model.md §1.2`): la traza financiera es la tabla misma, no solo logs. Nunca se loguean montos como `float` ni PII; `ReferenceId` (RunId/WalletTopUpId) y `TenantId` son GUIDs opacos aptos para correlación.

## 2. Correlación

- **CorrelationId** de Wolverine se propaga en todos los eventos; se casa con el `ReferenceId` (RunId/WalletTopUpId).
- Cada `LedgerEntry` guarda `Reason`, `OperationKey`, `ReferenceId` → traza extremo-a-extremo: top-up (`topup:{Id}`) → `Credit` → cobro de run (`run:{RunId}`) → `Debit`.

## 3. Métricas (OpenTelemetry / Prometheus)

| Métrica | Tipo | Etiquetas | Uso |
|---|---|---|---|
| `wallet_run_authorization_total` | counter | `tenant`,`outcome`(authorized/denied) | tasa de autorizaciones, ratio de denegación |
| `wallet_campaign_charge_cents_total` | counter | `tenant` | ingresos cobrados a campañas |
| `wallet_topup_cents_total` | counter | `tenant`,`outcome`(succeeded/failed) | recargas |
| `wallet_posted_cents` | gauge | `tenant` | saldo disponible (alertas de saldo bajo) |
| `wallet_denied_insufficient_total` | counter | `tenant` | runs rechazados por saldo (señal de negocio) |
| `wallet_idempotency_replay_total` | counter | `reason` | replays (reentregas at-least-once) |
| `wallet_concurrency_conflict_total` | counter | `operation` | contención de RowVersion |
| `wallet_op_duration_seconds` | histogram | `operation` | latencia p50/p95/p99 |
| `wallet_channel_price_change_total` | counter | `channel` | cambios de tarifa (auditoría de PlatformAdmin) |

## 4. Alertas

- **Reconciliación rota** (crítica): `PostedCents != Σ(asientos confirmados)` para algún wallet → posible corrupción/bug. Pagina.
- **Denegación alta** (`outcome=denied` sostenido): tenants sin saldo intentando campañas → señal de negocio (avisar recarga vía `BalanceLowWarningIntegrationEvent`; UX de intercepción `00_Plan §8-bis`).
- **Top-up fallidos** (`wallet_topup_cents_total{outcome=failed}` alto): problema con tarjetas guardadas / Stripe off-session.
- **Conflictos de concurrencia** sobre umbral: hotspot en un wallet (muchos runs del mismo tenant a la vez).

## 5. Reconciliación (job periódico)

`LedgerReconciliation` recalcula `Σ(Credit) − Σ(Debit)` por wallet y compara contra `PostedCents` cacheado. Divergencia → alerta + asiento `Adjustment` correctivo auditado (nunca edición del ledger). Valida la decisión "saldo cacheado con guardas" (`Domain_Design.md §3.1`): la caché es verificable contra la fuente de verdad inmutable. **Ya no hay** reconciliación de `HeldCents` (no existe).

## 6. Logging estructurado

- INFO por operación exitosa: `{reason, tenant, referenceId, amountCents, direction, balanceAfter, correlationId}`.
- WARN en `InsufficientFunds`/`ConcurrencyConflict`/top-up `Failed` (esperados, no errores).
- ERROR solo en fallo inesperado. Nunca secretos.
- **Auditoría de admin:** `Adjustment`/`Freeze`/`Unfreeze` y cambios de `ChannelPrice` loguean `ActorId`+`reason`; los `Adjustment` quedan en el ledger.

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Snapshot `BalanceAfterCents` por asiento habilita auditoría/reconciliación | `Data_Model.md §1.2` (diseño) | NEW | n/a |
| CorrelationId/ReferenceId opaco (patrón existente) | `PostmasterEmailEvents.cs:37,104` | VERIFIED | 92% |
| Replay contabilizable (reentregas at-least-once) | `SqlBusinessIdempotencyExecutor.cs:108-116` (patrón) | VERIFIED | 90% |
| Métricas/alertas/reconciliación (prepago, sin holds) | `00_Plan` (diseño) | NEW | n/a |
