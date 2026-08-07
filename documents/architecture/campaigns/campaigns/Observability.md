# Campaigns — Observability

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Objetivo: hacer **auditable y depurable** la saga distribuida (balance + dispatch) que el legado hacía opaca (fan-out fire-and-forget en memoria, sin correlación end-to-end, "éxito" declarado sin confirmación real). Todo instrumentado sobre la correlación opaca `runId` / `dispatch_idempotency_key` que ya viaja en los eventos.

---

## 1. Correlación

- **`runId`** correla toda la vida de una ejecución (start → reserve → dispatch → results → reconcile → complete).
- **`dispatch_idempotency_key`** correla un destinatario a través del boundary con el ejecutor y de vuelta (mismo patrón que `CampaignId` en `PostmasterEmailEvents.cs:37,104` — el ejecutor lo devuelve intacto).
- **`campaignId`** agrupa runs.
- **`tenantId`** en todo log/span/métrica (multi-tenant; nunca cruzar tenants en dashboards).
- OpenTelemetry trace context propagado en los envelopes Wolverine (el span del handler enlaza con el del emisor).

---

## 2. Trazas (spans)

Spans por paso de saga, atributos `runId`, `campaignId`, `tenantId`, `channel`:

```
campaigns.run.start          → recipient_count, unit_price_minor, cost_estimate_minor, gate_active
campaigns.run.reserve        → amount_minor, reservation_id (al confirmar)
campaigns.run.dispatch        → dispatched_count (span padre del fan-out)
  └─ campaigns.recipient.dispatch (por N)  → recipient_id, attempt_no  [muestreado]
campaigns.recipient.result   → outcome, provider_message_id           [muestreado]
campaigns.run.reconcile      → delivered, refunded_minor, consumed_minor
campaigns.run.complete       → cost_actual_minor, duration
```

El fan-out por destinatario se **muestrea** (p.ej. head-sampling + siempre-on para errores) para no explotar el volumen de spans en runs de 100k destinatarios.

---

## 3. Métricas (nombres propuestos, prefijo `campaigns_`)

| Métrica | Tipo | Labels | Uso |
|---|---|---|---|
| `campaigns_run_started_total` | counter | tenant, channel, trigger_kind | volumen |
| `campaigns_run_rejected_total` | counter | tenant, reason (gate/insufficient) | separar gate vs balance |
| `campaigns_run_completed_total` | counter | tenant, channel | throughput |
| `campaigns_run_duration_seconds` | histogram | channel | start→complete |
| `campaigns_dispatch_total` | counter | channel | fan-out emitido |
| `campaigns_dispatch_result_total` | counter | channel, outcome (delivered/failed/suppressed/bounced) | tasa de entrega |
| `campaigns_recipient_stuck_total` | counter | channel | recipients marcados timeout por el sweeper |
| `campaigns_cost_estimate_minor` / `_actual_minor` | histogram | channel | drift estimado vs real |
| `campaigns_wallet_reserve_minor` / `_consume_minor` / `_refund_minor` | counter | tenant | reconciliación financiera |
| `campaigns_idempotency_hit_total` | counter | operation | dedupe efectivo (redelivery absorbido) |
| `campaigns_saga_inflight` | gauge | run_status | runs por estado (detecta stuck) |
| `campaigns_outbox_lag_seconds` | gauge | — | salud de la outbox Wolverine |

**Métrica financiera crítica:** `reserved == consumed + refunded` por run debe cerrar. Una alerta sobre el desbalance detecta bugs de liquidación (el legado no tenía forma de verificar esto — refund frágil dependiente de JWT, `CampaignSendService.cs:120-146`).

---

## 4. Logs estructurados

- Nivel INFO en transiciones de saga (con `runId`), WARN en compensaciones/timeouts, ERROR en fallos no idempotentes.
- **Nunca** loggear PII cruda (email/phone) ni tokens. Loggear `recipientId`/`contactRef` opacos. Corrige el legado, que loggeaba montos/refs con emojis y datos sensibles a granel.
- **Nunca** loggear el JWT ni secretos (el legado ni siquiera debería tener JWT — `Campaign.BackgroundAuthToken`, `Campaign.cs:87`).
- Log de auditoría separado para efectos financieros (reserve/consume/refund) con `runId`, `amount_minor`, `reservation_id`, `idempotency_key`.

---

## 5. Dashboards / alertas

| Alerta | Condición | Severidad |
|---|---|---|
| Saga stuck | `campaigns_saga_inflight{run_status=Dispatching}` sin bajar > 30min | alta |
| Desbalance financiero | `reserve != consume + refund` por run (job de reconciliación) | crítica |
| Outbox lag | `campaigns_outbox_lag_seconds > 60` | alta |
| Tasa de fallo de dispatch | `failed/(delivered+failed) > umbral` por channel | media |
| Rechazos por saldo | pico de `run_rejected_total{reason=insufficient}` | info (UX: sugerir top-up) |
| Recipients stuck | `campaigns_recipient_stuck_total` creciente | media (salud del ejecutor) |

---

## 6. Auditoría de negocio

Cada run es un registro **inmutable** auto-auditable: snapshot congelado + cost estimate/actual + reservation id + contadores finales. A diferencia del legado (recurrentes que mutan una fila, `CampaignSchedulerBackgroundService.cs:124-135`, borrando el historial), aquí cada ejecución deja su propia evidencia. `campaigns.run.completed.v1` alimenta el read-model de reporting.

---

## 7. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Correlación opaca devuelta por el ejecutor ya existe | `PostmasterEmailEvents.cs:37,104` | VERIFIED | 97% |
| Legado: refund frágil sin verificación de balance financiero | `CampaignSendService.cs:120-146` | VERIFIED | 94% |
| Legado persiste JWT (no loggear/tener) | `Campaign.cs:87` | VERIFIED | 97% |
| Legado: recurrentes mutan una fila (sin auditoría por run) | `CampaignSchedulerBackgroundService.cs:124-135` | VERIFIED | 96% |
| Métricas/spans/alertas propuestas | diseño (este doc) | NEW | 84% |
