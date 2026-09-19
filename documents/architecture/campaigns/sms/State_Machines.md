# TaxVision.Sms — State Machines

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — SMS NO es un servicio nuevo; `TaxVision.Sms` YA EXISTE y ya es M2M** (`SendSmsBatchCommand`, `POST /sms/messages`, `ActorType.Service`). El canal SMS es un **CONSUMER dentro de `TaxVision.Sms`** que procesa `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio SMS nuevo y/o un Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- **Servicio:** SMS (`TaxVision.Sms`) — consumer del canal SMS **dentro de `TaxVision.Sms` (ya existente)**
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

## 1. `SmsDispatch.Status`

Estados: `Segmented → Dispatched → Accepted → {Delivered | Failed | Suppressed | Undeliverable}`. El `result` hacia Campaigns usa el enum canónico `Outcome = Accepted | Delivered | Failed | Skipped | Unknown`:
- proveedor **acepta** la request (2xx/queued) ⇒ result `Accepted` (ya no es sólo fase interna: se reporta);
- DLR de entrega del carrier ⇒ `Delivered`;
- DLR de fallo permanente / rechazo no-retryable ⇒ `Failed`;
- `Suppressed` (opt-out/STOP/blocked) ⇒ `Skipped`;
- **timeout** (envío sin respuesta, o `Accepted` sin DLR tras TTL) ⇒ `Unknown` (**nunca** `Failed`).

Cada transición es un **método del aggregate que devuelve `Result`** (convención de la casa) con guard explícito; no hay UPDATE suelto de estado. — (removido: sin dinero en el canal; ver banner — antes existía un estado `Reserved` ligado a Wallet).

**Alias SMS-local → evento canónico** (los nombres `SmsDispatch*` son aliases internos; el evento en el bus es siempre `campaign.dispatch.result.v1` con el `Outcome` indicado):

| Alias SMS-local | Evento canónico | `Outcome` |
|---|---|---|
| `SmsDispatchAccepted` | `campaign.dispatch.result.v1` | `Accepted` |
| `SmsDispatchDelivered` | `campaign.dispatch.result.v1` | `Delivered` |
| `SmsDispatchFailed` | `campaign.dispatch.result.v1` | `Failed` |
| `SmsDispatchSuppressed` | `campaign.dispatch.result.v1` | `Skipped` |
| `SmsDispatchUnknown` (timeout/DLR ausente) | `campaign.dispatch.result.v1` | `Unknown` |

```
                         (opt-in / STOP gate falla)
                     ┌─────────────────────────────► Suppressed (terminal ⇒ Outcome=Skipped)
                     │
 [create] ──► Segmented ──► Dispatched ──► Accepted ──► Delivered (terminal)
                │ Segment()   │ send()       │ webhook      │ webhook DLR OK
                │             │ (provider)   │ 2xx/queued   │
                │             │              │              └─► Failed (terminal, DLR error)
                │             │              └─► Failed (provider 4xx/5xx no-retryable)
                │             └─► Failed (excepción)
                └─► Suppressed (número Blocked / marketing sin opt-in)
