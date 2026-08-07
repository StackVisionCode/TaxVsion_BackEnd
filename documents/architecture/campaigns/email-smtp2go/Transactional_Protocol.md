# Email (SMTP2GO) — Transactional Protocol

- Servicio: **TaxVision.Campaigns.Email**
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

Este servicio es un **participante** de la saga balance+dispatch definida en `../06_Cross_Service_Transactional_Protocol.md`. Aquí se detalla su parte local: del `dispatch_requested` a los result events, con atomicidad estado↔evento y sin TOCTOU.

## 1. El problema del legado (a corregir)
- **Fan-out síncrono fire-and-forget**: `SendBatchAsync` iteraba en memoria con `Task.Delay` entre batches (`Smtp2GoService.cs:367-406`); al reiniciar el proceso se perdía el progreso y no había retomada.
- **Pago no-atómico / TOCTOU** (a nivel suite): check+debit en 2 HTTP calls antes de `SaveChanges` (anti-patrón #4). El ejecutor de email no cobra, pero su result **es la señal** que dispara consume/refund, así que debe ser **exacto y atómico** con el cambio de estado.
- **Log tras el hecho, sin outbox**: el efecto (log) se guardaba fuera de transacción con el envío (`Smtp2GoService.cs:252-264`), tragándose errores.

## 2. Flujo local (happy path)
```
Wolverine inbox recibe campaigns.email.dispatch_requested.v1
  │  (scope tenant explícito seteado en el handler)
  ▼
BEGIN TX
  1. dedupe: ProcessedBusinessMessage(handler="ProcessEmailDispatch", key=IdempotencyKey)
       └─ ya procesado ⇒ COMMIT no-op (idempotente) y return
  2. UPSERT email_dispatch(Pending)  [unique (run,recipient,attempt) atrapa duplicado]
  3. suppression check (tenant, to_address)
       └─ hit ⇒ MarkSuppressed(); outbox: dispatch.suppressed.v1; COMMIT; return
  4. render (Scribe) si el cuerpo no viajó
COMMIT TX  (dispatch en Pending, dedupe marcado)
  │
  ▼  (fuera de TX — llamada de red)
POST https://api.smtp2go.com/v3/email/send   (con retry HTTP 5xx/timeout, MISMO attempt)
  │
  ▼
BEGIN TX
  5. éxito (200, data.succeeded>0) ⇒ MarkSent(email_id); outbox: dispatch.sent.v1
     4xx definitivo / succeeded=0   ⇒ MarkFailed(reason); outbox: dispatch.failed.v1
COMMIT TX  (estado ↔ evento atómicos vía outbox Wolverine)
```

Puntos clave:
- **La llamada HTTP a SMTP2GO ocurre fuera de la transacción de BD** (nunca mantener una TX abierta durante I/O de red). El estado `Pending` persistido antes del POST es el que permite retomar tras crash.
- **Atomicidad estado↔evento**: `MarkSent`/`MarkFailed` y el result event se persisten en la **misma** TX vía la outbox durable de Wolverine. No hay ventana donde el estado cambie pero el evento se pierda (ni viceversa).
- El scope de tenant se fija explícito en el handler (no ambient) — ver `Guia_IgnoreQueryFilters...`.

## 3. Crash recovery (retomada)
| Crash en | Estado en BD | Recuperación |
|---|---|---|
| Antes de paso 2 | nada | reentrega del bus recrea desde cero (dedupe aún no marcado) |
| Tras COMMIT paso 4, antes del POST | `Pending`, dedupe marcado | reentrega ⇒ dedupe hit ⇒ **NO re-POST**; un reconciliador barre `Pending` viejos y decide reintentar POST o marcar `Failed` (ver §5) |
| Tras POST, antes de COMMIT paso 5 | `Pending` + email posiblemente enviado | **riesgo de doble envío** — mitigado por `IdempotencyKey` propagado a SMTP2GO (ver §4) |

## 4. Idempotencia hacia el proveedor (evitar doble envío en at-least-once)
- SMTP2GO no garantiza dedupe fuerte por key de cliente en `email/send`, así que la defensa primaria es **no re-POSTear** un dispatch cuyo `dedupe` ya está marcado + `Pending` con posible envío: el reconciliador **consulta estado** (por `provider_message_id` si existe, o vía webhook/stats) antes de reintentar.
- Se incluye un header `X-Campaign-Dispatch-Id: {dispatchId}` en el envío (correlación estable), y `List-Unsubscribe`/one-click (conservado del legado, `Smtp2GoService.cs:541-548`).
- **Ventana residual aceptada**: at-least-once puede, en el peor caso (crash exacto entre POST y COMMIT), producir un envío duplicado. Se documenta como riesgo conocido, acotado por el reconciliador; nunca se promete exactly-once (regla de la suite).

## 5. Reconciliador (barrido de Pending huérfanos)
Job periódico (idempotente, con lease — ver `Concurrency_Spec.md`):
- Toma `email_dispatch` en `Pending` con `created_at_utc` > umbral.
- Si hay `provider_message_id` o webhook de delivery ⇒ transiciona.
- Si no hay evidencia de envío y venció el TTL ⇒ `MarkFailed("provider-timeout")` (dispara refund).
- Nunca re-POST sin verificar (evita el doble envío del §3/§4).

## 6. Interacción con la saga de Wallet (resumen)
Este servicio **no** llama a Wallet. Emite result; la saga en Campaigns (`../06_...`) traduce:
- `suppressed`/`failed` ⇒ Wallet **refund** (unidad no consumida).
- `sent`/`delivered` ⇒ Wallet **consume**.
Solo Wallet muta saldo, por movimientos inmutables (regla dura de la suite).

## 7. Evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado fan-out en memoria con Task.Delay (se pierde al reiniciar) | `Smtp2GoService.cs:367-406` | VERIFIED | 96% |
| Legado log fuera de TX del envío | `Smtp2GoService.cs:252-264` | VERIFIED | 88% |
| `List-Unsubscribe` one-click a conservar | `Smtp2GoService.cs:541-548` | VERIFIED | 94% |
| Outbox atómico estado↔evento (Wolverine) | patrón suite / anchors | NEW | n/a |
| Semántica consume/refund | `../06_...` (pendiente) | DOCUMENTED_ONLY | 70% |

## 8. BLOCKERS
- **B-EMAIL-TX-1**: la política exacta de costeo (consume en `sent` vs `delivered`, refund en bounce) debe fijarse en `../06_...` + `wallet-ledger/` antes de implementar los handlers de result. Hoy `DOCUMENTED_ONLY`.
- **B-EMAIL-TX-2**: confirmar si SMTP2GO ofrece alguna idempotencia por client-key en `email/send`; de no existir, el reconciliador (§5) es obligatorio para MVP.
