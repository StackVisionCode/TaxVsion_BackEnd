# WhatsApp — Observability

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — WhatsApp SÍ es un servicio nuevo (`TaxVision.WhatsApp`, Meta/WABA), pero de FASE POSTERIOR y solo como CONSUMER del contrato de dispatch — sin dinero.** Consume `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un Wallet o cobro por este canal queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. Correlación
- **`DispatchId`** (por destinatario) y **`CampaignId`/`CampaignRunId`** (opacos) atraviesan logs, traces y eventos, extremo a extremo (dispatch→Meta→webhook→result). Mismo patrón de correlación eco-intacto que `PostmasterEmailEvents.cs:37`.
- **`wamid`** (ProviderMessageId) enlaza nuestro registro con el ticket de Meta para soporte.
- Trace spans: `wa.accept` → `wa.send(Meta POST)` → `wa.webhook.status`. (Span `wa.settle` — removido: sin dinero en el canal, ver banner.) El POST a Meta y la recepción del webhook son spans separados (no hay trace continuo cross-provider; se unen por `wamid`).

## 2. Métricas (dimensiones: tenant, channel=whatsapp, category, country, phone_number_id)
| Métrica | Tipo | Uso |
|---|---|---|
| `wa_dispatch_accepted_total` / `wa_dispatch_rejected_total{reason}` | counter | validación (sin plantilla, sesión cerrada, número inválido) |
| `wa_sent_total` / `wa_delivered_total` / `wa_read_total` / `wa_failed_total{code}` | counter | embudo de entrega real (imposible en el legado simulado) |
| `wa_send_latency` (accept→sent) y `wa_delivery_latency` (sent→delivered) | histogram | salud de Meta |
| `wa_webhook_lag` (occurredAt→processedAt) | histogram | atraso de procesamiento de webhooks |
| `wa_reaper_timeout_total` | counter | mensajes `Sent` sin webhook (alerta si sube) |
| `wa_template_status{status}` | gauge | plantillas Approved/Paused/Rejected por tenant |
| `wa_rate_limited_total` | counter | throttling de Meta (backpressure) |

> Métricas `wa_billed_amount_cents`, `wa_consume_total`, `wa_refund_total` — (removidas: sin dinero en el canal; ver banner).

## 3. Alertas
- `wa_failed_total` ratio alto por `code` (p.ej. `131047` re-engagement, `132xxx` plantilla) → problema de plantilla/opt-in.
- `wa_reaper_timeout_total` creciente → webhooks no llegan (firma mal configurada, URL caída) o Meta degradado.
- (Alerta de fuga de settlement `wa_consume_total` ≠ `wa_delivered_total` — removida: sin dinero en el canal, ver banner.)
- `wa_webhook_lag` alto → inbox saturado / réplicas insuficientes.
- Firma de webhook inválida repetida → posible mala config del App Secret o ataque; alerta de seguridad.

## 4. Logs (estructurados, sin PII sensible)
- Se loguea `DispatchId, CampaignId, wamid, Status, Category, FailureCode`, tenant.
- **Nunca** se loguea: el `AccessToken`/`AppSecret` de Meta, el cuerpo completo del mensaje al usuario, ni el JWT (el legado persistía `BackgroundAuthToken` — prohibido). El número se loguea enmascarado (`+1809***4567`).
- El envelope crudo del webhook se guarda para auditoría/replay con retención acotada, con secretos redactados.

## 5. Conciliación de costo — (removida)
— (removido: sin dinero en el canal; no hay costeo/facturación ni conciliación con Wallet por este canal; ver banner).

## 6. Evidencia
| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Correlación eco-intacta existente | `PostmasterEmailEvents.cs:37,103-104` | VERIFIED | 95% |
| Legado sin métricas de entrega real (simulado) | `WhatsAppCampaignSender.cs:77-101` | VERIFIED | 96% |
| Legado persistía JWT (a no repetir) | ADR §5 `05_Master_ADR.md:48` | VERIFIED | 93% |
