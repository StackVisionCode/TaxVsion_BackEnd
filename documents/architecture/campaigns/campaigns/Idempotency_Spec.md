# Campaigns — Idempotency Spec

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-16 (revisión: **sin claves de dinero**)
- **Estado:** DISEÑO — no implementado

Mensajería at-least-once (Wolverine outbox/inbox durable). **Exactly-once no existe.** Toda operación con efecto es idempotente por diseño, en tres capas:

1. **Transporte:** inbox durable de Wolverine deduplica *envelopes* reentregados.
2. **Constraint de BD:** unique keys hacen que la segunda escritura del "mismo hecho" falle o sea no-op.
3. **Efecto de negocio:** `ProcessedBusinessMessage` (copia local del de Growth, `Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-23`) protege operaciones que no se pueden expresar como un solo constraint (p.ej. crear campaign vía API con respuesta cacheable).

Las tres son necesarias: el inbox protege del redelivery del **mismo** mensaje; el constraint y `ProcessedBusinessMessage` protegen del **mismo efecto** llegando por mensajes distintos o rutas distintas.

> **Regla dura:** Campaign no tiene operaciones monetarias, así que **ninguna clave de idempotencia aquí es de reserve/consume/refund**. Las únicas operaciones idempotentes son de definición, creación de run, dispatch por destinatario y su result.

---

## 1. Claves de idempotencia por operación

| Operación | Clave lógica | Mecanismo primario |
|---|---|---|
| Crear/editar Campaign (API) | header `Idempotency-Key` + fingerprint del body | `ProcessedBusinessMessage(op="write_campaign", scope=tenant)` |
| Import de contactos (API) | `Idempotency-Key` + fingerprint del batch | `ProcessedBusinessMessage(op="import_contacts", scope=tenant)` |
| Trigger / StartCampaignRun | `occurrence_key` = `campaignId:<instant o triggerId>` | `UNIQUE(tenant, campaign_id, occurrence_key)` en `campaign_run` |
| Dispatch por destinatario | `dispatch_id` = `f(runId, contactRef, channel, attemptNo)` | `UNIQUE(run_id, dispatch_id)` en `campaign_recipient` |
| Registrar dispatch result | mismo `dispatch_id` | guard de estado del recipient (transición monótona) |
| Cierre / Complete | `run_status` guard set-once | guard de estado del run |

---

## 2. Creación de run idempotente

Dos entregas de `scheduler.run_due.v1` con el mismo `occurrenceKey`, o dos POST `trigger` con el mismo `Idempotency-Key`, deben producir **un** `campaign_run`. El insert compite sobre `UNIQUE(tenant, campaign_id, occurrence_key)`: el ganador crea el run, el perdedor recibe violación de unicidad y **devuelve el run existente** (no error). Corrige el doble-scheduler legado, que no tenía entidad de run y podía ejecutar la misma campaña dos veces (`CampaignSchedulerBackgroundService`, sin lease ni unique key).

---

## 3. Dispatch por destinatario (el corazón)

`dispatch_id = hash(runId | contactRef | channel | attemptNo)`. Propiedades:

- Estable para un `(run, recipient, channel, attempt)` → el ejecutor puede deduplicar su lado y devolverlo intacto en el result (patrón `CampaignId` de `PostmasterEmailEvents.cs:37,104`).
- `UNIQUE(run_id, dispatch_id)` → el fan-out nunca emite dos dispatch para el mismo recipient/canal/attempt aunque el handler `DispatchRun` se reejecute (redelivery).
- Un reintento **legítimo** (el anterior falló) usa `attemptNo+1` → key nueva → dispatch nuevo, sin colisión.

Esto corrige el anti-patrón legado #3 (ADR-CAMP-000): el legado marcaba `Sent` a todos los no-fallidos en un solo `SaveChanges` (`CampaignSendService.cs:63-71`) sin clave por destinatario, así que un reintento del batch re-enviaba a todos.

---

## 4. Dispatch result idempotente (guard de estado) — v2 con Accepted/Unknown

`RecordDispatchResult` se correlaciona por el `dispatch_id` **del intento** (no de la unidad) y avanza `dispatch_state` solo si la transición es válida **y nueva**:

```
Dispatched --accepted---> Accepted     (proveedor aceptó; aún no confirmado)
Dispatched --delivered--> Delivered
Dispatched --failed-----> Failed
Accepted   --delivered--> Delivered    (reconciliación por webhook; delivered++, accepted--)
Accepted   --failed-----> Failed
(deadline) Dispatched --> Unknown      (sweeper; ver Concurrency §7)
Unknown    --delivered--> Delivered    (reconciliación tardía, auditada, sin doble conteo)
Unknown    --failed-----> Failed
Delivered  --*---------> (no-op, terminal)
Failed     --delivered--> (conflicto tardío: log + no-op; no revierte a la fuerza)
```

