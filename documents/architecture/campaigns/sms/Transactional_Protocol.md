# TaxVision.Sms — Transactional Protocol

- **Servicio:** SMS (`TaxVision.Sms`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Alineado con `../06_Cross_Service_Transactional_Protocol.md` (saga balance + dispatch). Objetivo: entregar un SMS consumiendo **dinero real** sin TOCTOU, sin doble-cobro y sin cobrar lo no entregado. Wolverine at-least-once + handlers idempotentes + saga con reserva.

## 1. Principio: reserve → consume/refund (nunca check-then-debit)

El legado hacía **check y debit en dos HTTP calls y debitaba antes de `SaveChanges`** (ADR-CAMP-000 §Anti-patrón 4) y cobraba **al crear** la campaña, cobrando incluso lo no entregado. Aquí:

1. **RESERVE** (dinero real bloqueado) antes de tocar al proveedor.
2. **CONSUME** sólo al confirmar `Delivered` (por el costo **actual** en segmentos).
3. **REFUND** en cualquier terminal fallido posterior a la reserva.

Solo **Wallet** muta saldo, por movimientos inmutables; SMS sólo emite solicitudes.

## 2. Saga de envío individual (SMS directo)

```
[HTTP /api/sms/send] (Idempotency-Key)
  └─ crear SmsDispatch(Quoted) + calcular segments/cost         (tx local)
  └─ publicar SmsWalletReserveRequested(DispatchId, cost, key)  (outbox, misma tx)
        │
        ▼ Wallet
  SmsWalletReserved  ──► SmsDispatch.Reserved (RowVersion)      (tx local)
     │  └─ enviar al proveedor (HTTP)                            (efecto externo)
     │       ├─ 2xx/queued ► SmsDispatch.Accepted(providerMsgId)
     │       └─ 4xx/5xx no-retryable ► SmsDispatch.Failed ► SmsWalletRefundRequested
     ▼
  SmsWalletReserveDenied (saldo) ─► SmsDispatch.Failed(insufficient_balance)  // nada que refundear
        │
        ▼ webhook DLR (más tarde)
  delivered   ─► SmsDispatch.Delivered ─► SmsWalletConsumeRequested(actualCost)
  undelivered ─► SmsDispatch.Failed     ─► SmsWalletRefundRequested
```

**Orden de commit crítico:** el `SmsDispatch` y el evento de reserva se persisten en **la misma transacción local** (outbox de Wolverine). Nunca se llama al proveedor antes de que la reserva esté confirmada. El envío externo ocurre **después** de `Reserved` y su resultado se captura; si el proceso muere entre el envío y el `Accepted`, la reconciliación (§5) lo resuelve.

## 3. Saga de campaña (fan-out)

Campaigns reserva el **estimate del run** en Wallet a nivel agregado y hace fan-out de `SmsDispatchRequested` por destinatario. SMS:
1. Crea el `SmsDispatch` idempotente por `(campaign, recipient, attempt)`.
2. Renderiza + segmenta ⇒ conoce el costo **actual** por destinatario.
3. Envía; al `Delivered` reporta `ActualCostCents` en `SmsDispatchDelivered`.
4. **Wallet concilia** contra la reserva del run: consume lo entregado, refunda el remanente reservado no entregado. (La política de quién dispara consume/refund por-item vs. por-run la fija `wallet-ledger/` + `06_…`; SMS **siempre** reporta el actual y la intención por-item.)

Diferencia con legado: el legado marcaba `Sent` a todos los no-fallidos y **no** distinguía entregado de aceptado (`SmsCampaignSender.cs:307-317`), doble-contando en reintentos. Aquí `Accepted != Delivered`; sólo `Delivered` consume.

## 4. Idempotencia transaccional
- Toda solicitud a Wallet lleva `IdempotencyKey` derivada de `DispatchId` + operación (`reserve`/`consume`/`refund`). Wallet deduplica por `(operation, scopeId, key)`.
- Todo webhook DLR aplica el efecto una sola vez vía `ProcessedBusinessMessage` `(provider, providerMessageId, eventType)`. Un DLR duplicado no re-consume ni re-refunda.
- Ver `Idempotency_Spec.md`.

## 5. Fallos y reconciliación
| Escenario | Resolución |
|---|---|
| Muere tras `Reserved`, antes de enviar | job de barrido: dispatch `Reserved` sin `provider_message_id` tras timeout ⇒ reintenta envío (idempotente en proveedor vía client-ref) o refunda y marca `Failed`. |
| Proveedor aceptó pero perdimos el `Accepted` | reconciliación por `provider_message_id`/client-ref: consulta estado al proveedor; evita doble-envío. |
| DLR nunca llega | TTL de `Accepted`: tras N horas sin DLR, política configurable (consume optimista o refund). Se documenta como decisión operativa. |
| Refund tras consume (edge) | Wallet rechaza doble-terminal por su propia idempotencia; SMS no fuerza. |

## 6. Consistencia multi-tenant
Todos los handlers corren con tenant explícito en el scope Wolverine + query filter fail-closed (ver `Guia_IgnoreQueryFilters`). Un webhook resuelve el tenant por sender/DID antes de abrir el scope; si no resuelve, se descarta (no se procesa cross-tenant).

## 7. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado TOCTOU / cobro al crear | ADR-CAMP-000 §Anti-patrón 4 | VERIFIED | 95% |
| Legado no distingue Accepted/Delivered, marca Sent | `SmsCampaignSender.cs:307-317` | VERIFIED | 95% |
| Reserve→consume/refund con Wallet inmutable | `00_Overview_And_Index.md` §Principio, `05_Master_ADR.md` Dec.3 | VERIFIED (política) | 96% |
| Saga individual/campaña propuesta | este documento | NEW | — |