```

| Transición | Guard | Efecto lateral |
|---|---|---|
| `→ Segmented` | cuerpo renderizado disponible | calcula `Encoding`, `Segments` |
| `Segmented → Suppressed` | STOP/Blocked o marketing sin opt-in | reporta `SmsDispatchSuppressed` (Outcome=Skipped) |
| `Segmented → Dispatched` | proveedor aceptó la request HTTP | fija intento saliente |
| `Dispatched → Accepted` | proveedor devolvió `ProviderMessageId`/queued | persiste `ProviderMessageId` |
| `Dispatched → Failed` | proveedor rechazó (4xx/5xx no-retryable) | reporta `SmsDispatchFailed` (`Outcome=Failed`) |
| `Dispatched → (sin transición terminal)` | **timeout**/sin respuesta al enviar | reporta `Outcome=Unknown` (no `Failed`); queda pendiente de reconciliación (`Transactional_Protocol.md` §5) |
| `Accepted → Delivered` | webhook DLR = delivered | reporta `SmsDispatchDelivered` (`Outcome=Delivered`) |
| `Accepted → Failed` | webhook DLR = undelivered/expired/rejected | reporta `SmsDispatchFailed` (`Outcome=Failed`) |
| `Accepted → (sin transición terminal)` | **DLR ausente tras TTL** (timeout) | reporta `Outcome=Unknown` (no `Failed`); reconciliación decide |
| `Accepted → Undeliverable` | DLR carrier permanente (número inválido) | marca número `Blocked` |

**Regla de estado:** — (removido: sin dinero en el canal; ver banner — antes reserve/consume/refund contra Wallet). La autorización por balance es un interceptor/PEP externo y DIFERIDO. Ver `Transactional_Protocol.md`.

### Reintentos
Un reintento **no** revive un `SmsDispatch` terminal: crea un **nuevo** `SmsDispatch` con `Attempt+1`, mismo `RecipientId` estable y su propia clave de idempotencia (nueva fila `CampaignDispatchAttempt`, `UNIQUE(run_id, recipient_id, attempt_no)`). El legado mutaba `RetryCount` sobre la misma fila (`SmsSendLog.cs:59`); aquí cada intento es auditable e idempotente por separado. Elegibles para reintento: `Failed` retryable (5xx/red no-timeout) y **`Unknown` por timeout** (envío sin respuesta o `Accepted` sin DLR tras TTL — se reporta `Outcome=Unknown`, nunca `Failed`); `Suppressed`/`Undeliverable` y `Delivered` no se reintentan nunca.

**Opt-out después de congelar la audiencia:** un STOP recibido cuando la audiencia ya está congelada pero la unidad aún **no** se ha enviado igual la detiene: el guard de opt-out se **revalida en/adyacente a `Segmented→Dispatched`** ⇒ `Suppressed` (`Outcome=Skipped`). La supresión de Campaign consume el optout de `TaxVision.Sms` como fuente única.

## 2. `SmsOptInRegistry.OptInState`

Estados: `Pending → Subscribed → StoppedByUser → (Resubscribed) → Subscribed`; `Unsubscribed`; `Blocked`.

```
 [seen] ──► Pending ──► Subscribed ◄────────┐
              │            │ (STOP inbound)  │ (START/UNSTOP inbound, con opt-in previo)
              │            ▼                 │
              │        StoppedByUser ────────┘
              │            │
              ▼            ▼
        Unsubscribed    Blocked (carrier hard-reject; solo admin/soporte revierte)
```

| Evento | Transición | Notas |
|---|---|---|
| Import con `HasPriorConsent` | `Pending → Subscribed` | requiere prueba de consentimiento (auditoría) |
| Doble opt-in confirmado | `Pending → Subscribed` | recomendado para marketing |
| Inbound `STOP`/`CANCEL`/`END`/`QUIT`/`UNSUBSCRIBE` | `* → StoppedByUser` | **idempotente**; corta marketing y transactional |
| Inbound `START`/`UNSTOP`/`YES` | `StoppedByUser → Subscribed` | sólo si hubo opt-in previo |
| Inbound `HELP` | sin cambio de estado | responde plantilla HELP |
| DLR carrier "unknown subscriber" permanente | `* → Blocked` | evita reintentos costosos |

STOP se procesa vía webhook inbound (ver `API_Contracts.md`) y es **idempotente** (`ProcessedBusinessMessage`). El legado modelaba esto con `SystemMessageType.Stop` sobre `SmsIncomingMessage` (`SmsIncomingMessage.cs:79-89`) sin dedupe.

## 3. Concurrencia de estado
- `SmsDispatch` y `SmsOptInRegistry` llevan `RowVersion` (optimistic concurrency). Un webhook DLR y un reintento no pueden pisar el estado sin detectar el conflicto. Ver `Concurrency_Spec.md`.
- Las transiciones terminales (`Delivered`/`Failed`) son idempotentes por `ProcessedBusinessMessage` (`Idempotency_Spec.md`): recibir dos veces el mismo webhook no produce doble efecto (corrige el doble-conteo de tracking del legado, ADR-CAMP-000 §Anti-patrón 3). — (removido: sin dinero en el canal; ver banner — antes consume/refund de Wallet).

## 4. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado muta `RetryCount` en la misma fila | `SmsSendLog.cs:59-61` | VERIFIED | 97% |
| Legado modela STOP como tipo de mensaje entrante | `SmsIncomingMessage.cs:79-89` | VERIFIED | 95% |
| Máquinas de estado dispatch/opt-in propuestas | este documento | NEW | — |
