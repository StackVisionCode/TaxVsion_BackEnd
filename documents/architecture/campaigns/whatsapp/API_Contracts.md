# WhatsApp — API Contracts

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — WhatsApp SÍ es un servicio nuevo (`TaxVision.WhatsApp`, Meta/WABA), pero de FASE POSTERIOR y solo como CONSUMER del contrato de dispatch — sin dinero.** Consume `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un Wallet o cobro por este canal queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

El transporte primario es **mensajería (Wolverine outbox/inbox durable)** para dispatch/result (ver `Commands_And_Events.md`). HTTP existe para: (a) el **webhook público de Meta**, (b) endpoints M2M de administración de plantillas/config, (c) un endpoint M2M de **envío individual** (SMS-suelto-equivalente para WhatsApp). **Todo endpoint público lleva `[RateLimit(categoría)]` o `[RateLimitExempt]`** (ver `documents/.../RateLimit/Guia_Nuevos_Servicios_Endpoints.md`). Sin tipos compartidos entre contexts: los contratos de dispatch/result se **copian por contexto** (schema estable, no referencia a assembly ajeno).

## 1. Webhook público de Meta (entrada de estados + inbound)

### `GET /webhooks/whatsapp` — verification handshake
- Meta llama con `hub.mode`, `hub.verify_token`, `hub.challenge`.
- `[RateLimitExempt]` (handshake de proveedor; protegido por verificación de token + firma).
- Valida `hub.verify_token` contra el secreto configurado; responde `hub.challenge` en 200. Token inválido ⇒ 403.

### `POST /webhooks/whatsapp` — status + inbound messages
- `[RateLimitExempt]` (proveedor; se protege por **firma `X-Hub-Signature-256` (HMAC-SHA256 con App Secret)** — ver `Security.md`). Cuerpo sin firma válida ⇒ 401, **no** se procesa.
- Payload de Meta: `entry[].changes[].value` con:
  - `statuses[]`: `{ id (wamid), status (sent|delivered|read|failed), timestamp, recipient_id, conversation{id,origin{type}}, errors[] }`. (Campo `pricing{...}` de Meta — ignorado: sin dinero en el canal, ver banner.)
  - `messages[]`: inbound del usuario (abre/renueva `SessionWindow`).
- **Respuesta inmediata 200** tras persistir el envelope crudo (Wolverine inbox durable) — el procesamiento de negocio es asíncrono e idempotente (ver `Idempotency_Spec.md`). Nunca se hace trabajo pesado síncrono en el hilo del webhook (evita reintentos de Meta por timeout).

Contrato interno normalizado que emite el webhook handler (evento):
```
WhatsAppDeliveryStatusReceived {
  ProviderMessageId: string    // wamid
  Status: enum(Sent|Delivered|Read|Failed)
  OccurredAtUtc: DateTime
  ConversationId?: string
  ConversationCategory?: enum(Marketing|Utility|Authentication|Service)
  // PricingModel / Billable / BilledAmount — (removidos: sin dinero en el canal; ver banner)
  ErrorCode?: string, ErrorDetail?: string
  TenantId, PhoneNumberId
}
```

## 2. Envío individual M2M (`POST /messages`)

Para consumidores que envían un WhatsApp suelto (no campaña). (Autorización por saldo — removida: externa/diferida, ver banner.)
- `[RateLimit(Category = "messaging-send")]`, RBAC **M2M client-credentials** con `audience = taxvision.whatsapp` y `scope = whatsapp:send`; `[HasPermission]` acumulativo; tenant explícito en el token; ownership del `PhoneNumberId` verificado.
- Header `Idempotency-Key` **obligatorio** (ver `Idempotency_Spec.md`).
- Request:
```
SendWhatsAppMessageRequest {
  ToPhoneE164: string
  RecipientRef?: string
  Template: { Name, Language, Variables: { "1": "...", ... }, Buttons?: [...] }  // requerido fuera de sesión
  FreeText?: string                                                             // solo si sesión abierta
  Category?: enum                                                               // derivada de la plantilla si se omite
  CampaignId?: Guid                                                             // opaco, opcional
}
```
- Response `202 Accepted`: `{ DispatchId, Status: "Accepted"|"Rejected", FailureCode? }`. El resultado de entrega llega después por el flujo de result/webhook, no en esta respuesta (evita el TOCTOU síncrono del legado).
- **Precondición de saldo** — (removida: sin dinero en el canal; la autorización por balance es un interceptor/PEP externo y diferido, ver banner).

## 3. Administración de plantillas / config (M2M)

| Endpoint | RateLimit | Scope M2M | Nota |
|---|---|---|---|
| `POST /templates/sync` | `admin` | `whatsapp:templates:admin` | fuerza pull del catálogo desde Meta |
| `GET /templates` | `read` | `whatsapp:templates:read` | catálogo local (tenant-scoped) |
| `PUT /provider-config` | `admin` | `whatsapp:config:admin` | setea WABA/PhoneNumberId/token (**token nunca en logs**; se cifra) |

— (removido: sin dinero en el canal; no se acepta ni gestiona precio por mensaje aquí; la autorización por balance es externa/diferida, ver banner).

## 4. Contrato dispatch/result (bus, común a todos los canales)

Idéntico en forma al de Email/SMS/Push; ver `Commands_And_Events.md §1`. Resumen del seam:
- **Entra**: `WhatsAppDispatchRequested` (Campaigns → WhatsApp) con `DispatchId, CampaignId, CampaignRunId, RecipientRef, ToPhoneE164, TemplateRef|FreeText, Variables`. (`ReservationRef` — removido: sin dinero en el canal, ver banner.)
- **Sale**: `WhatsAppDispatchResult` (WhatsApp → Campaigns) con `DispatchId, CampaignId (eco intacto), Outcome (Sent|Delivered|Read|Failed|Rejected), ProviderMessageId?, FailureCode?`. (`BilledAmount?` — removido: sin dinero en el canal, ver banner.)
- `CampaignId` se **transporta y devuelve intacto, nunca se interpreta** (mismo patrón que `PostmasterEmailEvents.cs:37`).

## 5. Evidencia

| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Convención `[RateLimit]`/`[RateLimitExempt]` obligatoria | `documents/.../RateLimit/Guia_Nuevos_Servicios_Endpoints.md` (citado en anchors) | VERIFIED | 90% |
| Seam CampaignId eco-intacto | `PostmasterEmailEvents.cs:37,103-104` | VERIFIED | 95% |
| Legado enviaba síncrono sin webhook (TOCTOU) | `WhatsAppCampaignSender.cs:77-101` | VERIFIED | 96% |
| Firma HMAC + verify token del webhook Meta | Meta Cloud API docs | DOCUMENTED_ONLY | 88% |
