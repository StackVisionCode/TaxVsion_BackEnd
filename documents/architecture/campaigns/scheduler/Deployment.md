# Scheduler — Deployment

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

## 1. Forma de despliegue (según SCHED-001)

Recomendación del `ADR.md` (SCHED-001): **módulo dentro del deployment de Campaigns** (in-process, esquema `scheduler` en la BD de Campaigns), con un seam duro (`ICampaignScheduler` + tablas propias + comandos internos) que permita extraerlo a `TaxVision.Campaigns.Scheduler` sin reescritura si el escalado lo exige. Esta sección describe ambos modos porque el seam los hace equivalentes en contrato.

### Modo A — Módulo de Campaigns (recomendado, MVP)
- Corre **dentro** del proceso Campaigns (mismos pods/réplicas).
- El tick de lease es un `BackgroundService`/`IHostedService` **único por proceso** (no dos, a diferencia del legado que registraba `CampaignSchedulerBackgroundService` **y** `CampaignSchedulerService`).
- Comparte BD con Campaigns → TX-A y TX-C pueden co-locar la creación/actualización con el outbox de Campaigns; `StartCampaignRun` puede incluso ser un comando **in-process** si Campaigns está en el mismo proceso, o un mensaje durable si se separan. Empezar con mensaje durable mantiene el seam limpio.

### Modo B — Servicio propio `TaxVision.Campaigns.Scheduler`
- Deployment/imagen/BD propios; se comunica con Campaigns solo por bus (Wolverine) y M2M.
- Se justifica solo si el volumen de disparos o el aislamiento operativo lo requieren (ver ADR alternativas).

## 2. Escalado horizontal (seguro por diseño)

**N réplicas son seguras sin coordinación externa.** El claim atómico (`FOR UPDATE SKIP LOCKED` + `row_version`, `Concurrency_Spec.md`) hace que cualquier número de réplicas reparta ocurrencias sin doble-disparo. **No** se requiere leader election, Redis lock, ni Quartz cluster. Esto es el fix directo del legado, que no podía escalar (dos schedulers ya se pisaban dentro de **un** proceso).

Regla operativa: escalar réplicas mejora throughput y resiliencia (más manos para el dequeue y la reconciliación); nunca introduce duplicados. Rolling restart es seguro: ocurrencias `Leased` por un pod que baja se reconcilian por TTL en otro pod.

## 3. Dependencias de arranque

| Dependencia | Necesaria para | Estado |
|---|---|---|
| Postgres (esquema `scheduler` + tablas Wolverine) | persistencia + outbox | infra estándar |
| **Campaigns** (contrato `StartCampaignRun` + `CampaignRun`) | destino del disparo | **BLOCKER B-SCHED-1** |
| Bus Wolverine (RabbitMQ/transport del monorepo) | entrega durable de `StartCampaignRun` | infra estándar |
| Reloj confiable (NTP) | puntualidad de disparo | infra |

El Scheduler **no** depende de Wallet, Customer, ni de ningún ejecutor de canal para arrancar (esas dependencias son de Campaigns aguas abajo).

## 4. Configuración

- `Scheduler:TickInterval` (default 1 min; el legado usaba 1 min fijo, `CampaignSchedulerService.cs:17`).
- `Scheduler:LeaseTtlSeconds` (default 60).
- `Scheduler:MaxAttempts` (default p.ej. 5) antes de `Failed`.
- `Scheduler:CatchUpGrace` (ventana de gracia para disparos vencidos; fuera de ella → `Skipped`).
- `Scheduler:DequeueBatchSize` (`LIMIT @batch`).
- `Scheduler:MaxOccurrencesCeiling` (techo de seguridad para series sin fin).

Todo overridable por entorno; ningún valor hardcodeado en código como el legado.

## 5. Migraciones

EF Core migrations para `scheduler.schedule_entries` y `scheduler.trigger_occurrences` (índices parciales de `Data_Model.md`). Wolverine crea sus tablas de outbox/inbox por su envelope storage. Si Modo A, las migraciones del Scheduler se aplican junto con las de Campaigns (mismo DbContext o contexto separado con mismo connection string, según aislamiento deseado).

## 6. Rollout

1. Desplegar tablas + módulo con el tick **deshabilitado** por flag (`Scheduler:Enabled=false`).
2. Habilitar en una réplica, verificar métricas de cuadre (`fired == startcampaignrun.published`, `Observability.md`).
3. Escalar a N réplicas; confirmar `lease.contended` sano y cero duplicados en Campaigns.
4. Retirar cualquier disparador legado (no aplica en greenfield, pero relevante si se migra el CRM).

## 7. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Legado registraba dos schedulers (no escalable) | `CampaignSchedulerBackgroundService.cs:9` + `CampaignSchedulerService.cs:13` | VERIFIED | 96% |
| Intervalo de tick hardcodeado 1 min | `CampaignSchedulerService.cs:17`; `CampaignSchedulerBackgroundService.cs:13` | VERIFIED | 98% |
| Dep. dura de Campaigns para el destino | `../07_MVP_Scope.md`; `Domain_Design.md §7 B-SCHED-1` | DOCUMENTED_ONLY | 88% |
| Modo A/B + rollout propuesto | este documento | NEW | — |
