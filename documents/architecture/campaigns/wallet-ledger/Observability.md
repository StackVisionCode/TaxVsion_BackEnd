# Wallet/Ledger — Observability

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

---

## 1. Principios

Dinero real → observabilidad **auditable y reconciliable**. Todo movimiento deja un `LedgerEntry` inmutable con snapshot de saldo (`BalanceAfterPostedCents/BalanceAfterHeldCents`, `Data_Model.md §1.3`): la traza financiera es la tabla misma, no solo logs. Nunca se loguean montos como `float` ni PII; `ScopeId` y `TenantId` son GUIDs opacos aptos para logs/correlación.

## 2. Correlación

- **CorrelationId** de Wolverine se propaga en todos los eventos; se casa con el `ScopeId` (RunId/SmsSendId) — mismo patrón que `CampaignId` opaco en `PostmasterEmailEvents.cs:37,104`.
- Cada `LedgerEntry` guarda `Operation`, `ScopeId`, `IdempotencyKey`, `SourceReference` → traza extremo-a-extremo: top-up (SaaSPaymentId) → recharge → reserve(RunId) → consume → refund.

## 3. Métricas (OpenTelemetry / Prometheus)

| Métrica | Tipo | Etiquetas | Uso |
|---|---|---|---|
| `wallet_reserve_total` | counter | `tenant`,`context`,`outcome`(ok/insufficient/conflict) | tasa de reservas, ratio de insuficiencia |
| `wallet_consume_cents_total` | counter | `tenant`,`context` | ingresos consumidos |
| `wallet_refund_cents_total` | counter | `tenant`,`reason`(completed/cancelled/expired) | devoluciones |
| `wallet_recharge_cents_total` | counter | `tenant` | recargas (top-up) |
| `wallet_available_cents` | gauge | `tenant` | saldo disponible (alertas de saldo bajo) |
| `wallet_held_cents` | gauge | `tenant` | fondos apartados vivos |
| `wallet_reservation_expired_total` | counter | `tenant` | holds abandonados (salud de consumidores) |
| `wallet_idempotency_replay_total` | counter | `operation` | replays (reentregas at-least-once) |
| `wallet_concurrency_conflict_total` | counter | `operation` | contención de RowVersion |
| `wallet_op_duration_seconds` | histogram | `operation` | latencia p50/p95/p99 |

## 4. Alertas

- **Reconciliación rota** (crítica): `PostedCents != Σ(entries confirmados)` para algún balance → posible corrupción/bug. Bloquea nada, pero pagina.
- **Held creciente sin consume** (`wallet_reservation_expired_total` alto): consumidores (Campaigns/SMS) muriendo tras Reserve → salud del fan-out.
- **Insuficiencia alta** (`outcome=insufficient` sostenido): tenants sin saldo intentando campañas → señal de negocio (avisar recarga vía `BalanceLowWarningIntegrationEvent`).
- **Conflictos de concurrencia** por encima de umbral: hotspot en un balance; revisar patrón de consume incremental.

## 5. Reconciliación (job periódico)

Recalcula `Σ(SignedAmountCents WHERE Kind∈{Recharge,Consume,Refund,Adjust} confirmados)` y `Σ(reservas Held.Remaining)` por balance y compara contra `PostedCents`/`HeldCents` cacheados. Divergencia → alerta + entry `Adjust` correctivo auditado (nunca edición del ledger). Esto valida la decisión "saldo cacheado con guardas" (`Domain_Design.md §3.1`): la caché es verificable contra la fuente de verdad inmutable.

## 6. Logging estructurado

- Nivel INFO por operación exitosa: `{op, tenant, scopeId, amountCents, currency, availableAfter, correlationId}`.
- WARN en `InsufficientFunds`/`IdempotencyConflict`/`ConcurrencyConflict` (esperados, no errores).
- ERROR solo en fallo inesperado (excepción no controlada). Nunca se loguea el `RequestFingerprint` completo ni secretos.
- **Auditoría de admin:** `Adjust`/`Freeze`/`Unfreeze` loguean `ActorId`+`reason` (quién y por qué), y quedan en el ledger.

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Snapshot de saldo por entry habilita auditoría/reconciliación | `Data_Model.md §1.3` (diseño) | NEW | n/a |
| CorrelationId/ScopeId opaco (patrón existente) | `PostmasterEmailEvents.cs:37,104` | VERIFIED | 94% |
| Replay contabilizable (reentregas at-least-once) | `SqlBusinessIdempotencyExecutor.cs:108-116` | VERIFIED | 92% |
| Métricas/alertas/reconciliación | diseño | NEW | n/a |
