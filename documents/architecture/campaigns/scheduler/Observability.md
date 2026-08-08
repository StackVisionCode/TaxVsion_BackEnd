# Scheduler — Observability

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

Objetivo de observabilidad: hacer **visible** lo que el legado dejaba invisible — disparos perdidos, dobles disparos, leases colgados, drift de recurrencia. El legado tragaba errores en `catch` y solo emitía `ILogger` de texto (`CampaignSchedulerBackgroundService.cs:144-147`, `CampaignSchedulerService.cs:104-110`), sin métricas ni trazas correlacionadas.

## 1. Métricas (OpenTelemetry)

| Métrica | Tipo | Labels | Alerta |
|---|---|---|---|
| `scheduler.tick.duration` | histogram | `instance` | p99 alto = contención de lease |
| `scheduler.occurrences.due` | gauge | `tenant?` | cola de debidos creciente = réplicas insuficientes |
| `scheduler.occurrences.leased` | counter | `instance` | — |
| `scheduler.occurrences.fired` | counter | `trigger_kind` | tasa base del sistema |
| `scheduler.occurrences.failed` | counter | `reason` | **> 0 alerta** |
| `scheduler.lease.reclaimed` | counter | `instance` | pico = crashes/evictions |
| `scheduler.lease.contended` | counter | — | claim `rowcount=0` frecuente = demasiadas réplicas |
| `scheduler.fire.dispatch_latency` | histogram | — | `due_at` → `fired_at`; **SLO clave** (ver §3) |
| `scheduler.startcampaignrun.published` | counter | — | debe = `occurrences.fired` (cuadre) |
| `scheduler.catchup.skipped` | counter | `tenant` | disparos vencidos descartados (§ catch-up) |
| `scheduler.duplicate_fire.suppressed` | counter | — | **debería ser ~0**; > 0 confirma que la guarda de idempotencia trabaja (y que hubo redelivery) |

**Cuadre de invariante:** `startcampaignrun.published` == `occurrences.fired`. Divergencia = bug de atomicidad (marca sin publicar o viceversa) → alerta crítica.

## 2. Trazas distribuidas

Un trace por ocurrencia, propagando `trace_id` en `StartCampaignRun` para enganchar con el trace de Campaigns → Wallet → ejecutores. Spans: `scheduler.lease` → `scheduler.fire` → (baggage `OccurrenceId`, `CampaignId`, `ScheduleEntryId`, `SequenceNo`, `TenantId`). Correlación con el seam existente: mismo espíritu que `CampaignId` opaco en `PostmasterEmailEvents.cs:37` — el `OccurrenceId` es la clave de correlación end-to-end del disparo.

## 3. SLOs

| SLO | Objetivo | Racional |
|---|---|---|
| **Puntualidad de disparo** | p95 `dispatch_latency` ≤ 30s | tick de 1 min → disparo dentro de ~1 tick |
| **Cero disparos perdidos** | `fired + skipped(justificado) == due materializados` en ventana | invariante de completitud |
| **Cero dobles disparos** | `duplicate_fire.suppressed` no genera `CampaignRun` extra (verificado contra Campaigns) | invariante de unicidad |
| **Recuperación de lease** | ocurrencia colgada vuelve a `Pending` ≤ TTL+tick | reconciliación viva |

## 4. Logs estructurados (no texto suelto)

Cada transición emite log estructurado con `event`, `occurrence_id`, `schedule_entry_id`, `campaign_id`, `tenant_id`, `sequence_no`, `status_from`, `status_to`, `attempt`. **Prohibido** el `catch` que traga (regla anti-legado): todo fallo se registra con `reason` **y** transiciona la ocurrencia a `Failed`/`Pending` (nunca queda en estado intermedio invisible).

## 5. Health checks

- `GET /internal/health/scheduler` (`[RateLimitExempt]`): liveness + readiness (BD alcanzable, outbox drenando).
- **Heartbeat de tick:** métrica `scheduler.tick.last_success_utc`; si ninguna réplica ticó en > 3× intervalo → alerta "scheduler detenido" (el legado podía morir en silencio y nadie lo notaba).
- **Watchdog de backlog:** `occurrences.due` con `due_at_utc < now - Nmin` y `status=Pending` sostenido → alerta (nadie está reclamando).

## 6. Auditoría

Las `trigger_occurrences` inmutables **son** el log de auditoría: cada disparo histórico persiste con su `due_at`/`fired_at`/`attempt`/`campaign_run_id`. Responde "¿se disparó la campaña X el día Y y a qué run derivó?" — imposible en el legado, que reseteaba la fila (`CampaignSchedulerBackgroundService.cs:124-126`).

## 7. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Legado solo `ILogger`, errores tragados | `CampaignSchedulerBackgroundService.cs:144-147`; `CampaignSchedulerService.cs:104-110` | VERIFIED | 96% |
| Historia de disparo se perdía (fila reseteada) | `CampaignSchedulerBackgroundService.cs:124-126` | VERIFIED | 95% |
| `CampaignId` como clave de correlación existente | `PostmasterEmailEvents.cs:37` | VERIFIED | 96% |
| Métricas/SLOs propuestos | este documento | NEW | — |
