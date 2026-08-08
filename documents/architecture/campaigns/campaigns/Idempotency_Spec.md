# Campaigns — Idempotency Spec

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Mensajería at-least-once (Wolverine outbox/inbox durable). **Exactly-once no existe.** Toda operación con efecto es idempotente por diseño, en tres capas:

1. **Transporte:** inbox durable de Wolverine deduplica *envelopes* reentregados.
2. **Constraint de BD:** unique keys hacen que la segunda escritura del "mismo hecho" falle o sea no-op.
3. **Efecto de negocio:** `ProcessedBusinessMessage` (copia local del de Growth, `Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-23`) protege operaciones que no se pueden expresar como un solo constraint (p.ej. una llamada M2M con respuesta cacheable).

Las tres son necesarias: el inbox protege del redelivery del **mismo** mensaje; el constraint y `ProcessedBusinessMessage` protegen del **mismo efecto** llegando por mensajes distintos o rutas distintas.

---

## 1. Claves de idempotencia por operación

| Operación | Clave lógica | Mecanismo primario |
|---|---|---|
| Crear Campaign (API) | header `Idempotency-Key` + fingerprint del body | `ProcessedBusinessMessage(op="create_campaign", scope=tenant)` |
| Trigger / StartCampaignRun | `occurrence_key` = `campaignId:<instant o triggerId>` | `UNIQUE(tenant, campaign_id, occurrence_key)` en `campaign_run` |
| Wallet RESERVE | `(reserve, runId)` | `ProcessedBusinessMessage(op="reserve", scope=runId)` + Wallet-side |
| Dispatch por destinatario | `dispatch_idempotency_key` = `f(runId, recipientId, attemptNo)` | `UNIQUE(run_id, dispatch_idempotency_key)` en `campaign_recipient` |
| Registrar dispatch result | mismo `dispatch_idempotency_key` | guard de estado del recipient (transición monótona) |
| Tracking (open/click/bounce) | `(recipientId, providerEventId)` | dedupe + campos set-once |
| Wallet CONSUME | `(consume, runId)` | `ProcessedBusinessMessage(op="consume", scope=runId)` + Wallet-side |
| Wallet REFUND | `(refund, runId)` | idem |
| Reconcile / Complete | `run_status` guard set-once (`cost_actual` una vez) | guard de estado del run |

---

## 2. Creación de run idempotente

Dos entregas de `scheduler.run_due.v1` con el mismo `occurrenceKey`, o dos POST `trigger` con el mismo `Idempotency-Key`, deben producir **un** `campaign_run`. El insert compite sobre `UNIQUE(tenant, campaign_id, occurrence_key)`: el ganador crea el run, el perdedor recibe violación de unicidad y **devuelve el run existente** (no error). Corrige el doble-scheduler legado, que no tenía entidad de run y podía ejecutar la misma campaña dos veces (`CampaignSchedulerBackgroundService`, sin lease ni unique key).

---

## 3. Dispatch por destinatario (el corazón)

`dispatch_idempotency_key = hash(runId | recipientId | attemptNo)`. Propiedades:

- Estable para un `(run, recipient, attempt)` → el ejecutor puede deduplicar su lado.
- `UNIQUE(run_id, dispatch_idempotency_key)` → el fan-out nunca emite dos dispatch para el mismo recipient/attempt aunque el handler `DispatchRun` se reejecute (redelivery).
- Un reintento **legítimo** (el anterior falló) usa `attemptNo+1` → key nueva → dispatch nuevo, sin colisión.

Esto corrige el anti-patrón legado #3 (ADR-CAMP-000): el legado marcaba `Sent` a todos los no-fallidos en un solo `SaveChanges` (`CampaignSendService.cs:63-71`) sin clave por destinatario, así que un reintento del batch re-enviaba a todos.

---

## 4. Dispatch result idempotente (guard de estado)

`RecordDispatchResult` avanza `dispatch_state` solo si la transición es válida **y nueva**:

