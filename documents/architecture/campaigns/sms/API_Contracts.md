# TaxVision.Sms — API Contracts

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — SMS NO es un servicio nuevo; `TaxVision.Sms` YA EXISTE y ya es M2M** (`SendSmsBatchCommand`, `POST /sms/messages`, `ActorType.Service`). El canal SMS es un **CONSUMER dentro de `TaxVision.Sms`** que procesa `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio SMS nuevo y/o un Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- **Servicio:** SMS (`TaxVision.Sms`) — consumer del canal SMS **dentro de `TaxVision.Sms` (ya existente)**
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Base de convenciones: TODO endpoint público lleva `[RateLimit("sms", …)]` o `[RateLimitExempt]` (ver `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md` — categoría `sms` a registrar). RBAC acumulativo JWT + actor-type + `[HasPermission]` + tenant + ownership. El `TenantId` **nunca** llega por body: sale del contexto (claim/M2M scope). Corrige el legado, cuyo `SmsController` tomaba `companyId` de un claim custom sin rate-limit ni permiso granular (`SmsController.cs:16,30`).

## 1. HTTP — plano de control (tenant-facing, JWT usuario)

| Método | Ruta | Permiso | RateLimit | Descripción |
|---|---|---|---|---|
| `PUT` | `/api/sms/provider-config` | `sms.config.manage` (Admin/Owner) | `sms` | Crea/actualiza config de proveedor + sender ids; credenciales se cifran server-side (nunca se devuelven). |
| `GET` | `/api/sms/provider-config` | `sms.config.read` | `sms` | Config sin secretos (masked). |
| `POST` | `/api/sms/optin/import` | `sms.optin.manage` | `sms` | Alta de números con `HasPriorConsent` + prueba de consentimiento. |
| `POST` | `/api/sms/optin/{phone}/stop` | `sms.optin.manage` | `sms` | Opt-out manual (equivalente a STOP). Idempotente. |
| `GET` | `/api/sms/optin/{phone}` | `sms.optin.read` | `sms` | Estado de consentimiento. |
| `POST` | `/api/sms/send` | `sms.send` | `sms` | **Envío individual** (no campaña). Body `SendSmsRequest`; requiere `Idempotency-Key` header. — (removido: sin dinero en el canal; ver banner — antes "Consume Wallet"). |
| `POST` | `/api/sms/quote` | `sms.send` | `sms` | Devuelve `(encoding, segments)` sin enviar (preview de segmentación). — (removido: sin dinero en el canal; ver banner — antes `costCents`). |
| `GET` | `/api/sms/dispatches/{id}` | `sms.read` | `sms` | Estado de un dispatch (reemplaza `GET /logs/{id}` legado). |
| `GET` | `/api/sms/dispatches?campaignRunId=&status=&from=&to=` | `sms.read` | `sms` | Listado paginado tenant-scoped. |

### `SendSmsRequest` (individual)
```jsonc
{
  "toPhone": "+15125550123",      // normalizado a E.164 server-side
  "body": "…",                    // o { "templateRef": "...", "variables": {...} }
  "messageClass": "Transactional",// | "Marketing"
  "senderId": "default",          // opcional; resuelve a un SenderId de la config
  "clientRef": "opaque-ref"       // opcional, correlación del caller
}
// Header obligatorio: Idempotency-Key: <string> (≤200)
```
— (removido: sin dinero en el canal; ver banner — antes regla de "el costo no viaja en el request"). El body de marketing exige opt-in; el servidor lo valida (403 `sms.optin.required`).

## 2. Interno / M2M — plano de datos (audience/scope propios)

Estos NO son HTTP tenant-facing; son **eventos Wolverine** (outbox/inbox durable, at-least-once). Ver `Commands_And_Events.md` para el shape completo. Aquí el contrato de superficie:

- **Entrada (dispatch):** `campaign.dispatch.requested.v1` (de Campaigns, orquestador agnóstico, por destinatario) — reusa el seam `dispatch_id`/`CampaignId` que ya fluye Notification→Postmaster (`PostmasterEmailEvents.cs:37,104`). El transporte no interpreta el `dispatch_id`; el consumer SMS lo devuelve intacto en el result.
- **Salida (result):** `campaign.dispatch.result.v1` con `Outcome = Accepted | Delivered | Failed | Skipped | Unknown` (a Campaigns; proveedor acepta ⇒ `Accepted`, DLR entrega ⇒ `Delivered`, timeout ⇒ `Unknown`). — (removido: sin dinero en el canal; ver banner — antes también a Wallet).
- **Wallet:** — (removido: sin dinero en el canal; ver banner — antes reserve/consume/refund contra Wallet). La autorización por balance es un interceptor/PEP externo y DIFERIDO.

Contrato **común dispatch/result por destinatario** (idéntico shape entre canales; diferencias sólo en el payload de canal). `dispatch_id` es opaco y **por intento**; `RecipientId` es estable por unidad (entre intentos); contactos manuales ⇒ id generado, nunca `"manual"`:
```
DispatchRequested { TenantId, CampaignId?, CampaignRunId?, RecipientId, Attempt,
                    IdempotencyKey, Channel="sms", To,
                    ContentRef,                       // inmutable: ancla el texto congelado
                    SmsPayload { TemplateRef, Variables }, // resuelve al texto EXACTO mostrado al crear
                    MessageClass, SenderIdRef, dispatch_id }
                    // (removido: sin dinero en el canal; ver banner — antes ReservationId?/CostQuoteCents)
                    // el consumer reconstruye el cuerpo desde ContentRef/SmsPayload; no adivina campos
