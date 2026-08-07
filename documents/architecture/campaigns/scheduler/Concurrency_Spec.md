# Scheduler — Concurrency Spec

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

El Scheduler es, por definición, un componente **concurrente y escalado horizontalmente**: N réplicas del proceso corriendo el mismo tick. Toda su corrección depende de que N réplicas produzcan el mismo resultado que 1. Este documento fija cómo.

## 1. El bug del legado que corregimos (doble-scheduler)

Evidencia dura: en el legado coexisten **DOS** `BackgroundService` distintos que hacen exactamente lo mismo sobre la misma tabla:

- `CampaignSchedulerBackgroundService` — poll cada 1 min (`CampaignSchedulerBackgroundService.cs:13,27,38`), selecciona `Status==Scheduled && ScheduledAt<=now` (`:54-59`), encola en `IBackgroundTaskQueue` in-proc (`:78`).
- `CampaignSchedulerService` — poll cada 1 min (`CampaignSchedulerService.cs:17,42`), selecciona el **mismo** conjunto (`:56-74`), marca `Status=Sending` y ejecuta (`:88-99`).

Ambos registrados como hosted services → **cada campaña debida se procesa dos veces** por proceso, y una vez más por cada réplica adicional. El único "candado" era `campaign.Status = Sending; SaveChangesAsync()` **sin** condición de fila (`:88-91`): entre el `SELECT` (que ya leyó `Scheduled`) y el `UPDATE`, otra instancia/otro servicio ya podía haber leído la misma fila. TOCTOU clásico → doble-envío al escalar. Además la fan-out era `Task.Run`/cola en memoria, perdida al reiniciar (`:97`, `BackgroundTaskQueue`).

## 2. Modelo de concurrencia nuevo: un solo camino, claim atómico

**Un solo** planificador lógico, sin líder dedicado: cada réplica compite por ocurrencias con **claim atómico a nivel BD**. No hay elección de líder frágil; la BD es el árbitro.

### 2.1 Dequeue: `FOR UPDATE SKIP LOCKED`
```sql
SELECT id, row_version FROM scheduler.trigger_occurrences
 WHERE status = Pending AND due_at_utc <= now()
 ORDER BY due_at_utc
 FOR UPDATE SKIP LOCKED
 LIMIT @batch;
```
`SKIP LOCKED` reparte las filas debidas entre réplicas sin que dos tomen la misma. Escala linealmente (cada réplica se lleva un lote disjunto).

### 2.2 Claim condicional (segunda guarda, optimistic)
```sql
UPDATE scheduler.trigger_occurrences
   SET status=Leased, lease_owner=@me, lease_until_utc=now()+@ttl, row_version=<new>
 WHERE id=@id AND row_version=@seen;   -- rowcount=1 => gané; 0 => otro ganó
```
Doble red de seguridad: aun sin `SKIP LOCKED` (o con lecturas repetibles distintas), el `row_version` garantiza un único ganador. Es la corrección exacta del `Status=Sending` sin condición del legado.

### 2.3 Fire con guarda de propiedad del lease
El `UPDATE … SET status=Fired WHERE id=@id AND status=Leased AND lease_owner=@me AND lease_until_utc>now()` (ver `Transactional_Protocol.md §TX-C`) asegura que solo el dueño vigente del lease publica `StartCampaignRun`, y solo si el lease no venció.

## 3. Leases y reloj

- **TTL del lease** dimensionado > tiempo máx esperado de `Fire` (que es solo encolar en outbox + marcar, milisegundos), con margen amplio (ej. 60s). Corto para recuperación rápida, pero mayor que cualquier GC pause razonable.
- **`IClock` inyectado** en todo el dominio (materialización, "debido", expiración). El legado usaba `DateTime.UtcNow` disperso e inline (`RecurrenceCalculator.cs:42`, `CampaignSchedulerService.cs:54`), imposible de testear y fuente de condiciones de carrera con el reloj. Aquí el reloj es una dependencia → tests deterministas de recurrencia/lease.
- **Timezone-aware:** `RecurrenceSpec.Next` calcula en la `TimeZone` IANA de la entry y convierte a UTC (maneja DST). El legado calculaba en UTC naïve (`nextDate.Date.Add(timeOfDay)`, `RecurrenceCalculator.cs:25`) → una campaña "9:00 local" derivaba una hora tras el cambio de horario.

## 4. Reconciliación de runs colgados

Barrido periódico (TX-E, `Transactional_Protocol.md`): ocurrencias `Leased` con `lease_until_utc < now()` → vuelven a `Pending` (`Attempt++`) o `Failed` si agotaron reintentos. Cubre: réplica muerta entre lease y fire, deploy/rolling restart, pod evicted. Como el re-fire usa el mismo `OccurrenceId`, la reconciliación es segura frente a duplicados (idempotencia aguas abajo, `Idempotency_Spec.md §5`). Esto es lo que el legado nunca tuvo: una ocurrencia `Sending` tras un crash quedaba **colgada para siempre** (nadie la devolvía a `Scheduled`).

## 5. Catch-up / disparos vencidos

Al arrancar tras downtime pueden existir muchas ocurrencias `Pending` con `due_at_utc` muy pasado. Política:
- **OneShot** vencido dentro de la ventana de gracia (ej. ≤ configurable) → dispara.
- Vencido **más allá** de la gracia → `Skipped` con evento (no floodear a los destinatarios por una campaña que debió salir ayer). Decisión explícita, no accidental como el legado (que dispararía todas de golpe al reiniciar).
- **Recurring:** no se "acumulan" ocurrencias perdidas; se materializa la **próxima** relevante desde `now` (coalescing), evitando ráfagas de disparos atrasados.

## 6. Backpressure

El `LIMIT @batch` del dequeue acota cuántas ocurrencias reclama una réplica por tick, cediendo el fan-out real (por destinatario) a Campaigns/ejecutores. El Scheduler nunca hace fan-out por destinatario (a diferencia del legado, que iteraba destinatarios con `Task.Delay` inline). El disparo es O(campañas debidas), no O(destinatarios).

## 7. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| DOS schedulers sobre la misma tabla | `CampaignSchedulerBackgroundService.cs:54-59` + `CampaignSchedulerService.cs:56-74` | VERIFIED | 97% |
| "Candado" no atómico (Status=Sending sin condición de fila) | `CampaignSchedulerService.cs:88-91` | VERIFIED | 96% |
| Poll con `Task.Delay`, fan-out in-proc perdido al reiniciar | `CampaignSchedulerBackgroundService.cs:38,78`; `CampaignSchedulerService.cs:42,97` | VERIFIED | 95% |
| `DateTime.UtcNow` inline, sin `IClock`, UTC naïve | `RecurrenceCalculator.cs:25,42`; `CampaignSchedulerService.cs:54` | VERIFIED | 94% |
| Claim atómico `SKIP LOCKED` + `row_version` | este documento | NEW | — |
