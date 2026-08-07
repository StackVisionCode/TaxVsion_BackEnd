# Email (SMTP2GO) — Observability

- Servicio: **TaxVision.Campaigns.Email**
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. Correlación end-to-end
Toda traza/log/metric lleva la tripleta opaca **`CampaignId` + `CampaignRunId` + `RecipientId`** (+ `Attempt`), el mismo seam que ya viaja Notification→Postmaster (`PostmasterEmailEvents.cs:37,103`). Más `TenantId` y `ProviderMessageId` (`email_id` SMTP2GO) para saltar de nuestro dominio al panel del proveedor. `IdempotencyKey` como clave de deduplicación en logs.

## 2. Métricas (OpenTelemetry)
| Métrica | Tipo | Labels | Uso |
|---|---|---|---|
| `email.dispatch.processed` | counter | tenant, result(sent/failed/suppressed) | throughput y tasa de fallo pre-provider |
| `email.provider.request.duration` | histogram | tenant, status_class | latencia del POST a SMTP2GO |
| `email.provider.request.errors` | counter | tenant, kind(5xx/4xx/timeout) | salud del proveedor |
| `email.dispatch.terminal` | counter | tenant, status(delivered/bounced/complained/failed) | outcome real (por webhook) |
| `email.suppression.hits` | counter | tenant, reason | cuántos envíos se evitan por suppression |
| `email.webhook.received` | counter | tenant, type, signature_valid | volumen y firmas inválidas |
| `email.reconciler.repaired` | counter | tenant, action(marked_failed/transitioned) | huérfanos `Pending` barridos |
| `email.provider.rate_limit.throttled` | counter | credential | backpressure activo |
| `email.dispatch.pending.age` | gauge | — | detección de atascos (SLO) |

Derivadas (dashboards): **bounce rate**, **complaint rate**, **delivery rate** por tenant/run — las mismas señales que el legado leía del proveedor (`Smtp2GoService.cs:608-611`) pero calculadas desde **nuestros** result events deduplicados, no de un scrape.

## 3. Trazas (spans)
```
consume dispatch_requested
  ├─ span: dedupe.check
  ├─ span: suppression.check
  ├─ span: render.scribe            (si aplica)
  ├─ span: provider.smtp2go.send    (attrs: provider_message_id, http.status)
  └─ span: outbox.emit.result
apply webhook
  ├─ span: signature.verify
  ├─ span: dedupe.check
  └─ span: dispatch.transition      (attrs: from_status, to_status)
```
Trace context se propaga por el bus (Wolverine) desde Campaigns.

## 4. Logs (estructurados)
- Nivel INFO: 1 log por transición terminal con `{tenant, campaignId, runId, recipientId, attempt, status, providerMessageId}`.
- **Nunca** loguear: `ApiKey`, HTML/cuerpo del email, PII más allá del address hasheado/parcial en niveles no-debug. El legado logueaba el email en claro con emojis (`Smtp2GoService.cs:202-206`) — se restringe.
- Firmas de webhook inválidas ⇒ WARN con IP/origen (señal de abuso), sin el payload completo.

## 5. Alertas / SLO
| Alerta | Condición |
|---|---|
| Provider degradado | `email.provider.request.errors{kind=5xx}` > umbral en 5m |
| Bounce/complaint spike | complaint rate por tenant supera umbral (riesgo de reputación/blacklist) |
| Firmas inválidas | `email.webhook.received{signature_valid=false}` > 0 sostenido (posible ataque) |
| Atasco de dispatch | `email.dispatch.pending.age` p99 > SLO |
| Reconciliador inactivo | ausencia de heartbeat del job con lease |

Complaint rate alta es crítica: SMTP2GO puede suspender la cuenta; alertar antes del umbral del proveedor.

## 6. Auditoría
- `inbound_webhook_event.raw_payload` (jsonb) = registro auditable inmutable de lo que el proveedor reportó.
- Toda transición de `email_dispatch` es reconstruible desde los result events (event log) + timestamps por estado.
- Cambios de `provider_credential` (rotación de key, verificación) auditados con actor + `key_version`.

## 7. Evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Seam `CampaignId` correlacionable end-to-end | `PostmasterEmailEvents.cs:37,103` | VERIFIED | 96% |
| Legado leía bounce/spam rate del proveedor | `Smtp2GoService.cs:608-611` | VERIFIED | 90% |
| Legado logueaba email en claro | `Smtp2GoService.cs:202-206` | VERIFIED | 88% |
| Métricas/trazas OTel de este servicio | este diseño | NEW | n/a |
