# WhatsApp — Transactional Protocol

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Coherente con `../06_Cross_Service_Transactional_Protocol.md` (saga balance + dispatch).

## 1. Principio: entrega asíncrona con costo real diferido

WhatsApp rompe el patrón "cobra al enviar" porque **el costo real solo se conoce por webhook** (`pricing.category`, `billable`). La saga separa **reserva** (antes de tocar Meta), **envío** (POST idempotente), y **consumo/refund** (al confirmar entrega o fallo). Corrige el TOCTOU del legado (check+debit en 2 HTTP calls, debit antes de `SaveChanges` — anti-patrón §4 de ADR-CAMP-000).

## 2. Flujo campaña (reserva la trae Campaigns)

```
Campaigns  ── reserve(estimado, N destinatarios) ──►  Wallet   (movimiento RESERVE inmutable)
Campaigns  ── WhatsAppDispatchRequested(ReservationRef) ──►  WhatsApp
WhatsApp:
  1. ValidateAndAcceptDispatch (plantilla Approved? sesión? E.164? categoría)
        └─ falla ⇒ WhatsAppMessage(Rejected) + refund(ReservationRef, DispatchId)  → result Rejected
  2. SendToMeta (POST Cloud API, Idempotency por DispatchId → wamid)  → WhatsAppMessage(Sent) → result Sent
  3. webhook delivered ⇒ ApplyDeliveryStatus + captura pricing.BilledAmount
        └─ RequestConsume(ReservationRef, DispatchId, BilledAmount)  → Wallet CONSUME inmutable → result Delivered
  4. webhook read ⇒ result Read (sin efecto Wallet)
  X. webhook failed (en no-terminal) ⇒ RequestRefund(ReservationRef, DispatchId) → Wallet REFUND → result Failed
```

- **Un solo consume y un solo refund por `DispatchId`** (mutuamente excluyentes), garantizado por `ProcessedBusinessMessage(op="wa.settle", scope=DispatchId)`.
- La **reserva** cubre el estimado (precio máximo por categoría); el **consume** ajusta al costo real del webhook. Si `real < reservado`, Wallet libera la diferencia en el mismo movimiento de consume (política de Wallet, no de WhatsApp).

## 3. Flujo envío individual (WhatsApp origina la reserva)

```
POST /messages (Idempotency-Key)
  └─ reserve(estimado) → Wallet   (si falla: 409 INSUFFICIENT_BALANCE, no se llama a Meta)
  └─ ValidateAndAccept → SendToMeta → (webhooks) consume/refund
```
Idéntico settlement; la única diferencia es quién crea la reserva.

## 4. Costeo (per-conversación / per-plantilla)

- El costo **autoritativo** es el `pricing`/`conversation` del webhook de Meta, mapeado a `Money(cents, "USD")`.
- El **estimado de reserva** usa una tabla de precio por `(Category, Country)` mantenida en Wallet/Campaigns (no en el frontend, no en appsettings del ejecutor). Migración de Meta a per-message pricing (jul-2025): el estimado se parametriza por categoría; el real siempre viene del webhook. Ver blocker B-WA-DOM-2.
- Corrige el costo plano del legado (`CostService.cs:17` = 0.005; `appsettings.json:141` = 0.01) que ignoraba categoría, país y conversación.

## 5. Idempotencia del POST a Meta

- Meta acepta un identificador de idempotencia; además persistimos `wamid` con **UNIQUE (TenantId, ProviderMessageId)**. Un reintento del outbox tras crash **no** produce dos mensajes: si ya hay `wamid` para el `DispatchId`, se salta el POST y se re-emite el result. Corrige el fan-out fire-and-forget que se perdía al reiniciar (anti-patrón §2).

## 6. Orden y compensación
- No hay 2PC. La consistencia es **eventual vía outbox at-least-once + handlers idempotentes**.
- Fallo tras `Sent` sin webhook en `T_max` (SLA): reaper marca `Failed(timeout)` y **refund** (política conservadora: no cobrar lo no confirmado). Si un webhook `delivered` tardío llega después, se reconcilia (consume + revertir refund) idempotentemente por `DispatchId`. Ver `Concurrency_Spec.md §Reaper`.
- Fallo del `consume` (Wallet caído): reintento outbox; el `WhatsAppMessage` queda en `Delivered` con `ConsumeRef=null` hasta confirmarse (no se pierde dinero: la reserva sigue viva).

## 7. Evidencia
| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| TOCTOU legado (check+debit 2 HTTP) | ADR-CAMP-000 §Anti-patrones 4; `05_Master_ADR.md:47` | VERIFIED | 92% |
| Costo plano legado | `CostService.cs:17`, `appsettings.json:141` | VERIFIED | 96% |
| Saga reserve→consume/refund es la decisión aprobada | `05_Master_ADR.md:29` (decisión 3) | VERIFIED | 95% |
| pricing/conversation por webhook | Meta Cloud API docs | DOCUMENTED_ONLY | 85% |
