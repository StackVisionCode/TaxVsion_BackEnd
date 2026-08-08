# Campaigns — Concurrency Spec

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Concurrencia optimista (RowVersion/`xmin`) por aggregate + guards de estado idempotentes + unique constraints. Sin locks pesimistas de larga duración. El servicio escala horizontalmente: N instancias procesan la misma cola sin doble-efecto.

---

## 1. Fuentes de concurrencia

| Escenario | Contendientes | Riesgo si se ignora |
|---|---|---|
| Fan-out de dispatch | handler `DispatchRun` reentregado en 2 instancias | doble dispatch al mismo recipient |
| Results en paralelo | N `dispatch_result` para recipients distintos del mismo run | lost update en `campaign_run.counter_*` |
| Cierre del run | 2 results terminales evalúan cierre a la vez | doble `ReconcileRun` → doble consume/refund |
| Reserva Wallet | `ReservationConfirmed` reentregado | doble set de `wallet_reservation_id` |
| Edición vs disparo | usuario edita Campaign mientras el Scheduler dispara | run con snapshot inconsistente |
| Doble-scheduler / doble-trigger | 2 orígenes crean run de la misma ocurrencia | doble ejecución (anti-patrón legado #6) |

---

## 2. Aggregate boundaries = unidad de lock optimista

Cada `SaveChanges` afecta **un** aggregate con su `RowVersion`. Reglas:

- **Recipient-level:** un `dispatch_result` muta **solo** su fila `campaign_recipient` (RowVersion propio), no el `campaign_run`. Así N results de recipients distintos **no** contienden entre sí (filas distintas). Esto es clave para el throughput del fan-out.
- **Run-level:** las transiciones de `run_status` mutan `campaign_run` (RowVersion propio). Son de baja frecuencia (Created→Reserving→Dispatching→Reconciling→Completed).

**Separar el contador del run de la fila del recipient:** ver §3 (no denormalizar el incremento dentro de la misma tx del run si eso serializa todos los results).

---

## 3. Contadores: incremento sin contención

Dos opciones, se elige **B** por defecto:

**A. Incremento en `campaign_run.counter_*` dentro de la tx del result.** Simple, pero serializa todos los results del run sobre una fila → contención bajo fan-out grande.

**B. (elegida) Contador como agregación de recipients + rollup diferido.** El result muta solo el recipient (`dispatch_state` terminal). Un proceso de rollup (o un `UPDATE ... SET counter = (SELECT count...)` disparado en batch / al evaluar cierre) recomputa `counter_*`. La **fuente de verdad** son los recipients; `counter_*` es cache. Sin contención de fila caliente.

En ambos casos el incremento es idempotente porque la transición de recipient a terminal ocurre una sola vez (guard).

---

## 4. Cierre por conteo (evita doble reconcile)

El predicado de cierre `dispatched == delivered+failed+suppressed+bounced` puede ser verdadero para dos results terminales concurrentes. Para que **solo uno** dispare `ReconcileRun`:

- La transición `Dispatching → Reconciling` se hace con **compare-and-set sobre `run_status` + RowVersion**: `UPDATE campaign_run SET run_status=Reconciling WHERE id=@id AND run_status=Dispatching AND @closurePredicate`. El primero gana (1 fila afectada → procede a emitir consume/refund); el segundo afecta 0 filas → no-op.
- El consume/refund emitido es además idempotente por `(consume,runId)`/`(refund,runId)` (doble defensa). Ver `Idempotency_Spec.md`.

Corrige el `Status=Sending` **no-atómico** del legado (ADR-CAMP-000 §Anti-patrones #6): allí el cambio de estado y el trabajo no eran una operación atómica, permitiendo que dos schedulers lo tomaran.

---

## 5. Creación de run: unique constraint gana la carrera

Doble-trigger / doble-scheduler resuelto por `UNIQUE(tenant, campaign_id, occurrence_key)` (ver `Data_Model.md`, `Idempotency_Spec.md §2`). El insert perdedor captura la violación y devuelve el run existente. No hace falta lease en Campaigns para *esto* (el lease temporal vive en el Scheduler, `../scheduler/`); el unique key es la red de seguridad final incluso si el lease fallara.

---

## 6. Edición vs disparo

`StartCampaignRun` **congela** un snapshot de la Campaign en el `campaign_run` (channel/audience/template/price). Una edición concurrente de la Campaign (permitida solo en `Draft`; un `Scheduled` no es editable, `State_Machines.md §1`) no afecta runs ya creados. Si la Campaign estuviera en `Draft` no habría disparo (no está `Ready/Scheduled`), así que la ventana de carrera se cierra por la propia máquina de estados. La lectura de la Campaign para el snapshot usa su `RowVersion`; si cambia entre lectura y creación del run, se reintenta con el valor fresco.

---

## 7. Reserva Wallet: set-once

`wallet_reservation_id` se fija en la transición `Reserving→Dispatching` con guard `WHERE run_status=Reserving AND wallet_reservation_id IS NULL`. Un `ReservationConfirmed` reentregado afecta 0 filas la segunda vez → no-op. El fan-out solo se emite en la transición efectiva.

---

## 8. Sweeper de recipients "stuck"

Recipients que quedan en `Dispatched` sin result (el ejecutor murió, el result se perdió) bloquearían el cierre. Un job periódico (o el propio Scheduler) marca `Failed(timeout)` los que pasaron `dispatch_deadline`, con guard `WHERE dispatch_state=Dispatched AND deadline<now`. Es idempotente y permite refund de esa unidad. Sin este sweeper el run nunca cerraría (a diferencia del legado, que "cerraba" marcando Sent optimistamente sin confirmación real, ocultando el problema).

---

## 9. Multi-instancia / escalado

- Cualquier número de instancias consumen la cola Wolverine; la corrección **no** depende de "una sola instancia" (el legado dependía de un único `BackgroundService`, `CampaignSchedulerBackgroundService.cs:9`, y aun así podía doblar si se desplegaban dos réplicas).
- No hay estado en memoria load-bearing: todo el progreso está en BD (run/recipients/outbox). Un restart no pierde trabajo (corrige el `Task.Run`/`Task.Delay` volátil, `CampaignSchedulerBackgroundService.cs:38,78-95`).

---

## 10. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado depende de un único BackgroundService en memoria | `CampaignSchedulerBackgroundService.cs:9,23-42` | VERIFIED | 95% |
| Legado: fan-out volátil en memoria (se pierde al reiniciar) | `CampaignSchedulerBackgroundService.cs:38,78-95` | VERIFIED | 95% |
| Legado: `Status=Sending` no-atómico (doble scheduler) | ADR-CAMP-000 §Anti-patrones #6 | DOCUMENTED_ONLY | 90% |
| CAS sobre run_status + RowVersion para cierre único | diseño (este doc §4) | NEW | 87% |
| Contador como rollup para evitar fila caliente | diseño (este doc §3) | NEW | 84% |
| Sweeper de timeout | diseño (este doc §8) | NEW | 84% |
