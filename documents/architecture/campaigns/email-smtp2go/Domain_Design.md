# Email (SMTP2GO) — Domain Design

- Servicio: **TaxVision.Campaigns.Email** (ejecutor de canal EMAIL, greenfield)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Anchors: `../00_Overview_And_Index.md`, `../02_Context_Map.md`, `../05_Master_ADR.md`

## 1. Responsabilidad (y lo que NO es)

Ejecutor de canal EMAIL para campañas y para envíos transaccionales de la suite Campaigns. **Consume** un dispatch por destinatario del contrato común, **renderiza** (o usa el cuerpo pre-renderizado por Scribe), **entrega vía SMTP2GO**, y **reporta** el resultado (`sent`/`delivered`/`failed`/`bounced`/`suppressed`/`complained`) de vuelta a Campaigns con `CampaignId` intacto. Es el análogo de campañas a lo que Postmaster es a la app principal, pero **no reusa Postmaster** (decisión CAMP-000: Postmaster es exclusivo de la app principal).

**NO hace:**
- No define ni agenda campañas (eso es Campaigns), no resuelve audiencia (Customer), no dispara el reloj (Scheduler).
- No muta saldo. No calcula ni cobra: pide a nadie. El costo/`reserve`/`consume`/`refund` lo gobierna Wallet vía la saga de Campaigns (`../06_...`). Este servicio solo **emite el result** que dispara consume/refund.
- No es Postmaster ni comparte su base, sus credenciales ni su `SentMessage`.
- No persiste JWT de usuario (anti-patrón legado #5), no guarda secretos en texto plano.

## 2. Bounded context y lenguaje

| Término | Significado |
|---|---|
| **EmailDispatch** | Aggregate root: un intento de entrega de UN email a UN destinatario para UN `(CampaignRunId, RecipientId, Attempt)`. Inmutable en su identidad; su estado avanza por métodos del aggregate que devuelven `Result`. |
| **SuppressionEntry** | Aggregate: dirección suprimida (hard bounce, spam complaint, unsubscribe, manual) por tenant. Fail-closed: si existe, no se intenta el envío. |
| **ProviderCredential** | Aggregate: config SMTP2GO por tenant/scope, con `ApiKey` **cifrada** (envelope encryption), `FromDomain` verificado, límites de rate del proveedor. Reemplaza a `SmtpProviderConfig` legado (texto plano). |
| **InboundWebhookEvent** | Registro inmutable del webhook crudo recibido de SMTP2GO (delivery/bounce/spam/unsubscribe), deduplicado antes de proyectar efecto. |
| **ProviderScope** | `System` (credenciales SMTP2GO de la plataforma TaxVision) vs `Tenant` (SMTP2GO del tenant). Espeja `RequiredProviderScope` del seam existente (`PostmasterEmailEvents.cs:45`). |

## 3. Aggregates y su forma

### 3.1 EmailDispatch (root)
```
EmailDispatch
  Id                : Guid (PK)
  TenantId          : Guid            // fail-closed, query filter global
  CampaignId        : Guid            // correlación opaca, NO FK (seam CampaignId)
  CampaignRunId     : Guid            // run inmutable que originó el dispatch
  RecipientId       : Guid            // id opaco del destinatario en Campaigns
  Attempt           : int             // n-ésimo intento del run para este recipient
  IdempotencyKey    : IdempotencyKey  // (copia local del VO) = clave de negocio del dispatch
  ToAddress         : EmailAddress    // VO validado
  ProviderScope     : ProviderScope   // System | Tenant
  Status            : EmailDispatchStatus (ver State_Machines.md)
  ProviderMessageId : string?         // email_id devuelto por SMTP2GO (para correlar webhooks)
  FailureReason     : string?
  RowVersion        : byte[]          // optimistic concurrency
  CreatedAtUtc, SentAtUtc, DeliveredAtUtc, TerminalAtUtc : DateTime?
```
Invariantes:
- `(TenantId, CampaignRunId, RecipientId, Attempt)` es único (unique constraint) — **una fila por intento**, nunca se reescribe otra (corrige anti-patrón #8 y el double-count #3).
- Transiciones solo por métodos: `MarkSuppressed()`, `MarkSent(providerMessageId)`, `MarkDelivered()`, `MarkBounced(type,reason)`, `MarkComplained()`, `MarkFailed(reason)`. Cada uno valida el estado origen y devuelve `Result` (guardas de estado, no excepciones de control de flujo).
- El cuerpo del email **no** se persiste como columna material grande por defecto; si viaja pre-renderizado, se hashea (`BodyHash`) para trazabilidad, no se almacena el HTML completo (privacidad + tamaño). El HTML final vive solo el tiempo del POST a SMTP2GO.

### 3.2 SuppressionEntry (root)
```
SuppressionEntry
  Id, TenantId
  Address          : EmailAddress   // normalizada (lower, trim)
  Reason           : SuppressionReason (HardBounce|SpamComplaint|Unsubscribe|Manual|ProviderSuppressed)
  SourceMessageId  : string?
  CreatedAtUtc
```
Invariante: `(TenantId, Address)` único. Consulta O(1) antes de cada envío (fail-closed).

### 3.3 ProviderCredential (root)
```
ProviderCredential
  Id, TenantId, Scope (System|Tenant)
  EncryptedApiKey  : byte[]         // envelope-encrypted (KMS/DPAPI), NUNCA texto plano
  KeyVersion       : int            // rotación
  BaseUrl          : string         // default https://api.smtp2go.com/v3
  FromEmail, FromName : string      // FromEmail verificado en SMTP2GO
  FromDomainVerified  : bool
  ProviderRatePerSecond : int       // rate del plan del proveedor
  IsActive         : bool
```
Nunca se expone la key descifrada fuera del adapter de envío. Ver `Security.md`.

## 4. Value Objects (copia por contexto, no compartidos)
- `EmailAddress(string Value)` — valida formato + normaliza; rechaza vacío.
- `IdempotencyKey` — **copia local** del VO (`PaymentApp.Domain/ValueObjects/IdempotencyKey.cs`), no se comparte el tipo entre contexts.
- `ProviderScope`, `EmailDispatchStatus`, `SuppressionReason`, `BounceType(Hard|Soft)` — enums de dominio.
- El dinero **no vive acá**: este servicio no maneja `Money`; el costo se resuelve en Wallet/Campaigns.

## 5. Render
- Por defecto el cuerpo **ya viaja renderizado** por Scribe desde Campaigns (mismo patrón que `NotificationsEmailSendRequestedIntegrationEvent.HtmlBody/TextBody`, `PostmasterEmailEvents.cs:41-42`): el ejecutor **no re-renderiza** (Context Map, fila Email→Scribe).
- Si el dispatch trae `TemplateKey` + `TemplateVariables` en lugar de cuerpo, el ejecutor llama a **Scribe (REUSE, Fluid/Liquid)** para materializar HTML/text. **Prohibido** el `string.Replace`/regex de personalización del legado (`Smtp2GoService.cs:420-472`) — es frágil, no escapa consistente y no soporta lógica.
- Assets inline (logos) viajan como **referencia** (`EmailInlineAssetReference`, `PostmasterEmailEvents.cs:83-88`), nunca como bytes por el bus; se resuelven de CloudStorage justo antes de armar el MIME.

## 6. Mapa contra el legado (evidencia)

| Hecho legado | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Email de campañas funcionaba vía SMTP2GO (POST `email/send`) | `Smtp2GoService.cs:147-152` | VERIFIED | 97% |
| ApiKey SMTP2GO en texto plano (BD + settings) | `SmtpProviderConfig.cs:7`, `Smtp2GoSettings.cs:6` | VERIFIED | 98% |
| Fan-out síncrono con `Task.Delay` entre batches (se pierde al reiniciar) | `Smtp2GoService.cs:367-406` | VERIFIED | 96% |
| Sin idempotencia por destinatario; log por envío pero sin dedupe de reintento | `Smtp2GoService.cs:270-338` | VERIFIED | 92% |
| Personalización por `string.Replace`/regex (frágil) | `Smtp2GoService.cs:420-472` | VERIFIED | 95% |
| `List-Unsubscribe` + one-click ya se armaban (a conservar) | `Smtp2GoService.cs:541-548` | VERIFIED | 94% |
| Webhooks `[AllowAnonymous]` **sin verificación de firma** | `TrackingController.cs:133-140, 238-241` | VERIFIED | 95% |
| Pixel open fire-and-forget (`_ = _mediator.Send`) | `TrackingController.cs:53` | VERIFIED | 90% |
| Seam `CampaignId` opaco Notification→Postmaster→result (a generalizar) | `PostmasterEmailEvents.cs:37,103,119,137` | VERIFIED | 96% |
| Ejecutor Email nuevo NO reusa Postmaster | Decisión CAMP-000 §2 | DECISION | 100% |
| Contrato dispatch/result común por destinatario | `../05_Master_ADR.md` §Decisiones | NEW | n/a |

## 7. Reglas duras heredadas (CLAUDE.md)
- Multi-tenant fail-closed: query filter global + repos tenant-scoped + `.IgnoreQueryFilters()`+tenant explícito en el scope Wolverine de handlers (ver `documents/Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`).
- Mutaciones por métodos del aggregate devolviendo `Result`; nada de setters públicos que rompan invariantes.
- IDs opacos entre contexts, sin FK cruzada (`CampaignId`/`RecipientId` no son FK).
- Mensajería Wolverine outbox/inbox durable, at-least-once, handlers idempotentes.