Claves: (a) un `200 OK`/"encolado" del proveedor es **Accepted**, no Delivered (fix #03); (b) un result que llega para un `attempt_no` **anterior** cuando ya existe uno posterior se registra en su intento pero **no** pisa el outcome de la unidad si el intento vigente es otro (fix #09); (c) el contador incrementa solo en la transición efectiva, nunca en el no-op (corrige el doble-conteo del legado).

---

## 5. `ProcessedBusinessMessage` (patrón)

Ciclo (idéntico al de Growth, `ProcessedBusinessMessage.cs:27-105`):

```
Begin(tenant, op, scopeId, idempotencyKey, requestFingerprint, now, expiresAt)  -> Processing
  ├─ Complete(statusCode, contentType, json, now)   -> Completed  (respuesta cacheada)
  └─ Fail(failureCode, now)                          -> Failed
```

- Reentrada con **mismo fingerprint** y estado `Completed` → devolver la respuesta cacheada (no re-ejecutar).
- Reentrada con **distinto fingerprint** sobre la misma `(op,scope,key)` → conflicto `409` (reuso de key con payload distinto), igual que la semántica HTTP Idempotency-Key.
- `request_fingerprint` = SHA-256 hex (64 chars) del body canónico — validado por el propio VO (`ProcessedBusinessMessage.cs:52-56`).
- `expires_at_utc` acota la ventana de dedupe. **El TTL cubre la ventana real de replay + la política de repetición del usuario** (no se elige solo por limpieza de BD, fix #23).

Se usa para: crear/editar campaign, import de contactos, y cualquier comando de escritura de la API con `Idempotency-Key`. **No** para dinero (no existe en Campaign).

### 6.1 Recuperación de `Processing` y concurrencia (fix #23)

- **Solicitud concurrente con la misma clave mientras está `Processing`:** responde `409 Conflict` (o `425 Too Early`) con `Retry-After`; **no** ejecuta un segundo efecto. La primera en tomar la fila (insert que gana el `UNIQUE`) es la que procesa.
- **`Processing` abandonado (crash del handler):** una fila `Processing` con `updated_at` anterior a un `processing_lease_timeout` se considera huérfana; un barredor la marca `Failed(stale)` **o** la rehabilita para reintento **solo si** la operación es segura de reejecutar (idempotente aguas abajo). No se deja `Processing` colgado indefinidamente bloqueando la clave.
- **Captura del conflicto `UNIQUE` en PostgreSQL:** el "insert-gana / pierde → devuelve lo existente" se implementa capturando la violación **dentro de una subtransacción/`SAVEPOINT`** (o `INSERT ... ON CONFLICT`), porque un error aborta la transacción actual; no basta "capturar y devolver" sin cuidar el estado transaccional.
- **Alcance por endpoint:** cada endpoint declara qué recurso/método cubre la clave, el fingerprint canónico y el TTL (ver `API_Contracts.md`).

---

## 6. Interacción con Wolverine inbox

El inbox durable ya deduplica el **mismo** envelope reentregado; `ProcessedBusinessMessage`/unique-constraints cubren el caso de **efecto duplicado por rutas distintas** (p.ej. un result que llega por dos entregas, o dos `RunDue` distintos por misma occurrence). No se confía solo en el inbox — es defensa en profundidad exigida por CLAUDE.md ("nunca exactly-once; handlers idempotentes + unique constraints + state guards").

---

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| `ProcessedBusinessMessage` API (Begin/Complete/Fail, fingerprint SHA-256) | `Growth/.../ProcessedBusinessMessage.cs:27-105,52-56` | VERIFIED | 97% |
| Legado marca Sent a todos sin clave por destinatario | `CampaignSendService.cs:63-71` | VERIFIED | 97% |
| Correlación opaca devuelta por el ejecutor (modelo `dispatch_id`) | `PostmasterEmailEvents.cs:37,104` | VERIFIED | 95% |
| Legado sin entidad de run / sin unique de ocurrencia | `CampaignSchedulerBackgroundService.cs` (ausencia) | VERIFIED | 93% |
| Claves de idempotencia sin dinero | ADR-CAMP-001 D1 (decisión del usuario) | DECISION | 99% |