```
Dispatched --delivered--> Delivered   (primer delivered gana; segundo delivered = no-op)
Dispatched --failed-----> Failed
Delivered  --delivered--> (no-op, ya terminal)
Failed     --delivered--> (conflicto tardío: log + no-op; no revierte)
```

Como Delivered/Failed/Suppressed son **terminales**, un result duplicado o fuera de orden es no-op. El contador asociado incrementa **solo** en la transición efectiva (dentro de la misma tx), nunca en el no-op. Así el reintento de webhook no doble-cuenta (corrige `CampaignStatistics` sin dedupe del legado).

---

## 5. Tracking set-once

Open/click/bounce llegan por webhook del ejecutor, con reintentos y duplicados. Reglas:

- `first_open_at_utc` / `first_click_at_utc`: **set-once** (solo si NULL). El segundo open no cambia el timestamp.
- `open_count` / `click_count`: incrementan **solo** si `(recipientId, providerEventId)` no fue visto → dedupe por `ProcessedBusinessMessage(op="tracking")` o tabla `tracking_event_dedupe`. `providerEventId` es la clave que el ejecutor/proveedor garantiza única por evento físico.
- Un open sobre un recipient no-`Delivered` se ignora (defensa; no debería ocurrir).

Esto corrige el doble-conteo de `CampaignTrackingEvent` del legado en reintento de webhook (ADR-CAMP-000 §Anti-patrones #3).

---

## 6. `ProcessedBusinessMessage` (patrón)

Ciclo (idéntico al de Growth, `ProcessedBusinessMessage.cs:27-105`):

```
Begin(tenant, op, scopeId, idempotencyKey, requestFingerprint, now, expiresAt)  -> Processing
  ├─ Complete(statusCode, contentType, json, now)   -> Completed  (respuesta cacheada)
  └─ Fail(failureCode, now)                          -> Failed
```

- Reentrada con **mismo fingerprint** y estado `Completed` → devolver la respuesta cacheada (no re-ejecutar).
- Reentrada con **distinto fingerprint** sobre la misma `(op,scope,key)` → conflicto `409` (reuso de key con payload distinto), igual que la semántica HTTP Idempotency-Key.
- `request_fingerprint` = SHA-256 hex (64 chars) del body canónico — validado por el propio VO (`ProcessedBusinessMessage.cs:52-56`).
- `expires_at_utc` acota la ventana de dedupe (GC de filas viejas).

Se usa para: crear campaign (API), reserve/consume/refund (M2M a Wallet), y tracking dedupe.

---

## 7. Interacción con Wolverine inbox

El inbox durable ya deduplica el **mismo** envelope reentregado; `ProcessedBusinessMessage`/unique-constraints cubren el caso de **efecto duplicado por rutas distintas** (p.ej. un result que llega por webhook y por reconciliación, o dos `RunDue` distintos por misma occurrence). No se confía solo en el inbox — es defensa en profundidad exigida por CLAUDE.md ("nunca exactly-once; handlers idempotentes + unique constraints + state guards").

---

## 8. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| `ProcessedBusinessMessage` API (Begin/Complete/Fail, fingerprint SHA-256) | `Growth/.../ProcessedBusinessMessage.cs:27-105,52-56` | VERIFIED | 97% |
| Legado marca Sent a todos sin clave por destinatario | `CampaignSendService.cs:63-71` | VERIFIED | 97% |
| Legado sin dedupe de tracking (doble-cuenta) | ADR-CAMP-000 §Anti-patrones #3 | DOCUMENTED_ONLY | 90% |
| Legado sin entidad de run / sin unique de ocurrencia | `CampaignSchedulerBackgroundService.cs` (ausencia) | VERIFIED | 93% |
| Claves de idempotencia por operación | diseño (este doc §1) | NEW | 87% |
| Tracking set-once + dedupe providerEventId | diseño (este doc §5) | NEW | 86% |
