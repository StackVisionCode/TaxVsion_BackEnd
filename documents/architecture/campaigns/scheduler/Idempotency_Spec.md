# Scheduler — Idempotency Spec

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

Base: **at-least-once + handlers idempotentes**, nunca exactly-once. Tres niveles de dedupe, cada uno con su clave. La corrección estructural es que **la ocurrencia de disparo es una entidad con identidad estable** (`TriggerOccurrence.Id`), lo que convierte "disparé esta campaña" en una clave persistente — algo que el legado no tenía (marcaba `Sent` a todos los no-fallidos y reseteaba la misma fila en cada recurrencia).

## 1. Tres claves de idempotencia

| Nivel | Operación | Clave | Almacén | Colisión |
|---|---|---|---|---|
| **API** | `Schedule` / `Reschedule` | `(tenant, op="schedule", scope=CampaignId, IdempotencyKey)` | `processed_business_messages` | mismo fingerprint → devuelve resultado; distinto → `409 IdempotencyConflict` |
| **Disparo** | `Fire` → `StartCampaignRun` | `OccurrenceId` (= `trigger_occurrences.id`) | la propia fila (estado terminal `Fired`) | ya `Fired` → no-op |
| **Materialización** | `MaterializeNext` | `(schedule_entry_id, sequence_no)` | UNIQUE constraint | duplicado → insert falla → no-op seguro |

## 2. Nivel API — `ProcessedBusinessMessage`

Reutiliza el patrón business-inbox (copia por contexto; origen `ProcessedBusinessMessage.cs`). Flujo:
1. `Begin(tenant, "schedule", CampaignId, key, fingerprint=SHA256(payload canónico), expiresAt)` → estado `Processing`.
2. Ejecuta TX-A (crear entry + 1ª ocurrencia).
3. `Complete(200, json=ScheduleHandle)`.
Reintento con misma `key`:
- mismo `fingerprint` y `Completed` → devuelve `ResponseJson` sin re-ejecutar (`HasSameFingerprint`, `ProcessedBusinessMessage.cs:107`).
- distinto `fingerprint` → `409` (misma key para payload distinto = error del cliente).
- estado `Processing` (concurrente) → responder "en curso"/reintentar; el UNIQUE `(tenant,op,scope,key)` serializa.

**Regla de key:** `IdempotencyKey = "schedule:{campaignId:N}:{clientToken}"`. Reagendar la **misma** campaña con nueva intención usa nuevo `clientToken` (es una operación nueva, no un retry).

## 3. Nivel disparo — la ocurrencia como token

El `Fire` (TX-C) marca `status=Fired` con guarda `WHERE status=Leased AND lease_owner=@me AND lease_until>now`. Propiedades:
- **Idempotente por construcción:** un segundo intento sobre una fila ya `Fired` tiene `rowcount=0` → no publica segundo `StartCampaignRun`.
- **`OccurrenceId` viaja en `StartCampaignRun`** y Campaigns lo usa como idempotency key del `CampaignRun`. Así, aunque Wolverine reentregue el comando (at-least-once), Campaigns crea **un solo** run. La cadena de idempotencia es: `TriggerOccurrence.Id` → `StartCampaignRun.OccurrenceId` → `CampaignRun` (uno por ocurrencia).
- Corrige el legado: sin clave por disparo, un reintento re-ejecutaba `ExecuteCampaignAsync` y los contadores de tracking doble-contaban (`../05_Master_ADR.md §Anti-patrones 3`).

## 4. Nivel materialización — UNIQUE(entry, seq)

`MaterializeNext` puede correr dos veces (redelivery de `CampaignRunStarted`, o `Fired`-handler + evento). El `INSERT trigger_occurrences(seq=@n+1)` está protegido por `UNIQUE(schedule_entry_id, sequence_no)`: el segundo insert falla con violación de unicidad, capturada como no-op. Nunca se generan dos ocurrencias para la misma posición de la serie, ni se salta una.

## 5. Reintentos y crash (interacción con reconciliación)

- Crash **antes** de TX-C commit → lease vence → reconciliación (`Attempt++`) → re-`Pending` → re-lease → re-fire con el **mismo `OccurrenceId`** → dedupe aguas abajo. Idempotente.
- Crash **después** de TX-C commit → Wolverine reentrega `StartCampaignRun` → Campaigns dedupe. Idempotente.
- `Attempt >= maxAttempts` → `Failed` (no reintento infinito). Alertable.

## 6. Fingerprint canónico

Payload de `Schedule` se serializa canónicamente (orden de campos estable, timezone normalizado a IANA, fechas en UTC ISO-8601) antes de SHA-256, para que un mismo request lógico produzca el mismo fingerprint de 64 hex (validación exacta en `ProcessedBusinessMessage.cs:52`).

## 7. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Business-inbox con fingerprint SHA-256 + estados | `ProcessedBusinessMessage.cs:27-74,107` | VERIFIED | 97% |
| Legado sin idempotencia por disparo (doble-conteo) | `../05_Master_ADR.md §Anti-patrones 3`; `CampaignSchedulerService.cs:97` | VERIFIED | 92% |
| Cadena `OccurrenceId`→`CampaignRun` | `Commands_And_Events.md §1`; `Domain_Design.md §3.2` | NEW | — |
