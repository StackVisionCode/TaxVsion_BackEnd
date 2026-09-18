# Scheduler — ADRs

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — Scheduler = disparo temporal con lease atómico (Immediate/Scheduled/Recurring), un `CampaignRun` inmutable por disparo. SIN dinero:** el Scheduler NO reserva/consume/verifica saldo. La regla "para scheduled/recurrente cobrar ANTES según cuántos destinatarios" la hace un **interceptor/PEP externo** colocado sobre la ruta del `RunDue` (antes de que Campaign ejecute); PEP + Wallet son **externos y DIFERIDOS**, no viven en el Scheduler ni en Campaign (ver `../05_Master_ADR.md` D1/D7). Lo que abajo asuma que el Scheduler toca saldo/Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

Estos ADRs refinan `ADR-CAMP-000 §Decisión 4` (Scheduler como owner del disparo con lease atómico) al nivel del servicio. Estado de cada uno: **PROPOSED** salvo indicación.

---

## ADR-SCHED-001 — ¿Servicio propio o módulo de Campaigns?

**Estado:** PROPOSED → recomendación **MÓDULO de Campaigns** (con seam de extracción).

### Contexto
`ADR-CAMP-000` dejó abierta la forma de despliegue del Scheduler. El Wallet es un microservicio independiente **porque el usuario lo quiere reutilizable** (Campaigns, SMS individual, futuros consumidores). El Scheduler no tiene ese requisito: su **único** consumidor es Campaigns (emite `StartCampaignRun` y nada más), y su trabajo (materializar ocurrencias, disparar el run) está intrínsecamente acoplado al ciclo de vida de la campaña.

### Decisión
Implementar el Scheduler como **módulo/bounded-context dentro del deployment de Campaigns**, con:
- tablas propias en esquema `scheduler` (aislamiento de datos),
- puerto interno `ICampaignScheduler` (no HTTP),
- comandos/eventos internos propios,
de modo que sea **extraíble a `TaxVision.Campaigns.Scheduler`** sin reescritura si algún día se necesita.

### Justificación
1. **Un solo consumidor, acoplamiento temporal fuerte.** El disparo existe para arrancar un `CampaignRun`; separarlo por la red añade un hop y una frontera transaccional sin ganancia de reutilización (a diferencia de Wallet, sí reutilizable).
2. **Transacción local para la atomicidad crítica.** Co-locar `trigger_occurrences` con el outbox permite que "marcar `Fired`" y "publicar/crear el run" compartan transacción (o al menos el mismo outbox), reforzando la invariante de un-disparo-por-ocurrencia. Cruzar la red aquí reintroduce coordinación distribuida donde no aporta.
3. **Sin secretos ni PII** (ver `Security.md`) → no hay razón de aislamiento de seguridad que empuje a servicio separado.
4. **Menor superficie operativa** (un deployment menos, una BD menos) para el MVP.
5. **Reversible barato:** el seam duro (puerto + tablas + comandos) permite promover a servicio si el volumen de disparos o el aislamiento operativo lo justifican.

### Alternativas
- **Servicio propio desde el día 1** — rechazada para MVP: coordinación distribuida y una BD extra sin beneficio de reutilización; se mantiene como camino de extracción (Modo B, `Deployment.md`).
- **Reusar un scheduler genérico (Quartz.NET / Hangfire)** — rechazada: (a) su modelo de recurrencia no conoce `CampaignRun` inmutable ni el contrato dispatch/result; (b) introduce su propio store y su propio clustering, duplicando la maquinaria de lease que ya obtenemos gratis con Postgres `SKIP LOCKED`; (c) la lógica de tenant fail-closed y de idempotencia por ocurrencia quedaría fuera de nuestro dominio. Sí se pueden reutilizar **primitivas** (Wolverine scheduled messages) donde encajen, pero la fuente de verdad del "cuándo" es nuestra tabla.

### Consecuencias
Campaigns y Scheduler comparten proceso/BD pero **no** tipos de dominio ni tablas. Extracción futura = mover el módulo + apuntar el puerto a transporte M2M/bus. Documentado en `Deployment.md`.

