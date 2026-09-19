# Campaigns — State Machines

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-17 (revisión v2 — hallazgos de review #01/#03/#04/#09/#10/#17/#22)
- **Estado:** DISEÑO — no implementado

> **Modelo canónico v2 (decisiones del usuario 2026-09-17):**
> - **Unidad de trabajo = destinatario/canal.** Si una campaña selecciona Email+SMS, cada persona genera **2 unidades** (una por canal). `recipient_count` del run = **total congelado de unidades**, no de personas. Personas ≠ unidades ≠ intentos.
> - **Semántica de entrega explícita:** `Accepted` (proveedor aceptó) ≠ `Delivered` (confirmado por webhook) ≠ `Unknown` (sin confirmación / timeout). Un timeout **no** es `Failed`.
> - **Cierre por total congelado**, no por `Dispatched` (fix #01).

Tres máquinas independientes: **Campaign** (definición), **CampaignRun** (una ejecución), **CampaignRecipient** (una unidad destinatario/canal). Cada transición es un método del aggregate que devuelve `Result`, protegido por un state-guard idempotente. **Sin dinero** (ADR-CAMP-001 D1). Tracking de engagement (open/click) y `Suppressed`/`Bounced` = diferidos post-MVP.

---

## 1. Campaign (definición)

```
        create
          │
          ▼
      ┌────────┐  edit content/audience/schedule (permitido solo aquí)
      │ Draft  │◄─────────────┐
      └───┬────┘              │
   MarkReady │ (validación)   │ edit → vuelve a Draft (invalida readiness)
          ▼                   │
      ┌────────┐──────────────┘
      │ Ready  │
      └───┬────┘
   Schedule │ / TriggerNow
          ▼
      ┌───────────┐   (cada disparo NO cambia la Campaign; crea un CampaignRun)
      │ Scheduled │──────────► [Scheduler crea CampaignRun N]
      └───┬───────┘◄──────── recurrencia re-agenda la MISMA Campaign
   Archive │
          ▼
      ┌──────────┐
      │ Archived │ (soft; runs históricos permanecen)
      └──────────┘
```

| Estado | Significado | Transiciones salientes |
|---|---|---|
| `Draft` | Editable, incompleta | `MarkReady`, `Archive` |
| `Ready` | Validada, no agendada | `Schedule`, `TriggerNow`, editar→`Draft`, `Archive` |
| `Scheduled` | Con ScheduleSpec activo (incl. recurrente) | `Unschedule`→`Ready`, `Archive` |
| `Archived` | Retirada (soft) | — (terminal) |

**Envío inmediato (fix #10):** `TriggerNow` desde `Draft` **no** salta a un estado `Sending`. Ejecuta un único recorrido interno: **validar → `MarkReady` interno → crear `CampaignRun`**. No existe estado `Sending` en la Campaign — eso es del run. La API expone `send-now`; internamente aplica esa secuencia (documentado en `API_Contracts.md`). Una Campaign `Scheduled` recurrente **permanece `Scheduled`** y genera **N CampaignRun** (corrige el reset destructivo del legado `CampaignSchedulerBackgroundService.cs:124-135`).

---

## 2. CampaignRun (una ejecución)

El run es inmutable en su snapshot; solo su estado, contadores y flags de progreso mutan.

```
   StartCampaignRun (send-now, o lease del Scheduler)
          │
          ▼
     ┌─────────┐  (persiste DispatchRun en la MISMA tx → outbox; ver Transactional_Protocol §T1)
     │ Created │
     └────┬────┘
  gate check │ module.campaigns (Subscription, enforce en prod) — falla → Rejected
          ▼
     ┌──────────────┐  materializa audiencia por PÁGINAS (Clients+Listas+Manual, aplica opt-out)
     │ Materializing│  → una unidad (CampaignRecipient) por (contacto, canal); Skip los suprimidos
     └────┬─────────┘  al terminar: congela recipient_count (total de unidades) + materialization_complete
          ▼
     ┌───────────┐  fan-out: 1 dispatch por UNIDAD (idempotente por dispatch_id), por lotes con checkpoint
     │ Dispatching│  al terminar la emisión: emission_complete
     └────┬──────┘
          │ cierre (ver predicado abajo)
          ▼
     ┌───────────────────────────────────────┐
     │ Completed | PartiallyFailed | Failed   │ (terminal) ── contadores finales congelados
     └───────────────────────────────────────┘

   Cancel → Cancelling → Cancelled  (deja de emitir; drena/vence in-flight; conserva lo ya entregado)
```

| Estado | Significado | Salientes |
|---|---|---|
| `Created` | Snapshot congelado; `DispatchRun` encolado durable | `Materializing`, `Rejected` |
| `Materializing` | Resolviendo audiencia por páginas; creando unidades | `Dispatching`, `Cancelling`, `Rejected` |
| `Dispatching` | Fan-out por unidad en curso (lotes con checkpoint) | `Completed`, `PartiallyFailed`, `Failed`, `Cancelling` |
| `Cancelling` | Cancelación solicitada; drenando in-flight | `Cancelled` |
| `Completed` | Cerró sin fallos ni desconocidos | — terminal |
| `PartiallyFailed` | Cerró con ≥1 `Failed` o ≥1 `Unknown`, y ≥1 `Delivered/Accepted` | — terminal |
| `Failed` | Cerró sin ningún `Delivered/Accepted` (todo Failed/Skipped/Unknown) | — terminal |
| `Cancelled` | Cancelado | — terminal |
| `Rejected` | Nunca despachó (gate `module.campaigns` inactivo) | — terminal |

**Guards y predicado de cierre (fix #01):**
- `Created → Materializing → Dispatching` solo si el gate `module.campaigns` está **activo** (enforce en producción; el modo log-only es solo una etapa de rollout acotada, ver `Security.md` y #17).
- El run cierra **solo cuando**: `materialization_complete` ∧ `emission_complete` ∧ **no quedan unidades `Pending` ni `Dispatched` vivas** (las `Dispatched` vencidas pasan a `Unknown` por el sweeper). Equivale al invariante de conteo:
  `delivered + accepted + failed + skipped + unknown == recipient_count`  (total **congelado** de unidades).
- El estado terminal se deriva de los contadores: `Failed` si `delivered+accepted == 0`; `Completed` si `failed+unknown == 0`; `PartiallyFailed` en el resto.
- **No se cierra contra `Dispatched`** (que nunca incluye los `Skipped`). El total autoritativo es `recipient_count`.
- CAS sobre `run_status` + RowVersion garantiza cierre único (`Concurrency_Spec.md §4`). El cierre consulta una **condición autoritativa** (conteo sobre unidades o contador reconciliado), no una caché posiblemente atrasada.

---

## 3. CampaignRecipient (una unidad destinatario/canal)

Reemplaza el `RecipientStatus` legado de 9 valores (`RecipientStatus.cs`) que mezclaba dispatch con tracking.

```
     materialize  (una unidad por (contacto, canal))
        │
        ├─► Skipped   (opt-out del canal, sin destino válido, remitente no verificado) — terminal
        ▼
   ┌─────────┐  dispatch emitido (dispatch_id = f(run_id, recipient_id, channel, attempt_no))
   │ Pending │
   └────┬────┘
        ▼
   ┌────────────┐   result del consumer (correlacionado por dispatch_id)
   │ Dispatched │
   └────┬───────┘
        ├─ Accepted   (proveedor aceptó para procesar; puede avanzar por webhook)
        │     ├─ Delivered  (entrega confirmada por webhook) — terminal
        │     └─ Failed     (fallo confirmado posterior) — terminal
        ├─ Failed     (rechazo/fallo confirmado sin aceptación) — terminal
        └─ Unknown    (timeout / sin confirmación tras dispatch_deadline) — reconciliable
```

| Estado | Significado | ¿Cuenta para cierre? |
|---|---|---|
| `Pending` | Materializado, aún no despachado | no (bloquea cierre) |
| `Dispatched` | Evento emitido, esperando result | no (bloquea cierre hasta `Unknown` por deadline) |
| `Accepted` | Proveedor aceptó para procesar | **sí** (settled; puede refinarse a Delivered/Failed) |
| `Delivered` | Entrega confirmada (webhook) | **sí** (terminal) |
| `Failed` | Fallo confirmado (`Reason`) | **sí** (terminal) |
| `Skipped` | No se intentó (opt-out / sin destino / remitente inválido) | **sí** (terminal) |
| `Unknown` | Sin confirmación tras `dispatch_deadline` | **sí** (settled con incertidumbre; reconciliable) |

**Accepted vs Delivered (fix #03):** un `200 OK` del proveedor o "encolado" es **`Accepted`**, no `Delivered`. `Delivered` requiere confirmación posterior (webhook). Si un canal en el MVP **no** provee confirmación de entrega, `Accepted` es su outcome final y así se reporta (no se disfraza de `Delivered`).

**Timeout → `Unknown`, no `Failed` (fix #04):** cuando falta el result tras `dispatch_deadline`, la unidad pasa a `Unknown` (settled para permitir el cierre), **no** a `Failed`. Un `Delivered`/`Failed` que llegue después **reconcilia** `Unknown` → estado real (auditado), sin doble conteo. Nunca se convierte "perdí la respuesta" en "no se envió"; antes de crear un nuevo intento se reconcilia con el ejecutor/proveedor.

**Política de envío en la unidad (§7.5 confirmada):**
- **Quiet hours = diferir, no omitir:** una unidad fuera de la ventana del contacto queda `Pending` con `eligible_at_utc = nextEligibleAt`; el fan-out solo emite unidades con `eligible_at_utc <= now`. No es `Skipped`; el run sigue `Dispatching` hasta enviarlas (están programadas, no "stuck").
- **Frequency cap:** al emitir, si el `contact_send_ledger` ya alcanzó `MaxSendsPerContactPerWindow` → `Skipped(frequency_cap)` (chequeo atómico entre campañas, `Data_Model.md §1.5b`).
- **Preferencia de canal:** en la materialización, un canal no permitido/preferido por el contacto → `Skipped(channel_pref)`.

**Reconciliación tardía:** transiciones permitidas post-settle: `Unknown → Delivered/Failed`, `Accepted → Delivered/Failed`. Ajustan contadores de forma auditada aunque el run ya haya cerrado (corrección, no reapertura).

---

## 3.1 Intentos (DispatchAttempt) — fix #09

Un **reintento legítimo** (el anterior falló de forma transitoria) es un **nuevo intento**, no un recipient nuevo:

- `recipient_id` es **estable por unidad** `(run_id, contactRef, channel)`. El `contactRef` es un id estable incluso para contactos **manuales** (se asigna un id generado por entrada manual; nunca el literal `"manual"`, que colisionaría — fix #09).
- Cada intento es una fila `campaign_dispatch_attempt` con `UNIQUE(run_id, recipient_id, attempt_no)` y su propio `dispatch_id`. El recipient guarda `current_attempt_no` y su `outcome` final.
- **Reentrega del bus del MISMO intento** (mismo `dispatch_id`) → no-op idempotente. **Nuevo intento de negocio** (`attempt_no+1`) → fila nueva, `dispatch_id` nuevo.
- El `outcome` final de la unidad = el del último intento settled; un result del intento anterior que llegue tarde se correlaciona por su `dispatch_id` (del intento) y no pisa un intento posterior.

---

## 4. Acoplamiento entre máquinas

| Evento | Recipient (unidad) | RunCounters | RunStatus |
|---|---|---|---|
| materialize unidad | (nace) Pending o Skipped | recipient_count (congelado al fin) | Materializing |
| dispatch emitido | Pending→Dispatched | dispatched++ | Dispatching |
| accepted | Dispatched→Accepted | accepted++ | evalúa cierre |
| delivered | Dispatched/Accepted→Delivered | delivered++ (accepted-- si venía de Accepted) | evalúa cierre |
| failed | Dispatched/Accepted→Failed | failed++ | evalúa cierre |
| skipped | Pending→Skipped (o al materializar) | skipped++ | (no bloquea) |
| timeout (deadline) | Dispatched→Unknown | unknown++ | evalúa cierre |
| reconcile tardío | Unknown/Accepted→Delivered/Failed | ajusta contadores (auditado) | run ya cerrado; corrige stats |

Los contadores son **caché**; la **fuente de verdad** son las unidades (`Concurrency_Spec.md §3`). El cierre evalúa una condición autoritativa sobre unidades, no la caché.

---

## 5. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado aplana definición+ejecución en un `CampaignStatus` de 9 valores | `CampaignStatus.cs:4-12` | VERIFIED | 98% |
| Legado mezcla dispatch+tracking en `RecipientStatus` lineal | `RecipientStatus.cs:4-12` | VERIFIED | 97% |
| Legado resetea la misma fila en recurrencia | `CampaignSchedulerBackgroundService.cs:124-135` | VERIFIED | 96% |
| Result events con correlación opaca de vuelta ya existen | `PostmasterEmailEvents.cs:104` | VERIFIED | 97% |
| Unidad=destinatario/canal; Accepted/Delivered/Unknown; cierre por total congelado | decisiones del usuario 2026-09-17 | DECISION | 99% |
| Cierre por conteo idempotente sobre total congelado | diseño (este doc §2) | NEW | 88% |
