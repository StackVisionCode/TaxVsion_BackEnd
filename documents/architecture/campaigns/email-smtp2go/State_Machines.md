# Email (SMTP2GO) — State Machines

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — Email NO es un ejecutor dedicado nuevo; es un CONSUMER dentro del servicio EXISTENTE `Notification`** (reusa `SendEmailCommand` con el seam `CampaignId`; SMTP2GO es solo el proveedor que usa Notification). Campaign es un orquestador agnóstico que **no envía**: publica `campaign.dispatch.requested.v1` por destinatario y este consumer lo procesa (`ActorType.Service`) y responde `campaign.dispatch.result.v1`. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio dedicado `TaxVision.Campaigns.Email` y/o un Wallet queda **superseded** por esta nota. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- Componente: **Consumer/handler dentro del servicio EXISTENTE `Notification`** (SMTP2GO = proveedor que usa Notification); persistencia dentro de Notification.
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. EmailDispatch — máquina de estados

Una fila **inmutable en identidad** por `(CampaignRunId, RecipientId, Attempt)`. El estado avanza por métodos del aggregate con guardas; nunca se reusa una fila para otro intento (corrige el `Status=Sending` no-atómico y el "marcar Sent a todos" del legado, anti-patrones #6, #3).

```
                         (recibido dispatch, dedupe OK)
                                     │
                                     ▼
                                 [Pending]
                    ┌────────────────┼─────────────────┐
       suppression hit│      provider accept│        render/validación falla
                    ▼                │                 ▼
              [Suppressed]           │             [Failed] ── (terminal, no reintenta acá)
              (terminal)             ▼
                                 [Sent]  ── (SMTP2GO 200, email_id capturado)
                                     │
              ┌──────────────┬───────┼───────────────┐
     webhook delivered│  webhook bounce│      webhook spam-complaint
              ▼               ▼                 ▼
        [Delivered]      [Bounced]          [Complained]
        (terminal*)      (terminal)         (terminal)
```

`*` `Delivered` es "terminal happy path", pero un `Bounced`/`Complained` posterior sobre el mismo `ProviderMessageId` (raro pero posible con soft-bounce tardío) se aplica de forma **monótona** solo si mejora la severidad; ver reglas de webhook abajo.

### 1.1 Estados
| Estado | Semántica | ¿Terminal? |
|---|---|---|
| `Pending` | Aceptado del bus, dedupe OK, aún no despachado | No |
| `Suppressed` | Dirección en suppression list; **no se llamó a SMTP2GO** | Sí |
| `Sent` | SMTP2GO **aceptó/encoló** (HTTP 200, `data.succeeded>0`, `email_id`) — Outcome canónico = **Accepted**, NO es entrega confirmada | No |
| `Delivered` | Webhook `delivered` del proveedor (única confirmación real de entrega) | Sí |
| `Bounced` | Webhook bounce (hard/soft agotado) | Sí |
| `Complained` | Webhook spam complaint | Sí |
| `Failed` | Error pre-provider o rechazo definitivo del provider (4xx no-reintentable). Un **timeout NO es Failed**: es `Unknown` (reconcilable, ver §Reconciliador) | Sí |

> (removido: columna "Efecto en saga Wallet" y decisión de costeo — sin dinero en el canal; ver banner). Este consumer solo **reporta el hecho** vía `campaign.dispatch.result.v1` con `Outcome ∈ {Accepted, Delivered, Failed, Skipped, Unknown}` (mapeo estado→Outcome en `Commands_And_Events.md §3.0`); cualquier autorización por balance es un interceptor/PEP externo y DIFERIDO.

### 1.2 Transiciones válidas (guardas)
| Método | Origen permitido | Destino | Guarda |
|---|---|---|---|
| `MarkSuppressed(reason)` | `Pending` | `Suppressed` | address en SuppressionEntry |
| `MarkSent(providerMessageId)` | `Pending` | `Sent` | provider aceptó; setea `ProviderMessageId` |
| `MarkFailed(reason)` | `Pending` | `Failed` | error pre-provider o 4xx definitivo |
| `MarkDelivered()` | `Sent` | `Delivered` | webhook delivered idempotente |
| `MarkBounced(type,reason)` | `Sent`,`Delivered` | `Bounced` | webhook bounce; soft se ignora si aún reintenta el MTA |
| `MarkComplained()` | `Sent`,`Delivered` | `Complained` | webhook spam |

Toda transición desde un estado NO permitido devuelve `Result.Failure` **sin efecto** (idempotencia: reintento del mismo webhook = no-op, no doble-cuenta).

## 2. Reintentos de envío (transporte)
- Los reintentos de **transporte HTTP a SMTP2GO** (timeouts, 5xx) son responsabilidad del handler + Wolverine (backoff exponencial, jitter) **dentro del mismo `Attempt`**, no crean fila nueva.
- Un **nuevo `Attempt`** (fila nueva) solo lo origina Campaigns/Scheduler al reintentar el destinatario de un run (p.ej. tras un `Failed` recuperable). El ejecutor no se auto-reintenta creando runs.
- 4xx no-reintentables (dirección inválida, payload rechazado) ⇒ `Failed` inmediato, sin reintento HTTP (evita quemar rate del proveedor).

## 3. Bounce classification (SMTP2GO webhook)
```
bounce event ─┬─ hard / permanent  ─► MarkBounced(Hard) + upsert SuppressionEntry(HardBounce)
              └─ soft / transient   ─► si el MTA aún reintenta: NO transiciona (queda Sent)
                                        si agotó: MarkBounced(Soft) (sin suppression por defecto)
spam complaint ─► MarkComplained() + upsert SuppressionEntry(SpamComplaint)
unsubscribe    ─► upsert SuppressionEntry(Unsubscribe) (no cambia el dispatch histórico)
```

## 4. ProviderCredential — ciclo
```
[Draft] ── set FromDomain ──► [PendingVerification] ── DNS/SMTP2GO verify ──► [Active]
   └─ rotate key ─► KeyVersion++ (Active se mantiene)     [Active] ── deactivate ──► [Inactive]
```
Solo `Active` + `FromDomainVerified` habilita envío en scope `Tenant`. `System` scope es `Active` por config de plataforma.

## 5. Diferencia clave vs legado
El legado tenía **un** `Campaign.Status` global (`Draft→Scheduled→Sending→Sent/...`) que mezclaba definición, agenda y entrega en una fila, con `Sending` no-atómico. Acá el **estado de entrega por destinatario** vive en `EmailDispatch` (por intento), desacoplado del estado del `Campaign`/`CampaignRun` (que vive en Campaigns). Esto **reduce** el double-send al escalar y elimina el double-count en reintento. **No** garantiza "cero envío duplicado externo": el borde "proveedor aceptó pero la respuesta se perdió" NO es atómico con outbox+UNIQUE; ese residual se acota (no se elimina) con reconciliación — ver `Transactional_Protocol.md §3-5` e `Idempotency_Spec.md §3`.
