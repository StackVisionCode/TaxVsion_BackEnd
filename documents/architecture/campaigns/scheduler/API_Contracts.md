# Scheduler — API Contracts

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

## 0. Superficie: interna, no pública

El Scheduler **no** expone una API pública de cara al tenant. El tenant crea/agenda campañas contra **Campaigns** (que es quien tiene la UI y valida el gate `module.campaigns`). Campaigns, a su vez, invoca al Scheduler por un **puerto interno**. Esto respeta la frontera del `../02_Context_Map.md` (`Campaigns → Scheduler`).

Si el ADR (SCHED-001) resuelve **módulo dentro de Campaigns** (recomendado), este puerto es una **interfaz in-process** (`ICampaignScheduler`) — sin HTTP, sin red. Si se extrae a servicio propio, el mismo contrato se materializa como endpoints M2M (abajo). El contrato lógico es idéntico en ambos casos; solo cambia el transporte.

## 1. Puerto interno `ICampaignScheduler` (forma canónica)

```csharp
public interface ICampaignScheduler
{
    // Immediate / Scheduled / Recurring — devuelve la ScheduleEntry creada
    Task<Result<ScheduleHandle>> Schedule(ScheduleRequest req, CancellationToken ct);
    Task<Result> Pause(Guid scheduleEntryId, CancellationToken ct);
    Task<Result> Resume(Guid scheduleEntryId, CancellationToken ct);
    Task<Result> Cancel(Guid scheduleEntryId, CancellationToken ct);
    // Reprogramar (nuevo DueAt o nuevo spec) = Cancel lógico + nueva entry, o mutación si Active
    Task<Result<ScheduleHandle>> Reschedule(RescheduleRequest req, CancellationToken ct);
    Task<Result<ScheduleView>> Get(Guid scheduleEntryId, CancellationToken ct);
}
```

`ScheduleRequest`:
```
CampaignId        : Guid        // referencia opaca, sin FK
TenantId          : Guid        // explícito, fail-closed (nunca inferido del hilo en scope Wolverine)
Kind              : OneShot | Recurring
TimeZone          : string      // IANA obligatorio para Scheduled/Recurring
DueAtUtc          : DateTime?   // requerido si OneShot Scheduled (null => Immediate = now)
Recurrence        : RecurrenceSpecDto?   // requerido si Recurring
IdempotencyKey    : string      // "schedule:{campaignId}:{clientToken}"
```

`RecurrenceSpecDto` (paridad con el legado, validado): `Frequency`, `Interval(>0)`, `DaysOfWeek`, `DayOfMonth(1..31)`, `TimeOfDay(HH:mm)`, `EndAtUtc?`, `MaxOccurrences?`.

**Reglas de validación (Result.Failure, nunca excepción a la UI):**
- `TimeZone` desconocido → `Scheduler.TimeZoneInvalid`.
- `Recurring` sin `Recurrence` → `Scheduler.RecurrenceRequired`.
- `DueAtUtc` en pasado lejano (> tolerancia de gracia, ej. 24h) → `Scheduler.DueAtInPast` (una ocurrencia que ya venció por horas no debe dispararse en masa al arrancar; ver `Concurrency_Spec.md §catch-up`).
- `Interval <= 0`, `DayOfMonth` fuera de 1..31, `TimeOfDay` no parseable → error tipado (el legado no validaba, `RecurrenceCalculator.cs:23` parseaba en runtime).

`ScheduleHandle`: `{ ScheduleEntryId, NextDueAtUtc }`.

## 2. Variante servicio-propio (si SCHED-001 = servicio)

Endpoints M2M (client-credentials, audience `scheduler.api`, scope `scheduler:write`), **no** accesibles por el JWT de usuario final:

| Método | Ruta | RateLimit | Auth |
|---|---|---|---|
| `POST` | `/internal/schedules` | `[RateLimit("m2m-write")]` | M2M scope `scheduler:write` |
| `POST` | `/internal/schedules/{id}/pause` | `[RateLimit("m2m-write")]` | M2M |
| `POST` | `/internal/schedules/{id}/resume` | `[RateLimit("m2m-write")]` | M2M |
| `POST` | `/internal/schedules/{id}/cancel` | `[RateLimit("m2m-write")]` | M2M |
| `POST` | `/internal/schedules/{id}/reschedule` | `[RateLimit("m2m-write")]` | M2M |
| `GET`  | `/internal/schedules/{id}` | `[RateLimit("m2m-read")]` | M2M scope `scheduler:read` |
| `GET`  | `/internal/health/scheduler` | `[RateLimitExempt]` | infra |

Todo endpoint lleva `[RateLimit(...)]` o `[RateLimitExempt]` explícito (regla dura de CLAUDE.md / `RateLimit/Guia_Nuevos_Servicios_Endpoints.md`). El `TenantId` viaja **en el cuerpo** validado contra el token M2M (nunca en query string — regla de privacidad). No hay endpoint que reciba el JWT de usuario: corrige el anti-patrón del legado de persistir/propagar `Campaign.BackgroundAuthToken` (JWT de usuario en BD, `../05_Master_ADR.md §Anti-patrones 5`).

## 3. No hay endpoint de "ejecutar ahora la campaña"

El disparo real (`StartCampaignRun`) es **saliente** y **asíncrono** vía outbox (ver `Commands_And_Events.md`), no un endpoint que alguien invoque. No existe un `POST /execute` síncrono como el `SendCampaignCommand` del legado que hacía fan-out fire-and-forget dentro del request (`CampaignSchedulerBackgroundService.cs:78-95`, `BackgroundTaskQueue`). El único "trigger" es la maquinaria de lease + reloj.

## 4. Idempotencia de la API

`Schedule` es idempotente por `(TenantId, Operation="schedule", CampaignId, IdempotencyKey)` vía `ProcessedBusinessMessage` (business-inbox, `ProcessedBusinessMessage.cs:27`). Reintento con misma key y mismo fingerprint → devuelve el `ScheduleHandle` previo; misma key con payload distinto → `409 Scheduler.IdempotencyConflict`. Detalle en `Idempotency_Spec.md`.

## 5. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Legado disparaba vía handler HTTP + cola in-proc | `SendCampaignCommandHandler.cs:86`; `Infrastructure/Services/BackgroundTaskQueue.cs` | VERIFIED | 94% |
| JWT de usuario persistido (anti-patrón a no repetir) | `../05_Master_ADR.md §Anti-patrones 5` (`Campaign.BackgroundAuthToken`) | DOCUMENTED_ONLY | 88% |
| `ProcessedBusinessMessage` disponible para idempotencia de API | `TaxVision.Growth.Infrastructure/Persistence/Idempotency/ProcessedBusinessMessage.cs:27-74` | VERIFIED | 97% |
| Contrato `ICampaignScheduler` / endpoints M2M | este documento | NEW | — |
