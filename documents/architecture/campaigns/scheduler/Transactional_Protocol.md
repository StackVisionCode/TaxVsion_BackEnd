# Scheduler — Transactional Protocol

Servicio: **TaxVision.Campaigns.Scheduler**
Fecha: 2026-07-28
Estado: **DISEÑO — no implementado**

El Scheduler participa en la saga global de campañas (ver `../06_Cross_Service_Transactional_Protocol.md`) **solo en el primer eslabón**: convierte "es la hora" en "arrancá el run". No toca Wallet, ni audiencia, ni entrega. Su corrección transaccional se reduce a una garantía: **exactamente un `StartCampaignRun` comprometido por ocurrencia debida, sin doble-disparo al escalar y sin disparo perdido al reiniciar.**

## 1. Las tres transacciones locales

### TX-A — Agendar (síncrona, request del puerto interno)
```
BEGIN
  processed_business_messages.Begin(op=schedule, scope=CampaignId, key)   -- dedupe
  INSERT schedule_entries(...)
  INSERT trigger_occurrences(seq=1, status=Pending, due_at=…)             -- 1ª ocurrencia
  processed_business_messages.Complete(...)
COMMIT
```
Atómica: o hay entry + 1ª ocurrencia + registro de idempotencia, o no hay nada. Un retry con misma key devuelve el resultado previo (ver `Idempotency_Spec.md`).

### TX-B — Lease (claim atómico, tick del planificador)
```
BEGIN
  -- dequeue sin contención entre instancias
  SELECT id, row_version FROM trigger_occurrences
    WHERE status = Pending AND due_at_utc <= now()
    ORDER BY due_at_utc
    FOR UPDATE SKIP LOCKED
    LIMIT @batch;
  UPDATE trigger_occurrences
    SET status = Leased, lease_owner = @me, lease_until_utc = now()+@ttl, row_version = new
    WHERE id = @id AND row_version = @seen;     -- claim condicional (doble guarda)
COMMIT
```
`FOR UPDATE SKIP LOCKED` + claim condicional por `row_version`: dos instancias nunca reclaman la misma fila. Este es el reemplazo directo del legado, donde el "lock" era `campaign.Status = Sending; SaveChanges()` **sin** condición de fila (`CampaignSchedulerService.cs:88-91`) y con **dos** BackgroundServices escaneando la misma tabla al mismo tiempo (ver `Concurrency_Spec.md`).

### TX-C — Fire (publica el efecto, misma transacción que la marca) — **crítica**
```
BEGIN
  -- guarda: solo el dueño del lease, solo si sigue Leased y no vencido
  UPDATE trigger_occurrences
    SET status = Fired, fired_at_utc = now()
    WHERE id = @id AND status = Leased AND lease_owner = @me AND lease_until_utc > now();
  -- si rowcount = 0  => alguien más lo tomó / venció: ABORT sin publicar
  outbox.Enqueue(StartCampaignRunCommand{ OccurrenceId=@id, ... })   -- Wolverine outbox
COMMIT   -- marca Fired y mensaje encolado se comitean juntos
```
**Invariante de atomicidad:** la marca `Fired` y el encolado de `StartCampaignRun` viven en **la misma transacción** (outbox transaccional de Wolverine). Por tanto:
- Si commitea → el mensaje se entregará (at-least-once) y la fila está `Fired`.
- Si rollback (crash antes del commit) → ni marca ni mensaje; el lease vence y la ocurrencia vuelve a `Pending` (TX-E).

Esto elimina el TOCTOU del legado, donde el débito/estado se guardaba en una transacción y la ejecución ocurría en otra (`CampaignSchedulerService.cs:91` luego `:97`), permitiendo estado `Sending` sin ejecución o ejecución sin estado consistente.

### TX-D — Materializar próxima (recurrentes)
Disparada al confirmar `Fired` (mismo handler) o al recibir `CampaignRunStarted`:
```
BEGIN
  next = RecurrenceSpec.Next(spec, tz, last_due)     -- función pura, IClock
  IF next == null OR next > end_at OR occurrence_count+1 > max_occurrences:
      UPDATE schedule_entries SET status = Completed WHERE id=@e;
  ELSE:
      INSERT trigger_occurrences(seq = @seq+1, status=Pending, due_at=next);  -- UNIQUE(entry,seq)
      UPDATE schedule_entries SET occurrence_count = occurrence_count+1, next_due_at_utc = next;
COMMIT
```
`UNIQUE(schedule_entry_id, sequence_no)` hace la materialización idempotente: un reintento inserta la misma posición y falla el unique → no-op seguro.

### TX-E — Reconciliar leases colgados (barrido periódico)
```
BEGIN
  UPDATE trigger_occurrences
    SET status = CASE WHEN attempt+1 >= @maxAttempts THEN Failed ELSE Pending END,
        lease_owner = NULL, lease_until_utc = NULL, attempt = attempt+1
    WHERE status = Leased AND lease_until_utc < now();
COMMIT
```
Devuelve al ciclo las ocurrencias cuyo worker murió entre lease y fire. Como `StartCampaignRun` es idempotente por `OccurrenceId` en Campaigns, un re-fire tras crash **no** duplica el `CampaignRun`. Tras `@maxAttempts` → `Failed` + alerta (no bucle infinito silencioso como el legado).

## 2. Modos de falla y resultado

| Falla | Resultado | Garantía |
|---|---|---|
| Crash entre TX-B y TX-C | lease vence → TX-E → `Pending` → re-lease | sin disparo perdido |
| Crash tras COMMIT de TX-C, antes de entregar | Wolverine reintenta el envelope (durable) | at-least-once; Campaigns deduplica |
| Dos instancias compiten por la misma ocurrencia | `SKIP LOCKED` + claim condicional | una sola gana; sin doble-disparo |
| `StartCampaignRun` duplicado en Campaigns | dedupe por `OccurrenceId` | un solo `CampaignRun` |
| Materialización no comiteada | UNIQUE(entry,seq) + reintento | serie no se detiene ni duplica |

## 3. Frontera con la saga de Campaigns

El Scheduler entrega `StartCampaignRun` y **termina su responsabilidad**. Reserva de Wallet, resolución de audiencia, fan-out y consume/refund son de Campaigns/Wallet (ver `../06_Cross_Service_Transactional_Protocol.md`). El Scheduler no compensa nada aguas abajo: si Campaigns rechaza el run (ej. gate `module.campaigns` revocado, saldo insuficiente), eso lo maneja Campaigns; el Scheduler ya cumplió el contrato temporal. La recurrencia sigue viva salvo que Campaigns pida `Pause/Cancel` de la `ScheduleEntry`.

## 4. Evidencia

| Hecho | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Legado: "lock" no atómico (Status=Sending + Save, sin condición) | `CampaignSchedulerService.cs:88-91` | VERIFIED | 96% |
| Legado: estado y ejecución en transacciones separadas (TOCTOU) | `CampaignSchedulerService.cs:91,97,102` | VERIFIED | 94% |
| Outbox transaccional Wolverine es el estándar | `../00_Overview_And_Index.md:45` | DOCUMENTED_ONLY | 90% |
| Protocolo TX-A..TX-E propuesto | este documento | NEW | — |
