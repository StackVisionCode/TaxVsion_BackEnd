# Wallet/Ledger — State Machines

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- Ver `Domain_Design.md`, `Transactional_Protocol.md`, `06_Cross_Service_Transactional_Protocol.md`.

---

## 1. Máquina de estados de una **Reservation**

Corazón del protocolo `reserve → consume/refund`. Corrige el legado que **debitaba una sola vez al crear** sin estados intermedios (`CreateCampaignCommandHandler.cs:278-320`).

```
                 ┌──────────────────────────────────────────────┐
                 │                                              │
   Reserve       ▼        ConsumeReservation (parcial)          │
  (Available>=amt)   ┌─────────┐  consume < Remaining     ┌─────────────────┐
 ──────────────────► │  HELD   │ ───────────────────────► │  HELD (parcial) │
                     └────┬────┘                          └───────┬─────────┘
                          │  ConsumeReservation (total)           │ ConsumeReservation (resto)
                          │  consume == Remaining                 │ consume == Remaining
                          ▼                                       ▼
                     ┌──────────┐  ◄──────────────────────────────┘
                     │ CONSUMED │  (Remaining == 0; terminal)
                     └──────────┘
                          
   HELD / HELD(parcial) ── ReleaseReservation / RefundRemainder ──► ┌──────────┐
                                                                    │ RELEASED │ (terminal)
                                                                    └──────────┘
   HELD / HELD(parcial) ── expira (ExpiresAtUtc < now, sweep) ────► ┌──────────┐
                                                                    │ EXPIRED  │ (terminal)
```

### Estados

| Estado | Semántica | `Held` aporta | `Posted` afectado | Terminal |
|---|---|---|---|---|
| **Held** | Fondos apartados, nada consumido. `RemainingCents == AmountCents`. | sí (`Remaining`) | no | no |
| **Held (parcial)** | Consumo parcial ya aplicado; `0 < ConsumedCents < AmountCents`. | sí (`Remaining`) | sí (por lo consumido) | no |
| **Consumed** | `ConsumedCents == AmountCents`. Todo el reserve se gastó. | no | sí (total) | **sí** |
| **Released** | El remanente no consumido se devolvió a Available (cancelación / fin de run). | no | no (neto 0) | **sí** |
| **Expired** | Hold abandonado que superó `ExpiresAtUtc`; el remanente se libera automáticamente. | no | no | **sí** |

### Transiciones (guardas → `Result`)

| Desde | Evento | Guarda | Hacia | LedgerEntry emitido |
|---|---|---|---|---|
| (none) | `Reserve` | `Available >= amount`, balance Active | Held | `Reserve` (+Held) |
| Held / Held(p) | `Consume(c)` | `c > 0`, `c <= Remaining` | Held(p) si `c<Remaining`; Consumed si `c==Remaining` | `Consume` (−Posted, −Held) |
| Held / Held(p) | `Release` / `RefundRemainder` | reserva no terminal | Released | `Refund` (−Held, +Available; neto Posted 0) |
| Held / Held(p) | `expire` (sweep) | `ExpiresAtUtc < now` | Expired | `Refund` (−Held; motivo=expiry) |
| Consumed/Released/Expired | cualquiera | — | (rechazado) | — (idempotente: ver abajo) |

**Idempotencia de terminal:** reintentar `Consume`/`Release` sobre una reserva ya terminal NO falla ruidosamente: el ejecutor idempotente (`ProcessedBusinessMessage`) detecta la clave repetida y **replica la respuesta previa** (ver `Idempotency_Spec.md`). Reintentar `Consume` con una **clave nueva** sobre una reserva Consumed → `Result.Failure(Wallet.ReservationNotConsumable)`.

**Consumo total ≠ suma exacta:** si la entrega real cuesta menos que lo reservado (p.ej. destinatarios que fallan pre-envío y no se cobran), el run hace `Consume(realCost)` y luego `Release`/`RefundRemainder` del sobrante. La reserva queda **Released tras consumo parcial** (variante de Released, no Consumed).

## 2. Máquina de estados del **TenantBalance**

```
        crear (primer Recharge o Reserve)
   ────────────────────────────────────────►  ┌──────────┐
                                               │  ACTIVE  │◄────┐ Unfreeze (admin)
                                               └────┬─────┘     │
                                     Freeze (admin) │           │
                                                    ▼           │
                                               ┌──────────┐─────┘
                                               │  FROZEN  │
                                               └──────────┘
```

| Estado | Recharge | Reserve | Consume | Release/Refund | Adjust |
|---|---|---|---|---|---|
| **Active** | ✔ | ✔ | ✔ | ✔ | ✔ |
| **Frozen** | ✖ (rechaza) | ✖ (rechaza) | ✔ (permite cerrar reservas ya vivas) | ✔ | ✔ (admin) |

`Frozen` es una salvaguarda operativa (fraude/dispute). No borra saldo ni reservas; solo bloquea nuevas recargas y reservas. Consume/Release siguen permitidos para **cerrar** operaciones en vuelo sin dejar holds colgados.

## 3. Interacción con la saga de dispatch (visión externa)

Alineado con `06_Cross_Service_Transactional_Protocol.md`. Wallet solo ve reserve/consume/refund; no conoce campaigns.

```
Campaigns: run start ─ reserve(estCost, scope=RunId, key=run-reserve-{RunId}) ──► HELD
   fan-out por destinatario (ejecutores entregan)
Campaigns agrega resultados ─────────────────────────────────────────────────────►
   consume(sum(delivered unit prices), key=run-consume-{RunId})  ──► HELD(parcial)/CONSUMED
   refundRemainder(key=run-refund-{RunId})                       ──► RELEASED
```

Nota: la política de "un consume total al cierre" vs "consume incremental por lote" la decide Campaigns; Wallet soporta ambas (Consume es acumulativo e idempotente por clave).

## 4. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado no tiene estados de reserva (débito único al crear) | `CreateCampaignCommandHandler.cs:278-320` | VERIFIED | 95% |
| `Status=Sending` no-atómico y doble-scheduler en legado (motiva estados atómicos) | `05_Master_ADR.md:49` | DOCUMENTED_ONLY | 85% |
| Máquina Held→Consumed/Released/Expired | diseño | NEW | n/a |
| Freeze/Unfreeze como salvaguarda | diseño | NEW | n/a |
