# Campaigns — Concurrency Spec

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-16 (revisión: **sin reserva/liquidación de dinero**)
- **Estado:** DISEÑO — no implementado

Concurrencia optimista (RowVersion/`xmin`) por aggregate + guards de estado idempotentes + unique constraints. Sin locks pesimistas de larga duración. El servicio escala horizontalmente: N instancias procesan la misma cola sin doble-efecto.

> **Regla dura:** no hay estado ni contención monetaria en Campaign (ni `wallet_reservation_id`, ni consume/refund). La concurrencia se resuelve solo alrededor de **run/recipients/contadores de entrega**.

---

## 1. Fuentes de concurrencia

| Escenario | Contendientes | Riesgo si se ignora |
|---|---|---|
| Fan-out de dispatch | handler `DispatchRun` reentregado en 2 instancias | doble dispatch al mismo recipient |
| Results en paralelo | N `dispatch.result` para recipients distintos del mismo run | lost update en `campaign_run.counter_*` |
| Cierre del run | 2 results terminales evalúan cierre a la vez | doble `CloseRun` |
| Edición vs disparo | usuario edita Campaign mientras el Scheduler dispara | run con snapshot inconsistente |
| Doble-scheduler / doble-trigger | 2 orígenes crean run de la misma ocurrencia | doble ejecución (anti-patrón legado #6) |

---

## 2. Aggregate boundaries = unidad de lock optimista

Cada `SaveChanges` afecta **un** aggregate con su `RowVersion`. Reglas:

- **Recipient-level:** un `dispatch.result` muta **solo** su fila `campaign_recipient` (RowVersion propio), no el `campaign_run`. Así N results de recipients distintos **no** contienden entre sí (filas distintas). Esto es clave para el throughput del fan-out.
- **Run-level:** las transiciones de `run_status` mutan `campaign_run` (RowVersion propio). Son de baja frecuencia (Created→Dispatching→Completed/PartiallyFailed/Cancelled).

**Separar el contador del run de la fila del recipient:** ver §3 (no denormalizar el incremento dentro de la misma tx del run si eso serializa todos los results).

---

## 3. Contadores: estrategia CANÓNICA única (fix #07)

**Decisión canónica (no hay dos opciones):** las **unidades `campaign_recipient` son la fuente de verdad**; `campaign_run.counter_*` es **solo caché**, recalculada por **rollup** (batch/al evaluar cierre), no dentro de la tx de cada result. El result muta **solo** su fila de unidad (`dispatch_state`); así N results de unidades distintas no contienden sobre la fila caliente del run. `Data_Model.md §3`, `Transactional_Protocol.md §5` y `Idempotency_Spec.md` reflejan esta misma elección (antes se contradecían).

- El incremento lógico es idempotente porque cada unidad settlea una sola vez (guard de estado).
- **El cierre NO consulta la caché:** evalúa una **condición autoritativa** — un `COUNT` sobre `campaign_recipient` por estado, o un contador ya reconciliado — para no cerrar con una caché atrasada.

---

## 4. Cierre por conteo (evita doble cierre) — predicado corregido (#01)

El predicado autoritativo es sobre el **total congelado de unidades**:
`materialization_complete ∧ emission_complete ∧ (delivered + accepted + failed + skipped + unknown == recipient_count)`.
Puede ser verdadero para dos results terminales concurrentes. Para que **solo uno** dispare `CloseRun`:

- La transición `Dispatching → {Completed|PartiallyFailed|Failed}` se hace con **compare-and-set sobre `run_status` + RowVersion** condicionado al predicado: `UPDATE campaign_run SET run_status=@terminal WHERE id=@id AND run_status=Dispatching AND @closurePredicate`. El primero gana (1 fila → emite `campaign.run.completed.v1`); el segundo afecta 0 filas → no-op.
- **Carrera de "dos últimos results":** si dos commits concurrentes no se ven mutuamente y ninguno dispara el CAS, un **reconciliador de cierre** (job periódico durable) reevalúa el predicado y cierra. Así el cierre no depende del orden de llegada ni de que un result "vea" al otro.

Corrige el `Status=Sending` **no-atómico** del legado (ADR-CAMP-000 §Anti-patrones #6) y el predicado erróneo `==Dispatched` (que ignoraba `Skipped`).

---

## 5. Creación de run: unique constraint gana la carrera

Doble-trigger / doble-scheduler resuelto por `UNIQUE(tenant, campaign_id, occurrence_key)` (ver `Data_Model.md`, `Idempotency_Spec.md §2`). El insert perdedor captura la violación y devuelve el run existente. No hace falta lease en Campaigns para *esto* (el lease temporal vive en el Scheduler, `../scheduler/`); el unique key es la red de seguridad final incluso si el lease fallara.

---

## 6. Edición vs disparo

`StartCampaignRun` **congela** un snapshot de la Campaign en el `campaign_run` (channels/audience/template/sender). Una edición concurrente de la Campaign (permitida solo en `Draft`; un `Scheduled` no es editable, `State_Machines.md §1`) no afecta runs ya creados. Si la Campaign estuviera en `Draft` no habría disparo (no está `Ready/Scheduled`), así que la ventana de carrera se cierra por la propia máquina de estados. La lectura de la Campaign para el snapshot usa su `RowVersion`; si cambia entre lectura y creación del run, se reintenta con el valor fresco.

---

## 7. Sweeper de unidades "stuck" → `Unknown` (fix #04)

Unidades que quedan en `Dispatched` sin result (el consumer murió, el result se perdió) bloquearían el cierre. Un job periódico marca **`Unknown`** (no `Failed`) las que pasaron `dispatch_deadline`, con guard `WHERE dispatch_state=Dispatched AND dispatch_deadline<now`. Es idempotente y permite cerrar con incertidumbre explícita. Un `Delivered`/`Failed` posterior **reconcilia** `Unknown` sin doble conteo. Nunca se declara `Failed` por falta de respuesta (a diferencia del legado, que "cerraba" marcando Sent sin confirmación real).

---

## 8. Multi-instancia / escalado

- Cualquier número de instancias consumen la cola Wolverine; la corrección **no** depende de "una sola instancia" (el legado dependía de un único `BackgroundService`, `CampaignSchedulerBackgroundService.cs:9`, y aun así podía doblar si se desplegaban dos réplicas).
- No hay estado en memoria load-bearing: todo el progreso está en BD (run/recipients/outbox). Un restart no pierde trabajo (corrige el `Task.Run`/`Task.Delay` volátil, `CampaignSchedulerBackgroundService.cs:38,78-95`).

---

## 9. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado depende de un único BackgroundService en memoria | `CampaignSchedulerBackgroundService.cs:9,23-42` | VERIFIED | 95% |
| Legado: fan-out volátil en memoria (se pierde al reiniciar) | `CampaignSchedulerBackgroundService.cs:38,78-95` | VERIFIED | 95% |
| Legado: `Status=Sending` no-atómico (doble scheduler) | ADR-CAMP-000 §Anti-patrones #6 | DOCUMENTED_ONLY | 90% |
| CAS sobre run_status + RowVersion para cierre único | diseño (este doc §4) | NEW | 87% |
| Contador como rollup para evitar fila caliente | diseño (este doc §3) | NEW | 84% |
| Sin estado/contención monetaria en Campaign | ADR-CAMP-001 D1 (decisión del usuario) | DECISION | 99% |