---

## ADR-SCHED-002 — Lease atómico vía Postgres, sin líder dedicado

**Estado:** PROPOSED (fija `ADR-CAMP-000 §Decisión 4` "lease atómico").

### Contexto
El legado tenía **dos** `BackgroundService` (`CampaignSchedulerBackgroundService.cs:9`, `CampaignSchedulerService.cs:13`) escaneando la misma tabla, con un "candado" no atómico (`Status=Sending; SaveChanges` sin condición de fila, `CampaignSchedulerService.cs:88-91`) → doble-disparo al escalar y ocurrencias colgadas al reiniciar.

### Decisión
Claim atómico a nivel BD: dequeue con `SELECT … FOR UPDATE SKIP LOCKED` + claim condicional `UPDATE … WHERE id=@id AND row_version=@seen`, con `lease_until_utc` (TTL) para recuperación. **Sin** leader election, **sin** lock distribuido externo (Redis/ZooKeeper). N réplicas son seguras por construcción.

### Justificación
- `SKIP LOCKED` reparte ocurrencias entre réplicas sin contención ni duplicados; `row_version` es la segunda red de seguridad.
- El TTL del lease + barrido de reconciliación devuelve ocurrencias de workers muertos (fix del "colgado para siempre" del legado).
- La BD ya es la fuente de verdad y transaccional; añadir un coordinador externo sería otra pieza que puede fallar.

### Alternativas
- **Leader election (un solo scheduler activo)** — rechazada: cuello de botella y punto único; el failover del líder puede duplicar o pausar disparos.
- **Lock distribuido (Redis SETNX / advisory lock global)** — rechazada: serializa el dequeue (mata el escalado) y añade dependencia externa. (El advisory lock **por-fila** es redundante frente a `SKIP LOCKED`.)

### Consecuencias
La corrección depende de una BD que soporte `SKIP LOCKED` (Postgres ✓). Detalle operativo en `Concurrency_Spec.md` y `Transactional_Protocol.md`.

---

## ADR-SCHED-003 — Ocurrencia de disparo inmutable (no mutar la regla)

**Estado:** PROPOSED (aplica `ADR-CAMP-000 §Anti-patrón 8` al plano del disparo).

### Contexto
El legado modelaba la recurrencia como **una fila mutable** (`RecurrenceRule.ExecutionCount++`, `NextExecutionAt`, `Campaign.SentAt=null` reseteados en cada ejecución, `CampaignSchedulerBackgroundService.cs:115-126`, `CampaignSchedulerService.cs:130-149`). Sin historia, sin auditoría, y sin clave estable para idempotencia por disparo.

### Decisión
Separar **definición** (`ScheduleEntry`, mutable de estado, no de ejecución) de **ejecución** (`TriggerOccurrence`, **inmutable** una vez `Fired`, una fila por instante). El `TriggerOccurrence.Id` es la clave de idempotencia que fluye a `StartCampaignRun.OccurrenceId` → `CampaignRun` de Campaigns.

### Justificación
- Idempotencia natural por disparo (fix del doble-conteo del legado, `ADR-CAMP-000 §Anti-patrón 3`).
- Auditoría completa: cada disparo histórico persiste.
- Materialización una-a-una acota el horizonte (sin pre-generar series infinitas).

### Consecuencias
Más filas (una por ocurrencia) con política de retención (`Data_Model.md §5`), a cambio de auditabilidad e idempotencia. Un único algoritmo de recurrencia puro `RecurrenceSpec.Next` reemplaza los **dos** divergentes del legado (`RecurrenceCalculator.cs` vs `CampaignSchedulerService.cs:158-169`).

---

## ADR-SCHED-004 — Timezone-aware con `IClock` inyectado

**Estado:** PROPOSED.

### Contexto
El legado calculaba recurrencia en `DateTime.UtcNow` naïve, sin timezone (`RecurrenceCalculator.cs:25,42`), con el reloj leído inline (no testeable). Una campaña "todos los días 9:00 local" derivaba mal tras cambios de DST.

