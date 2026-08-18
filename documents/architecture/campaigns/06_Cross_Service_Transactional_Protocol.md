# Campaigns Suite — Protocolo Transaccional Cross-Service (el corazón)

Fecha: 2026-07-28. Define la **saga completa** `reserve → dispatch fan-out → consume/refund` entre **Campaigns**, **Wallet/Ledger** y los **ejecutores de canal**. Todo asíncrono por **Wolverine outbox/inbox durable (at-least-once)** — nunca "exactly-once"; la corrección viene de **handlers idempotentes + unique constraints + state guards**, no del transporte.

Lee antes: `03_Ubiquitous_Language.md` (Reservation/Consume/Refund/Dispatch/Delivery), `04_Ownership_Matrix.md` (solo Wallet muta saldo).

## 0. Invariantes (no negociables)

- **I1 — Solo Wallet muta saldo**, y solo por `LedgerMovement` inmutable. Campaigns/ejecutores solo **piden** movimientos.
- **I2 — Reserve antes del fan-out; consume después de la entrega confirmada.** Nunca se gasta antes de intentar (corrige el debit-al-crear del legado, `CreateCampaignCommandHandler.cs:278`).
- **I3 — Idempotencia en dos ejes:** dispatch/entrega por `(campaignRunId, recipientRef, attempt)`; movimientos Wallet por `(operation, scopeId, idempotencyKey)` (`ProcessedBusinessMessage.cs:27-74`).
- **I4 — Conservación:** para cada CampaignRun, `reservado == consumido + devuelto` al cerrar. Un movimiento no puede consumir/devolver más de lo reservado (state guard en el aggregate Wallet).
- **I5 — At-least-once ⇒ todo handler tolera re-entrega:** re-procesar un mensaje ya aplicado devuelve el resultado previo, no un segundo efecto.
- **I6 — Correlación opaca:** `CampaignId`/`CampaignRunId` viajan de ida y **vuelven intactos** en el result; el ejecutor no los interpreta (patrón `PostmasterEmailEvents.cs:37,104`).

## 1. Actores y IDs

| Actor | Rol en la saga | Clave de idempotencia que posee |
|---|---|---|
| **Scheduler** | dispara el run (lease atómico) | `leaseToken` sobre `(campaignId, scheduledSlot)` |
| **Campaigns** | orquestador de la saga; owner del `CampaignRun` y `Recipients` | `(campaignRunId, recipientRef, attempt)` |
| **Wallet** | asienta movimientos | `(operation, scopeId=campaignRunId, idempotencyKey)` |
| **Ejecutor** | entrega + reporta result | dedupe de dispatch por `dispatchId` |

`scopeId` de Wallet = `campaignRunId` (agrupa reserva/consumos/refunds de una corrida). Envío SMS individual usa `scopeId = messageId`.

## 2. Happy path (diagrama de secuencia)

```
Scheduler          Campaigns              Wallet                 Ejecutor(canal)
   │  lease OK        │                      │                        │
   ├─fire run────────►│                      │                        │
   │            resolve audiencia (Customer, por ref)                 │
   │            crea CampaignRun (Reserved?) │                        │
   │            estima costo = Σ price(canal) sobre N recipients      │
   │                  ├─ReserveFunds(runId, amount, key=run)─────────►│  (Wallet)
   │                  │◄─FundsReserved(runId) │  movimiento Reservation inmutable
   │            run.State = Dispatching       │                        │
   │  ── fan-out por destinatario (un evento Dispatch por recipient, idempotente) ──
   │                  ├─Dispatch(runId,recipient,attempt=1)───────────────────────►│
   │                  │                       │                render(Scribe)+entrega proveedor
   │                  │◄──────────── Result(Delivered|Failed|Bounced|…, CampaignId)┤
   │            agrega result por recipient (ProcessedBusinessMessage)             │
   │  ── cuando el run cierra (todos los recipients en estado terminal) ──         │
   │                  ├─ConsumeReserved(runId, deliveredAmount, key)──►│  Consume (definitivo)
   │                  ├─RefundReserved(runId, undeliveredAmount, key)─►│  Refund (al disponible)
   │            run.State = Completed          │                        │
```

## 3. Fases detalladas

