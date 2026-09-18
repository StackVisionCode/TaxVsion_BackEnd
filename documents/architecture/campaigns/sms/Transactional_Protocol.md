# TaxVision.Sms — Transactional Protocol

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — SMS NO es un servicio nuevo; `TaxVision.Sms` YA EXISTE y ya es M2M** (`SendSmsBatchCommand`, `POST /sms/messages`, `ActorType.Service`). El canal SMS es un **CONSUMER dentro de `TaxVision.Sms`** que procesa `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio SMS nuevo y/o un Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- **Servicio:** SMS (`TaxVision.Sms`) — consumer del canal SMS **dentro de `TaxVision.Sms` (ya existente)**
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Objetivo: entregar un SMS de forma idempotente, sin doble-envío y distinguiendo `Accepted` de `Delivered`. Wolverine at-least-once + handlers idempotentes. — (removido: sin dinero en el canal; ver banner — antes saga de balance reserve/consume/refund contra Wallet).

## 1. Principio: idempotencia por dispatch (sin saldo en el canal)

— (removido: sin dinero en el canal; ver banner — antes el principio reserve → consume/refund). La autorización por balance es un interceptor/PEP externo y DIFERIDO, fuera de este canal. Lo que el canal SÍ garantiza:

1. Un `SmsDispatch` (≡ `CampaignDispatchAttempt`) idempotente por `(tenant, campaign_run?, recipient_id, attempt)` (≡ `UNIQUE(run_id, recipient_id, attempt_no)`; `dispatch_id` opaco por intento) antes de tocar al proveedor. `recipient_id` estable entre intentos; manual ⇒ id generado, nunca `"manual"`.
2. Envío al proveedor con client-ref determinístico ⇒ un reintento tras crash no genera dos SMS reales.
3. `Accepted != Delivered`: la aceptación del proveedor se reporta como `Outcome=Accepted`; sólo el DLR confirma la entrega real (`Outcome=Delivered`). Un **timeout** (sin respuesta al enviar o `Accepted` sin DLR tras TTL) se reporta `Outcome=Unknown`, **nunca** `Failed` (ver §5 reconciliación).

## 2. Flujo de envío individual (SMS directo)

```
[HTTP /api/sms/send] (Idempotency-Key)
  └─ crear SmsDispatch(Segmented) + calcular segments            (tx local)
     │  └─ enviar al proveedor (HTTP)                            (efecto externo)
     │       ├─ 2xx/queued ► SmsDispatch.Accepted(providerMsgId)
     │       └─ 4xx/5xx no-retryable ► SmsDispatch.Failed
     │
     ▼ webhook DLR (más tarde)
  delivered   ─► SmsDispatch.Delivered
  undelivered ─► SmsDispatch.Failed
```
— (removido: sin dinero en el canal; ver banner — antes reserva Wallet previa al envío + consume/refund + rama `insufficient_balance`).

**Orden de commit crítico:** el `SmsDispatch` se persiste en **una transacción local** (outbox de Wolverine). El envío externo ocurre **después** de materializar el dispatch y su resultado se captura; si el proceso muere entre el envío y el `Accepted`, la reconciliación (§5) lo resuelve.

## 3. Flujo de campaña (fan-out)

Campaigns (orquestador agnóstico) hace fan-out de `campaign.dispatch.requested.v1` por destinatario. El consumer SMS:
1. Crea el `SmsDispatch` (≡ `CampaignDispatchAttempt`) idempotente por `(campaign_run, recipient_id, attempt)`.
2. **Reconstruye** el cuerpo desde `ContentRef`/`SmsPayload` (texto congelado exacto) + segmenta ⇒ conoce los segmentos por destinatario (dato de facturación del proveedor; Campaign no lo tarifa).
3. Envía; al aceptar el proveedor responde `campaign.dispatch.result.v1(Accepted)` y al DLR de entrega `campaign.dispatch.result.v1(Delivered)`; un timeout ⇒ `Unknown` (nunca `Failed`).
4. — (removido: sin dinero en el canal; ver banner — antes conciliación Wallet consume/refund contra la reserva del run). La autorización por balance es un interceptor/PEP externo y DIFERIDO.

Diferencia con legado: el legado marcaba `Sent` a todos los no-fallidos y **no** distinguía entregado de aceptado (`SmsCampaignSender.cs:307-317`), doble-contando en reintentos. Aquí `Accepted != Delivered`; sólo `Delivered` cuenta como entrega.

## 4. Idempotencia transaccional
- — (removido: sin dinero en el canal; ver banner — antes claves de idempotencia de las solicitudes reserve/consume/refund a Wallet).
- Todo webhook DLR aplica el efecto una sola vez vía `ProcessedBusinessMessage` `(provider, providerMessageId, eventType)`. Un DLR duplicado no produce doble efecto.
- Ver `Idempotency_Spec.md`.

## 5. Fallos y reconciliación
| Escenario | Resolución |
|---|---|
| Muere tras `Segmented`, antes de enviar | job de barrido: dispatch `Segmented` sin `provider_message_id` tras timeout ⇒ reintenta envío (idempotente en proveedor vía client-ref); si el envío sigue sin respuesta se reporta `Outcome=Unknown` (no `Failed`). — (removido: sin dinero en el canal; ver banner — antes refund). |
| Proveedor aceptó pero perdimos el `Accepted` | reconciliación por `provider_message_id`/client-ref: consulta estado al proveedor; evita doble-envío. |
| DLR nunca llega | TTL de `Accepted`: tras N horas sin DLR ⇒ se reporta `Outcome=Unknown` (**nunca** `Failed`) y la unidad queda elegible para reconciliación/reintento según política configurable. Se documenta como decisión operativa. — (removido: sin dinero en el canal; ver banner). |
| ~~Refund tras consume~~ | — (removido: sin dinero en el canal; ver banner). |

## 6. Consistencia multi-tenant
Todos los handlers corren con tenant explícito en el scope Wolverine + query filter fail-closed (ver `Guia_IgnoreQueryFilters`). Un webhook resuelve el tenant por sender/DID antes de abrir el scope; si no resuelve, se descarta (no se procesa cross-tenant).

## 7. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado no distingue Accepted/Delivered, marca Sent | `SmsCampaignSender.cs:307-317` | VERIFIED | 95% |
| Flujo individual/campaña propuesto (sin dinero) | este documento | NEW | — |
