# Campaigns — Transactional Protocol

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-17 (revisión v2 — hallazgos #01/#04/#07/#08/#11/#12; **sin dinero**)
- **Estado:** DISEÑO — no implementado

> **Regla dura:** **Campaign NO maneja balance/monto/costo.** Orquesta solo: `resolver audiencia → fan-out dispatch por unidad → recolectar result → cerrar run por conteo`. Ningún paso reserva/consume/cobra. El dinero, si entra, lo hace un **PEP externo** que intercepta el trigger (ver §7, `../05_Master_ADR.md` D7).

Campaigns es un **orquestador de saga** (no 2PC). Cada paso es una transacción local (muta un aggregate + escribe outbox en la MISMA tx) más un evento; las compensaciones son explícitas. Unidad de trabajo = **destinatario/canal** (ver `State_Machines.md §v2`).

---

## 1. Por qué saga y no el patrón legado

El legado hacía **fan-out fire-and-forget** en memoria (`Task.Run`/`Task.Delay`, `CampaignSchedulerBackgroundService.cs:38,78-95`) que se **perdía al reiniciar**, y marcaba `Sent` a todos sin clave por destinatario (`CampaignSendService.cs:63-71`). Aquí todo progreso es **durable** (outbox + checkpoints) e **idempotente**.

---

## 2. Flujo feliz (saga, sin dinero)

Cada `[Tn]` es una transacción local. **Ningún paso queda sin su continuación durable** (fix #08): la mutación y el mensaje que dispara el siguiente paso se escriben juntos.

```
[T1] StartCampaignRun
     tx local: crea campaign_run (Created) + congela snapshot (canal/audiencia/plantilla/remitente)
               outbox (MISMA tx): DispatchRun{ runId }   ← continuación durable, NO "on Created" suelto
     Guard: gate module.campaigns activo. Si no → Rejected (nada que compensar).

[T2] on DispatchRun → materialización PAGINADA (fix #12)
     bucle por páginas de audiencia (cursor durable audience_cursor):
       tx local por página: inserta unidades campaign_recipient (Pending) — UNA por (contacto, canal);
                            marca Skipped las suprimidas/sin destino/remitente no verificado;
                            avanza audience_cursor.
       outbox por página: EmitDispatchBatch{ runId, page }  (o el propio bucle continúa por outbox)
     al terminar la última página:
       tx local: recipient_count = COUNT(unidades) (TOTAL CONGELADO); materialization_complete=true;
                 run Created→Materializing→Dispatching (o directo Dispatching con flag).

[T3] EmitDispatchBatch (fan-out por lotes, fix #12; respeta política §7.5)
     tx local por lote: por cada unidad Pending del lote con eligible_at_utc <= now:
        - frequency cap: incremento atómico en contact_send_ledger; si cap alcanzado → Skipped(frequency_cap), NO se emite;
        - si pasa → outbox CampaignDispatchRequested (unidad Pending→Dispatched; dispatched++).
        Las unidades con eligible_at_utc > now (quiet hours) se DIFIEREN (siguen Pending); no cuentan como emitidas.
        avanza emit_cursor.
     emission_complete=true SOLO cuando no quedan unidades Pending con eligible_at_utc<=now sin emitir.
     [T3b] Wake de diferidas: un job durable re-evalúa unidades Pending con eligible_at_utc<=now y las emite (mismo camino).

     ── cada consumer (Notification/Email, TaxVision.Sms, …) entrega y reporta ──►
     campaign.dispatch.result.v1{ dispatchId, outcome: Accepted|Delivered|Failed|Skipped, providerRef?, reason? }
        (N eventos, at-least-once, posiblemente duplicados/fuera de orden)

[T4] on DispatchResult (por unidad, idempotente por dispatch_id)
     tx local: avanza la unidad (Dispatched→Accepted/Delivered/Failed; Accepted→Delivered/Failed);
               contador++ una sola vez. Evalúa cierre.

[T5] CloseRun (cuando el predicado de cierre se cumple)
     tx local: run Dispatching→ Completed | PartiallyFailed | Failed (según contadores); congela finales.
     outbox: campaign.run.completed.v1 (reporting; y disponible a un Wallet externo futuro).
```

**Predicado de cierre (fix #01):** el run cierra cuando `materialization_complete ∧ emission_complete ∧` no quedan unidades `Pending` ni `Dispatched` vivas. Invariante de conteo sobre el **total congelado**:
`delivered + accepted + failed + skipped + unknown == recipient_count`.
Estado terminal derivado: `Failed` si `delivered+accepted==0`; `Completed` si `failed+unknown==0`; `PartiallyFailed` en el resto. **Nunca** se cierra contra `Dispatched` (no incluye `Skipped`).

**No hay reserva ni cobro.** El run se define por su resultado de **entrega**, no por dinero.

---

## 3. Compensaciones y fallos

| Fallo | Estado | Compensación |
|---|---|---|
| Gate `module.campaigns` inactivo | `Created` | run → `Rejected`; no se emite ningún dispatch |
| Crash entre T1 y T2 (fix #08) | `Created` | `DispatchRun` está en la outbox (misma tx de T1) → se reentrega y reanuda; backstop: sweeper de `Created` sin progreso |
| Crash a mitad de materialización (fix #12) | `Materializing` | `audience_cursor` durable → reanuda desde la última página; unidades ya creadas no se duplican (unique por unidad) |
| Crash a mitad de fan-out | `Dispatching` | `emit_cursor` + outbox → reenvía lotes pendientes; unidades ya `Dispatched` no se re-emiten (`UNIQUE(run_id, dispatch_id)`) |
| Result nunca llega | `Dispatched` (stuck) | sweeper: tras `dispatch_deadline`, unidad → `Unknown` (idempotente), **no** `Failed` (fix #04) → permite cierre |
| Result duplicado / fuera de orden | cualquiera | guard de estado por `dispatch_id`: 2ª vez no-op; sin doble conteo |
| Result tardío tras `Unknown` | cerrado | reconcilia `Unknown → Delivered/Failed` (auditado), sin reabrir el run ni doblar conteo |
| `CancelRun` con envíos en vuelo (fix #11) | `Dispatching→Cancelling` | deja de emitir lotes nuevos; los ya emitidos drenan o vencen a `Unknown`; cierra `Cancelled`. **No** revoca lo ya aceptado por el proveedor (límite explícito, ver §3.1) |

### 3.1 Límite real de cancelación (fix #11)
La cancelación promete: **detener trabajo aún no emitido** + **drenar lo emitido** + **conservar lo ya entregado**. Como el fan-out se emite **por lotes** (no todo de golpe), cancelar entre lotes evita emitir el resto. Lo ya `Dispatched`/`Accepted` por el proveedor **no se puede revocar**; existe una carrera final inevitable. No se promete revocación total.

---

## 4. Atomicidad local (outbox)

Cada `tx local` escribe la mutación **y** el mensaje saliente en la **misma transacción** (Wolverine transactional outbox). No hay `Task.Run`/`Task.Delay` (anti-patrón legado `CampaignSchedulerBackgroundService.cs:38`). Un registro persistido **siempre** persiste también su siguiente paso (fix #08): no hay "on Created" sin un mensaje durable que lo dispare.

---

## 5. Cierre por conteo (durable, no "último callback")

El cierre no depende de recibir el "último" result (puede duplicarse o desordenarse). Se evalúa por el invariante de §2 sobre el **total congelado**. Como cada unidad settlea una sola vez (guard), el conteo es monótono y el predicado estable. Dos results terminales concurrentes: un CAS sobre `run_status` deja pasar un solo cierre; si ninguno "ve" al otro antes del commit, un **reconciliador de cierre** (job) reevalúa el predicado de forma durable (`Concurrency_Spec.md §4`).

---

## 6. Idempotencia de la saga

| Paso | Clave | Constraint |
|---|---|---|
| StartCampaignRun | `occurrence_key` | `UNIQUE(tenant, campaign_id, occurrence_key)` |
| materialización de unidad | `(run_id, contactRef, channel)` | `UNIQUE(run_id, recipient_id)` |
| dispatch por intento | `dispatch_id = f(run_id, recipient_id, channel, attempt_no)` | `UNIQUE(run_id, dispatch_id)` |
| dispatch result | mismo `dispatch_id` | guard de estado de la unidad |
| cierre | `run_status` guard | CAS + RowVersion |

Detalle en `Idempotency_Spec.md`. **Ninguna clave es monetaria.**

---

## 7. Dónde encajaría el dinero (futuro, fuera de Campaign)

Dos piezas externas y **DIFERIDAS**:

**(a) Interceptor/PEP** delante de la ejecución (trigger manual **y** cada `RunDue`): calcula el `recipientCount` de ese disparo (+recurrencia), consulta el Wallet, **cobra/reserva antes** y **veta** si no alcanza. Campaign recibe solo triggers autorizados. **Importante (fix #20):** como Campaign materializa la audiencia **después**, el PEP debe autorizar contra un **manifiesto inmutable del trabajo** (run + versión de definición + audiencia + contenido) que Campaign prepara y expone; la autoridad aprueba una **identidad opaca** que todas las rutas de ejecución verifican. No es un campo de dinero en Campaign.

**(b) Wallet** externo: saldo USD, movimientos inmutables; responde al PEP y/o se suscribe a `campaign.run.started/dispatch.result/run.completed.v1` para medir consumo real. Nota: `campaign.run.started` **no** sirve para frenar (el run ya arrancó); el freno es el PEP **antes** del trigger.

**Hoy:** sin PEP ni Wallet, el seam está **abierto** (todo trigger autorizado). Ver `Domain_Design.md §8.1`, `../wallet-ledger/` (DIFERIDO).

---

## 8. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado: fan-out volátil en memoria (se pierde al reiniciar) | `CampaignSchedulerBackgroundService.cs:38,78-95` | VERIFIED | 95% |
| Legado: marca Sent a todos sin clave por destinatario | `CampaignSendService.cs:63-71` | VERIFIED | 97% |
| Correlación opaca devuelta por el ejecutor | `PostmasterEmailEvents.cs:104` | VERIFIED | 95% |
| Continuación durable T1→T2, materialización/fan-out paginados, cierre por total congelado | diseño v2 (este doc) + decisiones 2026-09-17 | NEW/DECISION | 88% |
| Timeout → Unknown (no Failed); reconciliación tardía | decisión 2026-09-17 | DECISION | 99% |
