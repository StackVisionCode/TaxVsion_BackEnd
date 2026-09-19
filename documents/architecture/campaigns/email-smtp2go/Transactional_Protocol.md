# Email (SMTP2GO) — Transactional Protocol

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — Email NO es un ejecutor dedicado nuevo; es un CONSUMER dentro del servicio EXISTENTE `Notification`** (reusa `SendEmailCommand` con el seam `CampaignId`; SMTP2GO es solo el proveedor que usa Notification). Campaign es un orquestador agnóstico que **no envía**: publica `campaign.dispatch.requested.v1` por destinatario y este consumer lo procesa (`ActorType.Service`) y responde `campaign.dispatch.result.v1`. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio dedicado `TaxVision.Campaigns.Email` y/o un Wallet queda **superseded** por esta nota. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- Componente: **Consumer/handler dentro del servicio EXISTENTE `Notification`** (SMTP2GO = proveedor que usa Notification); persistencia dentro de Notification.
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

Este consumer (dentro de Notification) procesa el `dispatch_requested` y emite result events, con atomicidad estado↔evento y sin TOCTOU. — (removido: participación en saga balance+dispatch; sin dinero en el canal; ver banner).

## 1. El problema del legado (a corregir)
- **Fan-out síncrono fire-and-forget**: `SendBatchAsync` iteraba en memoria con `Task.Delay` entre batches (`Smtp2GoService.cs:367-406`); al reiniciar el proceso se perdía el progreso y no había retomada.
- **Result no-atómico**: el result debe ser **exacto y atómico** con el cambio de estado del dispatch (outbox), para no perder ni duplicar la señal de entrega. — (removido: pago/consume/refund TOCTOU; sin dinero en el canal; ver banner).
- **Log tras el hecho, sin outbox**: el efecto (log) se guardaba fuera de transacción con el envío (`Smtp2GoService.cs:252-264`), tragándose errores.

## 2. Flujo local (happy path)
```
Wolverine inbox recibe campaign.dispatch.requested.v1
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
| Tras COMMIT paso 4, antes del POST | `Pending`, dedupe marcado | reentrega ⇒ dedupe hit ⇒ **NO re-POST**; un reconciliador barre `Pending` viejos y decide reintentar POST o clasificar como `Unknown` (reconcilable), ver §5 |
| Tras POST, antes de COMMIT paso 5 | `Pending` + email posiblemente enviado | **riesgo de doble envío** — mitigado por `IdempotencyKey` propagado a SMTP2GO (ver §4) |

## 4. Idempotencia hacia el proveedor (acotar —no eliminar— el doble envío en at-least-once)
**Límite real (#5):** outbox + UNIQUE `(run,recipient,attempt)` **NO** hacen atómico el borde "el proveedor aceptó pero la respuesta se perdió" (crash entre POST y COMMIT). **No se promete "cero envío duplicado externo"**; se mitiga, no se elimina. Mitigación:
- **Persistir un intento `Pending` ANTES del POST** (+ marcar `ProcessedBusinessMessage`) dentro de TX; ese estado permite retomar sin re-POSTear a ciegas.
- **Usar la idempotencia del proveedor cuando esté documentada.** ASSUMPTION a verificar (no un hecho): SMTP2GO podría **no** ofrecer dedupe fuerte por client-key en `email/send` — confirmar en B-EMAIL-TX-2. Si existe, enviar una client-key estable.
- **Identidad estable por intento**: header `X-Campaign-Dispatch-Id: {dispatchId}` en cada POST (correlación/auditoría), y `List-Unsubscribe`/one-click (conservado del legado, `Smtp2GoService.cs:541-548`).
- **Distinguir `Pending`/`Sending`/`Accepted`/`Unknown`** para no re-POSTear: un dispatch con `dedupe` marcado + `Pending`/posible envío NO se re-POSTea; el reconciliador **consulta estado** (por `provider_message_id`, webhook o stats) **antes** de reintentar.
- **Reconciliar antes de crear un nuevo intento**: nunca abrir un `Attempt` nuevo sin verificar el estado real del anterior.
- **Ventana residual aceptada**: en el peor caso (crash exacto entre POST y COMMIT) puede producirse 1 envío duplicado. Riesgo conocido, acotado por el reconciliador; nunca se promete exactly-once (regla de la suite).

## 5. Reconciliador (barrido de Pending huérfanos)
Job periódico (idempotente, con lease — ver `Concurrency_Spec.md`):
- Toma `email_dispatch` en `Pending` con `created_at_utc` > umbral.
- Si hay `provider_message_id` o webhook de delivery ⇒ transiciona.
- Si no hay evidencia de envío y venció el TTL ⇒ Outcome `Unknown` (reconcilable), **NO** `Failed`: un timeout no se colapsa a `Failed` (canónico); se sigue reconciliando / se escala, no se cierra como fallo definitivo.
- Nunca re-POST sin verificar (acota el doble envío del §3/§4).

## 6. Interacción con balance
— (removido: interacción con la saga de Wallet consume/refund; sin dinero en el canal; ver banner). Este consumer solo emite result events; cualquier autorización por balance es un interceptor/PEP externo y DIFERIDO, fuera de este canal.

## 7. Evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado fan-out en memoria con Task.Delay (se pierde al reiniciar) | `Smtp2GoService.cs:367-406` | VERIFIED | 96% |
| Legado log fuera de TX del envío | `Smtp2GoService.cs:252-264` | VERIFIED | 88% |
| `List-Unsubscribe` one-click a conservar | `Smtp2GoService.cs:541-548` | VERIFIED | 94% |
| Outbox atómico estado↔evento (Wolverine) | patrón suite / anchors | NEW | n/a |

## 8. BLOCKERS
- **B-EMAIL-TX-1**: — (removido: política de costeo consume/refund; sin dinero en el canal; ver banner). La autorización por balance es externa (interceptor/PEP + Wallet) y DIFERIDA.
- **B-EMAIL-TX-2**: confirmar si SMTP2GO ofrece alguna idempotencia por client-key en `email/send`; de no existir, el reconciliador (§5) es obligatorio para MVP.