### Fase A — Reserve (antes del fan-out)
1. Campaigns resuelve la audiencia contra **Customer por referencia** (no snapshot) y **materializa los `Recipients` en el `CampaignRun`** (inmutable).
2. Estima `amount = Σ price(canal, recipient)` en USD minor units (precio owner = Campaigns/Wallet, **no frontend**).
3. Envía `ReserveFunds(campaignRunId, amount, idempotencyKey = "reserve:{runId}")` a Wallet.
4. Wallet, en **una** transacción: valida disponible ≥ amount (state guard sobre el saldo derivado), crea `LedgerMovement(Reservation)` inmutable, responde `FundsReserved`. Si insuficiente → `InsufficientFunds` → run pasa a `Rejected(insufficient_balance)`, **no** hay fan-out.
5. **Gate ortogonal:** antes de reservar, Campaigns verifica `module.campaigns` (Subscription). Sin entitlement → `Rejected(not_entitled)` sin tocar Wallet.

> Regla: **no se despacha ni un mensaje sin reserva confirmada.** Esto invierte el legado (que enviaba y luego "ajustaba" con refund vía JWT persistido, `CampaignSendService.cs:120-127`).

### Fase B — Dispatch fan-out (idempotente por destinatario)
6. Por cada Recipient, Campaigns publica **un** `Dispatch(campaignRunId, recipientRef, attempt, channel, templateRef, correlation=CampaignId)` por el outbox.
7. Wolverine entrega at-least-once. El ejecutor **dedup por `dispatchId`** (business-inbox): si ya lo procesó, re-emite el result previo sin re-entregar.
8. El ejecutor renderiza (Scribe si aplica), llama al proveedor, y publica `Result` con el mismo `CampaignId` de vuelta.
9. Campaigns aplica el result **una sola vez** por `(campaignRunId, recipientRef, attempt)` usando `ProcessedBusinessMessage.Begin(operation="apply_result", scopeId=runId, idempotencyKey=recipientRef+attempt, fingerprint)`. Re-entrega ⇒ mismo estado (I5).

> **Sin fan-out síncrono.** El legado hacía `Task.Run`/poll + `Task.Delay` (`CampaignSchedulerBackgroundService.cs:38`, `CampaignSendService`) que se perdía al reiniciar y marcaba `Sent` a todo no-fallido (`CampaignSendService.cs:55-69`). Aquí cada dispatch es un mensaje durable independiente con backpressure natural del bus.

### Fase C — Consume / Refund (tras entrega confirmada)
10. Un Recipient llega a estado **terminal**: `Delivered` (consumible) o `NotDelivered` (`Failed`/`Bounced`/`Suppressed`/`ProviderNotConfigured` → reembolsable). Ver §Refund para la política.
11. Cuando **todos** los Recipients del run están en estado terminal (o venció el `runDeadline`), Campaigns computa:
    - `deliveredAmount = Σ price(recipients Delivered)`
    - `undeliveredAmount = reserved − deliveredAmount`
12. Emite a Wallet, cada uno idempotente por `(operation, scopeId=runId, key)`:
    - `ConsumeReserved(runId, deliveredAmount, key="consume:{runId}")` → `LedgerMovement(Consume)`.
    - `RefundReserved(runId, undeliveredAmount, key="refund:{runId}")` → `LedgerMovement(Refund)`.
13. Wallet valida **I4** (no consumir/devolver más que lo reservado) y asienta. Run → `Completed`.

> Alternativa incremental (opción, ver `09` OQ-6): consumir por-recipient a medida que llega `Delivered` en vez de en batch al cierre. El batch al cierre es el default por simplicidad y menos movimientos; el incremental reduce el capital reservado en runs largos. Ambos respetan I4.

## 4. Política de Refund por no-entregado (BLK-5 / OQ-4)

| Result del ejecutor | ¿Se consume? | ¿Se devuelve? | Razonamiento |
|---|---|---|---|
| `Delivered` | Sí | — | Servicio prestado. |
| `Suppressed` (lista de supresión) | No | Sí | No se intentó el proveedor; no hubo costo real. |
| `ProviderNotConfigured` | No | Sí | No se envió. |
| `Failed` (rechazo pre-entrega, dirección inválida) | No | Sí | No entregado. |
| `Bounced` **soft** | **por decidir** | **por decidir** | El proveedor pudo cobrar el intento. Ver OQ-4. |
| `Bounced` **hard** (post-Sent) | **por decidir** | **por decidir** | Idem; el envío ocurrió. Ver OQ-4. |

