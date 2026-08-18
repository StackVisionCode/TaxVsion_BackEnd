# Email (SMTP2GO) — API Contracts

- Servicio: **TaxVision.Campaigns.Email**
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

Este servicio es **primariamente event-driven** (consume dispatch, emite result por el bus). La superficie HTTP es mínima: (a) webhook público de SMTP2GO, (b) endpoints tenant de admin de credenciales/suppression, (c) endpoint interno M2M de estado. **Todo endpoint público lleva `[RateLimit(categoría)]` o `[RateLimitExempt]`** (ver `documents/.../RateLimit/Guia_Nuevos_Servicios_Endpoints.md`).

## 1. Webhook de proveedor (público, sin auth de usuario, CON firma)

```
POST /api/email/webhooks/smtp2go
[AllowAnonymous]            // no hay JWT de usuario
[RateLimit("webhook-provider")]
Headers: X-Smtp2go-Signature: <hmac-sha256>   // verificación OBLIGATORIA (fix legado)
Body: <payload SMTP2GO delivery/bounce/spam/unsubscribe>
→ 200 OK  (siempre 200 tras aceptar, para no gatillar reintentos del proveedor)
→ 401     si la firma no valida (antes de tocar dominio)
```
Reglas (corrigen `TrackingController.cs:133-140,238-241`, que aceptaban cualquier POST):
- **Verificar HMAC** con el secret del webhook (cifrado) **antes** de deserializar/proyectar. Sin firma válida ⇒ 401, sin efecto.
- El handler solo **persiste el evento crudo** (`InboundWebhookEvent`) + encola su proyección; no hace trabajo pesado inline (responde rápido, 200).
- Dedupe por `provider_event_id` (unique) + `ProcessedBusinessMessage` antes de aplicar efecto (idempotente; corrige double-count #3).
- `CampaignId`/`RecipientId` se recuperan por `ProviderMessageId` (correlación), no se confían del body arbitrario.

## 2. Tracking pixel / click (público) — decisión

El legado exponía `GET /open/{cid}/{rid}` y `/click/...` propios (`TrackingController.cs:39,80`). **Diseño nuevo: usar el open/click tracking nativo de SMTP2GO** (que llega por webhook), evitando hostear pixel/redirect propios salvo requerimiento. Si se hostean:
```
GET /api/email/t/o/{token}    [AllowAnonymous][RateLimit("tracking-pixel")]  → 1x1 gif
GET /api/email/t/c/{token}    [AllowAnonymous][RateLimit("tracking-click")]  → 302 al destino firmado
```
- `token` = opaco firmado (HMAC) que codifica `(dispatchId)`; **no** `cid`/`rid` en claro en la URL (privacidad; el legado los ponía en claro, `Smtp2GoService.cs:449-453`).
- Apertura/click ⇒ emite evento **por el bus** (no fire-and-forget como `TrackingController.cs:53`), deduplicado.
- `{token}` de click solo redirige a URLs **allow-listed** por el dispatch (no open-redirect; el legado redirigía a cualquier `url` de query, `TrackingController.cs:109`).

## 3. Admin de credenciales (tenant, JWT + RBAC)

```
PUT  /api/email/providers/smtp2go        [Authorize][HasPermission("campaigns.email.provider.manage")][RateLimit("admin-write")]
  Body: { fromEmail, fromName, apiKey, baseUrl? }   // apiKey se cifra al persistir, nunca se devuelve
  → 200 { scope:"Tenant", fromDomainVerified:false }
POST /api/email/providers/smtp2go/verify [Authorize][HasPermission(...)][RateLimit("admin-write")]
  → 202  (dispara verificación de dominio)
GET  /api/email/providers/smtp2go        [Authorize][HasPermission("campaigns.email.provider.read")][RateLimit("admin-read")]
  → 200 { fromEmail, fromName, fromDomainVerified, isActive, keyVersion }   // SIN apiKey
```
La `apiKey` **nunca** se devuelve en ningún GET (write-only). Ownership + tenant enforced.

## 4. Suppression list (tenant)
```
GET    /api/email/suppressions            [Authorize][HasPermission("campaigns.email.suppression.read")][RateLimit("admin-read")]
POST   /api/email/suppressions            [Authorize][HasPermission("...manage")][RateLimit("admin-write")]  { address, reason:"Manual" }
DELETE /api/email/suppressions/{address}  [Authorize][HasPermission("...manage")][RateLimit("admin-write")]  // solo Manual/Unsubscribe; HardBounce/SpamComplaint no se borran
```

## 5. Endpoint interno M2M (estado de dispatch)
```
GET /internal/email/dispatches/{dispatchId}   [Authorize(M2M audience="campaigns-email")][RateLimitExempt]
  → 200 { status, providerMessageId, sentAtUtc, deliveredAtUtc, failureReason }
```
Consumido por Campaigns para reconciliación puntual; el camino normal es por eventos (§ Commands_And_Events).

## 6. Contrato consumido (entrada por bus)
El servicio **no** ofrece un endpoint HTTP de "enviar email de campaña": el dispatch entra como **evento** `campaigns.email.dispatch_requested.v1` (ver `Commands_And_Events.md`). Esto evita el fan-out HTTP síncrono del legado.

## 7. Convenciones de error
- `Result`-based en dominio; HTTP mapea a Problem Details.
- Webhooks: nunca 5xx por error de dominio recuperable (se responde 200 tras persistir crudo; el error se procesa async con reintento del bus).
- Rate limit 429 con `Retry-After`.

## 8. Categorías `[RateLimit]` nuevas a registrar
`webhook-provider`, `tracking-pixel`, `tracking-click`, `admin-read`, `admin-write` (ver guía RateLimit para límites por categoría).