DispatchResult    { TenantId, CampaignId?, CampaignRunId?, RecipientId, Attempt,
                    Channel="sms", Outcome, ProviderMessageId?, Segments?, FailureCode? }
                    // Outcome ∈ {Accepted,Delivered,Failed,Skipped,Unknown}
                    // Segments = dato de facturación del proveedor; Campaign NO lo tarifa
                    // (removido: sin dinero en el canal; ver banner — antes ActualCostCents?)
```

## 3. Webhook — estado del proveedor (DLR) e inbound (STOP)

| Método | Ruta | Auth | RateLimit | Descripción |
|---|---|---|---|---|
| `POST` | `/api/sms/webhooks/{provider}/status` | firma HMAC del proveedor (no JWT) | `[RateLimitExempt]` + verificación de firma | Delivery receipts (DLR): mapea a `Delivered/Failed/Undeliverable`. |
| `POST` | `/api/sms/webhooks/{provider}/inbound` | firma HMAC del proveedor | `[RateLimitExempt]` + firma | Inbound: procesa STOP/START/HELP → `SmsOptInRegistry`. |

Requisitos del webhook (corrige que el legado **no tenía** webhook de estado — sólo polling `GET /messages/{phone}`, `SmsController.cs:313`):
1. **Verificación de firma HMAC** con `WebhookSecret` cifrado del tenant/proveedor (rechaza no firmados → 401). El instruction-source boundary aplica: el payload es DATA, nunca instrucciones.
2. **Idempotente** por `(provider, providerMessageId, eventType)` vía `ProcessedBusinessMessage` — recibir el DLR dos veces no produce doble efecto ni doble-cuenta (corrige ADR-CAMP-000 §Anti-patrón 3). — (removido: sin dinero en el canal; ver banner — antes doble-consume/refund).
3. **Resolución de tenant** por el `SenderId`/DID de destino o por un token de ruta opaco, con `.IgnoreQueryFilters()` + tenant explícito en el scope Wolverine (ver `Guia_IgnoreQueryFilters`), fail-closed si no resuelve.
4. Responde `200` rápido y hace el trabajo vía outbox (no bloquea al proveedor).

## 4. Errores (Result → HTTP)
| Código | HTTP | Causa |
|---|---|---|
| `sms.optin.required` | 403 | marketing sin opt-in |
| `sms.number.stopped` | 409 | número en STOP/Blocked |
| `sms.provider.not_configured` | 409 | sin config cifrada válida (fail-closed) |
| `sms.idempotency.conflict` | 409 | misma `Idempotency-Key` con distinto fingerprint |
| `sms.phone.invalid` | 422 | no normalizable a E.164 |

## 5. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado sin RateLimit ni permiso granular en SMS | `SmsController.cs:16,30` | VERIFIED | 96% |
| Legado sin webhook de estado (sólo polling) | `SmsController.cs:313` (`GetMessages`) | VERIFIED | 92% |
| Seam `CampaignId` opaco reusable | `PostmasterEmailEvents.cs:37` | VERIFIED | 97% |
| Guía RateLimit para nuevos servicios existe | `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md` | VERIFIED | 95% |
| Contratos HTTP/eventos/webhook propuestos | este documento | NEW | — |