**Decisión pendiente (OQ-4):** si el proveedor factura por intento aceptado (típico SMS/WhatsApp), un bounce **posterior** al accept podría **no** reembolsarse. La tabla anterior fija los casos claros; los `Bounced` quedan como **blocker de negocio** hasta definir por canal.

## 5. Fallos, timeouts y reconciliación

| Escenario | Manejo |
|---|---|
| **Reserve enviado, sin respuesta** | Reintento del outbox con la **misma** idempotencyKey. Wallet devuelve el resultado previo (idempotente). Sin doble reserva. |
| **Reserve OK, Campaigns muere antes del fan-out** | Al reiniciar, el run está en `Reserved`; un **reconciler** reanuda el fan-out (los dispatch son idempotentes; los ya enviados dedup en el ejecutor). |
| **Dispatch entregado dos veces (at-least-once)** | Ejecutor dedup por `dispatchId`; result idempotente en Campaigns por `(run,recipient,attempt)`. |
| **Result perdido** | El recipient queda en `Dispatched`; al vencer `recipientTimeout` → nuevo **attempt** (no re-cuenta el anterior) o marca `Failed` según política de retry. |
| **Consume/Refund enviado, sin respuesta** | Reintento con misma key; Wallet idempotente. I4 impide sobre-consumo. |
| **Run nunca cierra (recipients colgados)** | `runDeadline`: fuerza cierre, consume los `Delivered`, devuelve el resto (los colgados se tratan como no-entregados). |
| **Reconciliación periódica** | Job compara, por run cerrado, `reserved == consumed + refunded` (I4). Discrepancia → alerta + `Adjustment` manual (nunca edición de un movimiento). |
| **Top-up:** pago OK pero evento perdido | PaymentApp reintenta el evento payment-succeeded; Wallet acredita `TopUp` idempotente por `paymentId`. Doble evento ⇒ un solo crédito. |

## 6. Contrato de mensajes (forma canónica)

Todos comparten **correlación opaca** (`CampaignId`, `CampaignRunId`) que el transporte/ejecutor no interpreta — generalización directa de `NotificationsEmailSendRequestedIntegrationEvent.CampaignId` (`PostmasterEmailEvents.cs:37`) y sus echoes en los result (`:104,:120,:137,:169`).

```
// Campaigns → Wallet (idempotencyKey obligatorio, scopeId = campaignRunId)
ReserveFunds   { walletTenantId, scopeId, amount(Money), idempotencyKey }
ConsumeReserved{ walletTenantId, scopeId, amount(Money), idempotencyKey }
RefundReserved { walletTenantId, scopeId, amount(Money), idempotencyKey }
// Wallet → Campaigns
FundsReserved | InsufficientFunds | FundsConsumed | FundsRefunded { scopeId, movementId }

// Campaigns → Ejecutor (uno por Recipient)
Dispatch { dispatchId, campaignRunId, recipientRef, attempt, channel, templateRef, variables,
           campaignId /*opaco*/ }
// Ejecutor → Campaigns
DispatchResult { dispatchId, campaignRunId, recipientRef, attempt,
                 status: Delivered|Failed|Bounced|Suppressed|ProviderNotConfigured,
                 providerMessageId?, reason?, campaignId /*eco intacto*/, eventAtUtc }
```

`amount` viaja como `Money(long, "USD")` — nunca `float`, nunca monto confiado por el frontend.

## 7. Multi-tenant en la saga

- Campaigns y Wallet aplican **query filter global fail-closed** + repos tenant-scoped.
- Los handlers Wolverine cruzan tenant con `.IgnoreQueryFilters()` **+ tenant explícito** en el scope (ver `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`), nunca `.Where` manual (anti-patrón legado).
- Endpoints M2M (Campaigns↔Wallet↔ejecutores) con **audience/scope** propios y `[RateLimit]`/`[RateLimitExempt]`; nunca JWT de usuario reenviado (corrige `BackgroundAuthToken`).
