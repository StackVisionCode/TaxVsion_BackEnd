# Campaigns — Observability

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-09-16 (revisión: **sin métricas/auditoría financiera**)
- **Estado:** DISEÑO — no implementado

Objetivo: hacer **auditable y depurable** la saga distribuida de **dispatch** (resolve → dispatch → result → cierre) que el legado hacía opaca (fan-out fire-and-forget en memoria, sin correlación end-to-end, "éxito" declarado sin confirmación real). Todo instrumentado sobre la correlación opaca `runId` / `dispatch_id` que ya viaja en los eventos.

> **Regla dura:** Campaign no mide dinero. **No hay spans, métricas, logs de auditoría ni alertas de reserve/consume/refund/balance.** La observabilidad es de **entrega** (qué se despachó, qué se entregó/falló/omitió, cuánto tardó).

---

## 1. Correlación

- **`runId`** correla toda la vida de una ejecución (start → dispatch → results → cierre).
- **`dispatch_id`** correla un destinatario a través del boundary con el ejecutor y de vuelta (mismo patrón que `CampaignId` en `PostmasterEmailEvents.cs:37,104` — el ejecutor lo devuelve intacto).
- **`campaignId`** agrupa runs.
- **`tenantId`** en todo log/span/métrica (multi-tenant; nunca cruzar tenants en dashboards).
- OpenTelemetry trace context propagado en los envelopes Wolverine (el span del handler enlaza con el del emisor).

---

## 2. Trazas (spans)

Spans por paso de saga, atributos `runId`, `campaignId`, `tenantId`, `channel`:

```
campaigns.run.start          → recipient_count, gate_active
campaigns.run.dispatch        → dispatched_count (span padre del fan-out)
  └─ campaigns.recipient.dispatch (por N)  → contact_ref, channel, attempt_no  [muestreado]
campaigns.recipient.result   → outcome (delivered/failed/skipped), provider_ref [muestreado]
campaigns.run.close          → delivered, failed, skipped, duration
```

El fan-out por destinatario se **muestrea** (p.ej. head-sampling + siempre-on para errores) para no explotar el volumen de spans en runs de 100k destinatarios.

---

## 3. Métricas (nombres propuestos, prefijo `campaigns_`)

| Métrica | Tipo | Labels | Uso |
|---|---|---|---|
| `campaigns_run_started_total` | counter | tenant, channel, trigger_kind | volumen |
| `campaigns_run_rejected_total` | counter | tenant, reason (gate) | rechazos por gate/entitlement |
| `campaigns_run_completed_total` | counter | tenant, channel, result (completed/partial/cancelled) | throughput |
| `campaigns_run_duration_seconds` | histogram | channel | start→close |
| `campaigns_dispatch_total` | counter | channel | fan-out emitido |
| `campaigns_dispatch_result_total` | counter | channel, outcome (accepted/delivered/failed/skipped/unknown) | tasa de aceptación/entrega |
| `campaigns_recipient_unknown_total` | counter | channel | unidades a `Unknown` por el sweeper (fix #04) |
| `campaigns_dlq_total` | counter | queue | mensajes a DLQ (salud del fan-out) |
| `campaigns_time_to_optout_seconds` | histogram | channel | latencia baja efectiva (privacidad) |
| `campaigns_idempotency_hit_total` | counter | operation | dedupe efectivo (redelivery absorbido) |
| `campaigns_saga_inflight` | gauge | run_status | runs por estado (detecta stuck) |
| `campaigns_outbox_lag_seconds` | gauge | — | salud de la outbox Wolverine |

**Sin métricas monetarias.** La "conservación" a verificar es de **conteo**: `delivered+accepted+failed+skipped+unknown == recipient_count` al cierre.

**Cardinalidad acotada (fix #25):** los IDs individuales (`runId`, `dispatch_id`, `campaignId`) van en **logs/trazas**, **no** como labels de métricas (explotarían la cardinalidad con cada ejecución). Las métricas usan labels acotados (`channel`, `outcome`, `tenant` solo si el nº de tenants es acotado; si no, analítica por tenant desde un almacén con cardinalidad presupuestada).

**SLOs medibles por canal (fix #25):** definir objetivos de **aceptación**, **retraso de cola**, **envío** y **confirmación** por canal; una alerta fija a 30 min no sirve igual para una campaña de 100 vs 100k. Incluir `Unknown`, DLQ, tiempo hasta baja efectiva y reenvíos evitados.

**Auditoría (fix #25):** además de los snapshots finales, auditar **actor + versión** de los cambios de definición y de remitente (quién cambió qué y cuándo), no solo el resultado.

---

## 4. Logs estructurados

- Nivel INFO en transiciones de saga (con `runId`), WARN en compensaciones/timeouts, ERROR en fallos no idempotentes.
- **Nunca** loggear PII cruda (email/phone) ni tokens. Loggear `contact_ref`/`recipientId` opacos. Corrige el legado, que loggeaba refs/datos sensibles a granel.
- **Nunca** loggear el JWT ni secretos (el legado ni siquiera debería tener JWT — `Campaign.BackgroundAuthToken`, `Campaign.cs:87`).
- **Sin log de auditoría financiera** (no hay efectos de dinero que auditar en Campaign).

---

## 5. Dashboards / alertas

| Alerta | Condición | Severidad |
|---|---|---|
| Saga stuck | `campaigns_saga_inflight{run_status=Dispatching}` sin bajar > 30min | alta |
| Outbox lag | `campaigns_outbox_lag_seconds > 60` | alta |
| Tasa de fallo de dispatch | `failed/(delivered+failed) > umbral` por channel | media |
| Recipients stuck | `campaigns_recipient_stuck_total` creciente | media (salud del ejecutor) |
| Conteo no cierra | `dispatched != delivered+failed+skipped` tras cierre (job) | alta (bug de conteo) |

---

## 6. Auditoría de negocio

Cada run es un registro **inmutable** auto-auditable: snapshot congelado + contadores finales de entrega. A diferencia del legado (recurrentes que mutan una fila, `CampaignSchedulerBackgroundService.cs:124-135`, borrando el historial), aquí cada ejecución deja su propia evidencia. `campaign.run.completed.v1` alimenta el read-model de reporting — y queda disponible para consumidores externos (p.ej. un Wallet futuro que quiera medir consumo por su cuenta, sin que Campaign lo sepa).

---

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Correlación opaca devuelta por el ejecutor ya existe | `PostmasterEmailEvents.cs:37,104` | VERIFIED | 97% |
| Legado persiste JWT (no loggear/tener) | `Campaign.cs:87` | VERIFIED | 97% |
| Legado: recurrentes mutan una fila (sin auditoría por run) | `CampaignSchedulerBackgroundService.cs:124-135` | VERIFIED | 96% |
| Observabilidad de entrega, no de dinero | ADR-CAMP-001 D1 (decisión del usuario) | DECISION | 99% |
| Métricas/spans/alertas propuestas | diseño (este doc) | NEW | 84% |