### Decisión
`RecurrenceSpec` guarda `TimeZone` IANA; `RecurrenceSpec.Next(spec, tz, afterUtc)` es una función pura que resuelve la hora local y convierte a UTC. El reloj es una dependencia (`IClock`) en todo el dominio (materialización, "debido", expiración de lease).

### Justificación
Correctitud de DST/zonas + tests deterministas de recurrencia y de reconciliación de leases. Elimina condiciones de carrera con el reloj del sistema.

### Consecuencias
`TimeZone` es obligatorio para Scheduled/Recurring (validado en la API, `API_Contracts.md §1`).

---

## ADR-SCHED-005 — Políticas temporales: misfire, solapamiento, DST, edición (decisiones del usuario 2026-09-17)

**Estado:** ACCEPTED (cierra los pendientes funcionales del review #29).

### Decisión
- **Misfire = COALESCE.** Tras downtime, se dispara **una** ocurrencia (la más reciente dentro de `MisfireGrace`); el resto `Skipped(misfire)`; todas fuera de gracia → `Skipped(stale)`. Evita ráfagas de campañas repetidas sin perder por completo el periodo.
- **Solapamiento = OMITIR + ALERTAR.** Si el run anterior de la entry sigue activo (`ActiveRunRef` vivo, sabido por `campaign.run.completed.v1`), la nueva ocurrencia → `Skipped(overlap)` + alerta. `overlap_watchdog` libera el ref si el evento de cierre nunca llega.
- **DST explícito.** Hora local inexistente (spring-forward) → siguiente instante válido; ambigua (fall-back) → primera ocurrencia, una sola vez.
- **Edición de recurrencia versionada.** Editar el spec de una entry `Active`/`Paused` incrementa `SpecVersion`, cancela la próxima `Pending` no leaseada y re-materializa desde `now`; las `Fired` son inmutables.

### Justificación
Cada política prioriza **no reenviar de más a la misma audiencia** y **preservar historial/auditoría**, coherente con la inmutabilidad de `TriggerOccurrence` (SCHED-003) y con que el estado del run vive en Campaigns (frontera del `../02_Context_Map.md`): el solapamiento se evalúa por evento de cierre, no consultando el run.

### Consecuencias
El Scheduler consume `campaign.run.completed.v1` (nuevo consumo, `Commands_And_Events.md §2.1`); añade `MisfireGrace`, `ActiveRunRef`, `SpecVersion` a `ScheduleEntry`; y `SkipReason` a `TriggerOccurrence`. Ver `Concurrency_Spec.md §5/§5.1/§3`, `Domain_Design.md §8`.

## Evidencia consolidada

| ADR | Evidencia legado (file:line) | Clasificación | Confianza |
|---|---|---|---|
| 001 | único consumidor = Campaigns (`Commands_And_Events.md §1`); Wallet reutilizable por contraste (`ADR-CAMP-000 §Decisión 3`) | DOCUMENTED_ONLY | 85% |
| 002 | doble scheduler + candado no atómico: `CampaignSchedulerBackgroundService.cs:9`, `CampaignSchedulerService.cs:13,88-91` | VERIFIED | 97% |
| 003 | regla mutada en sitio: `RecurrenceRule.cs:8-27`; `CampaignSchedulerService.cs:130-149` | VERIFIED | 97% |
| 004 | UTC naïve, reloj inline: `RecurrenceCalculator.cs:25,42`; dos algoritmos: `:9-83` vs `CampaignSchedulerService.cs:158-169` | VERIFIED | 95% |

## Blockers

- **B-SCHED-1:** Campaigns debe exponer el contrato `StartCampaignRun` + `CampaignRun` inmutable antes de que el Scheduler tenga destino (dep. dura, `Deployment.md §3`).
- **B-SCHED-2 (resuelto aquí):** forma de despliegue → SCHED-001 recomienda módulo con seam de extracción.
