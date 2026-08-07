# TaxVision.Sms — State Machines

- **Servicio:** SMS (`TaxVision.Sms`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

## 1. `SmsDispatch.Status`

Estados: `Quoted → Reserved → Dispatched → Accepted → {Delivered | Failed | Suppressed | Undeliverable}`.
Cada transición es un **método del aggregate que devuelve `Result`** (convención de la casa) con guard explícito; no hay UPDATE suelto de estado.

```
                         (opt-in / STOP gate falla)
                     ┌─────────────────────────────► Suppressed (terminal)
                     │
 [create] ──► Quoted ─┴─► Reserved ──► Dispatched ──► Accepted ──► Delivered (terminal)
                │ Quote()   │ Reserve()  │ send()       │ webhook      │ webhook DLR OK
                │           │            │ (provider)   │ 2xx/queued   │
                │           │            │              │              └─► Failed (terminal, DLR error)
                │           │            │              └─► Failed (provider 4xx/5xx no-retryable)
                │           │            └─► Failed (excepción; Wallet refund)
                │           └─► Failed (reserva Wallet denegada: saldo insuficiente)
                └─► Suppressed (número Blocked / marketing sin opt-in)
```

| Transición | Guard | Efecto lateral |
|---|---|---|
| `→ Quoted` | cuerpo renderizado disponible | calcula `Encoding`, `Segments`, `CostQuote` |
| `Quoted → Suppressed` | STOP/Blocked o marketing sin opt-in | reporta `SmsDispatchSuppressed`; **sin** cargo Wallet |
| `Quoted → Reserved` | `SmsWalletReserved` recibido | fija `ReservationId` |
| `Quoted/Reserved → Failed` | reserva denegada (saldo) | reporta `SmsDispatchFailed(reason=insufficient_balance)`; nada que refundear |
| `Reserved → Dispatched` | proveedor aceptó la request HTTP | fija intento saliente |
| `Dispatched → Accepted` | proveedor devolvió `ProviderMessageId`/queued | persiste `ProviderMessageId` |
| `Dispatched → Failed` | proveedor rechazó (no-retryable) o excepción | **Wallet Refund** de la reserva |
| `Accepted → Delivered` | webhook DLR = delivered | **Wallet Consume** por segmentos reales |
| `Accepted → Failed` | webhook DLR = undelivered/expired/rejected | **Wallet Refund** |
| `Accepted → Undeliverable` | DLR carrier permanente (número inválido) | **Wallet Refund** + marca número `Blocked` |

**Regla de dinero:** se **reserva** al pasar a `Reserved`; se **consume** sólo en `Delivered`; se **refunda** en cualquier terminal fallido tras la reserva. Corrige el TOCTOU del legado (cobro al crear, antes de `SaveChanges`, ADR-CAMP-000 §Anti-patrón 4). Ver `Transactional_Protocol.md`.

### Reintentos
Un reintento **no** revive un `SmsDispatch` terminal: crea un **nuevo** `SmsDispatch` con `Attempt+1` y su propia clave de idempotencia. El legado mutaba `RetryCount` sobre la misma fila (`SmsSendLog.cs:59`); aquí cada intento es auditable e idempotente por separado. Sólo `Failed` retryable (timeout/5xx/red) es elegible; `Suppressed`/`Undeliverable` no se reintentan nunca.

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
- Las transiciones de Wallet-driven (`Reserved`/`Delivered`/`Failed`) son idempotentes por `ProcessedBusinessMessage` (`Idempotency_Spec.md`): recibir dos veces el mismo webhook no doble-consume ni doble-refunda (corrige el doble-conteo de tracking del legado, ADR-CAMP-000 §Anti-patrón 3).

## 4. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado muta `RetryCount` en la misma fila | `SmsSendLog.cs:59-61` | VERIFIED | 97% |
| Legado modela STOP como tipo de mensaje entrante | `SmsIncomingMessage.cs:79-89` | VERIFIED | 95% |
| Legado cobra al crear (TOCTOU) | ADR-CAMP-000 §Anti-patrón 4 | VERIFIED | 95% |
| Máquinas de estado dispatch/opt-in propuestas | este documento | NEW | — |
