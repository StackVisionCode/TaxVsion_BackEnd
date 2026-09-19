# WhatsApp — Transactional Protocol

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — WhatsApp SÍ es un servicio nuevo (`TaxVision.WhatsApp`, Meta/WABA), pero de FASE POSTERIOR y solo como CONSUMER del contrato de dispatch — sin dinero.** Consume `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un Wallet o cobro por este canal queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Coherente con `../06_Cross_Service_Transactional_Protocol.md` (dispatch). **Saga de balance — (removida: sin dinero en el canal; ver banner).**

## 1. Principio: entrega asíncrona (estado diferido por webhook)

WhatsApp entrega de forma **asíncrona**: el POST a Meta devuelve `wamid` y el estado real (`delivered`/`read`/`failed`) llega por webhook. El POST es **idempotente** (ver §5). **Sin dinero:** este canal NO reserva/consume/refunda saldo; toda saga de reserva/consumo/refund queda **superseded** — la autorización por balance es un interceptor/PEP externo y diferido (ver banner y `../05_Master_ADR.md` D1/D3/D7).

## 2. Flujo campaña (dispatch → send → webhook)

```
Campaigns  ── campaign.dispatch.requested.v1 ──►  WhatsApp
WhatsApp:
  1. ValidateAndAcceptDispatch (plantilla Approved? sesión? E.164? categoría)
        └─ falla ⇒ WhatsAppMessage(Rejected)  → result Rejected
  2. SendToMeta (POST Cloud API, Idempotency por DispatchId → wamid)  → WhatsAppMessage(Sent) → result Sent
  3. webhook delivered ⇒ ApplyDeliveryStatus  → result Delivered
  4. webhook read ⇒ result Read
  X. webhook failed (en no-terminal) ⇒ WhatsAppMessage(Failed) → result Failed
```

- Reserva / consume / refund por `DispatchId` — (removido: sin dinero en el canal; ver banner).
- Estimado / costo real / ajuste de saldo — (removido: sin dinero en el canal; la autorización por balance es externa/diferida, ver banner).

## 3. Flujo envío individual

```
POST /messages (Idempotency-Key)
  └─ ValidateAndAccept → SendToMeta → (webhooks) avance de estado
```
Reserva y settlement de saldo — (removido: sin dinero en el canal; la autorización por balance es un interceptor/PEP externo y diferido, ver banner).

## 4. Costeo — (removido)

— (removido: sin dinero en el canal; no hay costeo/estimado/pricing por este canal; el blocker B-WA-DOM-2 queda superseded; ver banner).

## 5. Idempotencia del POST a Meta

- Meta acepta un identificador de idempotencia; además persistimos `wamid` con **UNIQUE (TenantId, ProviderMessageId)**. Un reintento del outbox tras crash **no** produce dos mensajes: si ya hay `wamid` para el `DispatchId`, se salta el POST y se re-emite el result. Corrige el fan-out fire-and-forget que se perdía al reiniciar (anti-patrón §2).

## 6. Orden y compensación
- No hay 2PC. La consistencia es **eventual vía outbox at-least-once + handlers idempotentes**.
- Fallo tras `Sent` sin webhook en `T_max` (SLA): reaper marca `Failed(timeout)`. Si un webhook `delivered` tardío llega después, se reconcilia el **estado** idempotentemente por `DispatchId`. Ver `Concurrency_Spec.md §Reaper`. (Efecto de refund/consume — removido: sin dinero en el canal, ver banner.)
- Compensación de saldo (consume/refund/reserva) — (removido: sin dinero en el canal; ver banner).

## 7. Evidencia
> Filas de evidencia sobre TOCTOU/costo/saga reserve→consume/refund/pricing — (removidas: sin dinero en el canal; ver banner).

| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| POST idempotente por `DispatchId` + `wamid` UNIQUE | `Idempotency_Spec.md §2` | VERIFIED | 95% |
| Reenvío/orden de webhooks Meta | Meta Cloud API docs | DOCUMENTED_ONLY | 85% |
