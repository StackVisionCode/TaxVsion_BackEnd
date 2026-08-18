# Campaigns Suite — Plan de Implementación

Fecha: 2026-07-28. Fases ordenadas **respetando la dependencia dura Wallet→ejecución** (BLK-1). Cada fase tiene entregable verificable y no arranca sin sus precondiciones. El orden minimiza el riesgo de reintroducir anti-patrones del legado (`05_Master_ADR §Anti-patrones`).

## Resumen de fases

| Fase | Entrega | Desbloquea | Blockers que cierra |
|---|---|---|---|
| 1 | Wallet/Ledger real (USD) | toda ejecución | BLK-1 |
| 2 | Top-up (PaymentApp + credit-on-paid) | probar ciclo de dinero real | BLK-2 |
| 3 | Campaigns esqueleto + `CampaignRun` + saga reserve/consume/refund (sin canal real) | fan-out | — |
| 4 | Email SMTP2GO (primer canal real) end-to-end | MVP funcional | BLK-3 (precio Email) |
| 5 | Scheduler con lease (Immediate/Scheduled/Recurring) | recurrencia | doble-scheduler |
| 6 | Reuse Push + contrato bulk | 2º canal, valida contrato común | BLK-6 |
| 7 (fase 2) | SMS + WhatsApp | canales adicionales | BLK-4, OQ-1/2/4 |

## Fase 1 — Wallet/Ledger (`TaxVision.Wallet`) — **fundacional**
Precondición: ninguna. **Sin esto no se ejecuta nada.**
- Aggregate `Wallet` por tenant; `LedgerMovement` inmutable (`TopUp/Reservation/Consume/Refund/Adjustment`); saldo **derivado** de movimientos (nunca campo mutable suelto).
- `Money(long,"USD")` — copia propia del value object (`04 §3`).
- Idempotencia `(operation, scopeId, idempotencyKey)` vía `ProcessedBusinessMessage` (copia en Wallet.Infrastructure, patrón `Growth/.../Idempotency/ProcessedBusinessMessage.cs`).
- State guards: I4 (`reservado ≥ consumido + devuelto`), no consumo sobre reserva inexistente/agotada.
- Multi-tenant fail-closed (query filter global + repos tenant-scoped).
- Endpoints M2M `Reserve/Consume/Refund` con `[RateLimit]` + audience/scope; unique constraint sobre `(scopeId, operation, idempotencyKey)`.
- **Verificable:** tests de conservación y de re-entrega (mismo movimiento no se duplica).

## Fase 2 — Top-up (PaymentApp glue)
Precondición: Fase 1.
- Nuevo `SaaSPaymentType` (top-up) en PaymentApp (`SaaSPayment`, platform→tenant).
- Handler en Wallet: al recibir payment-succeeded → `LedgerMovement(TopUp)` idempotente por `paymentId`.
- **Verificable:** pago de prueba acredita saldo exactamente una vez aun con evento duplicado.

## Fase 3 — Campaigns esqueleto + saga (sin canal real)
Precondición: Fase 1 (Wallet vivo).
- Aggregate `Campaign` (mutable solo en `Draft`) + `CampaignRun` **inmutable** + `Recipients` con `(runId, recipientRef, attempt)`.
- Resolución de audiencia **por referencia a Customer** (no snapshot); materialización de `Recipients` en el run.
- Estimación de costo = `Σ price(canal)` (precio owner Campaigns/Wallet).
- Consulta de gate `module.campaigns` a Subscription (ortogonal al balance).
- Orquestación de la saga (`06`): `ReserveFunds` → publicar `Dispatch` por recipient (outbox) → aplicar `DispatchResult` idempotente → `Consume/Refund` al cierre.
- **Ejecutor "loopback" de prueba** que responde `Delivered/Failed` para validar la saga **antes** de integrar un proveedor.
- **Verificable:** run completo con conservación de dinero contra el loopback; reinicio a mitad reanuda sin doble efecto.

## Fase 4 — Email SMTP2GO (`TaxVision.Campaigns.Email`) — primer canal real
Precondición: Fase 3 + **BLK-3 cerrado** (precio Email fijado).
- Ejecutor consume `Dispatch(channel=Email)`, dedup por `dispatchId`.
- Render vía **Scribe** (Fluid/Liquid); assets por referencia a CloudStorage (`EmailInlineAssetReference`), nunca bytes por el bus.
- Integración **SMTP2GO** (NO Postmaster); **secreto SMTP2GO cifrado** en el ejecutor.
- Emite `DispatchResult` con `CampaignId` de eco (patrón `PostmasterEmailEvents.cs:104`).
- **Verificable:** MVP end-to-end (top-up→campaña Email→reserve→entrega→consume/refund) con idempotencia y resiliencia a reinicio (definición de "hecho", `07 §1`).

## Fase 5 — Scheduler con lease
Precondición: Fase 3 (puede paralelizarse con 4).
- Owner del reloj: `Immediate` / `Scheduled(at)` / `Recurring(rule)`.
- **Lease/optimistic-lock atómico**: un solo ejecutor procesa un slot al escalar (fix `Status=Sending` no-atómico y doble-scheduler, `CampaignSchedulerBackgroundService.cs:54-59`).
- Cada disparo Recurring crea un **nuevo `CampaignRun`** (no muta una fila, fix `:115-142`).
- Decisión servicio-propio vs módulo → `scheduler/ADR.md`.
- **Verificable:** dos instancias en paralelo no doble-disparan; recurrente genera N runs distintos.

## Fase 6 — Reuse Push + contrato bulk
Precondición: Fase 3/4 (contrato dispatch/result estable).
- Agregar **contrato bulk** sobre Notification (hoy 1:1 transaccional); reusar `FcmPushSender`.
- Push consume `Dispatch(channel=Push)` y emite el mismo `DispatchResult`.
- **Verificable:** una campaña Push corre por el mismo contrato común sin cambios en Campaigns/Wallet.

## Fase 7 (fase 2 del producto) — SMS + WhatsApp
Precondición: BLK-4 + OQ-1/OQ-2/OQ-4 resueltos.
- `TaxVision.Sms` (proveedor por decidir) y `TaxVision.WhatsApp` (Meta/WABA), cada uno con secretos cifrados propios.
- Reutilizan Wallet para envíos individuales (`scopeId = messageId`), no solo campañas.
- Requiere política de refund por bounce definida por canal (OQ-4).

## Convenciones transversales (todas las fases)

- **Mensajería:** Wolverine outbox/inbox durable, at-least-once, handlers idempotentes. Nunca "exactly-once".
- **Dinero:** minor units `long` + `Money` por contexto; nunca `float`; nunca monto del frontend.
- **Seguridad:** RBAC acumulativo (JWT + actor-type + `[HasPermission]` + tenant + ownership + M2M audience/scope); **nunca** JWT de usuario persistido; secretos de proveedor cifrados.
- **Multi-tenant:** query filter global fail-closed + repos tenant-scoped + `.IgnoreQueryFilters()`+tenant explícito en scope Wolverine.
- **Endpoints:** todo público con `[RateLimit(categoría)]` o `[RateLimitExempt]`.
- **Mutaciones:** por métodos del aggregate devolviendo `Result`.
- **Docs por servicio:** cada microservicio produce el set estándar (`Domain_Design`, `State_Machines`, `Transactional_Protocol`, `Idempotency_Spec`, `Concurrency_Spec`, `Security`, `ADR`, …) — ver `00 §Índice`.
