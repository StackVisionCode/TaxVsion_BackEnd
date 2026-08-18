# Email (SMTP2GO) — Security

- Servicio: **TaxVision.Campaigns.Email**
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. Amenaza #1 — secretos de proveedor (fix directo del legado)
El legado guardaba la **API key de SMTP2GO en texto plano** en BD (`SmtpProviderConfig.ApiKey`, `SmtpProviderConfig.cs:7`) y en config (`Smtp2GoSettings.ApiKey`, `Smtp2GoSettings.cs:6`), y persistía **JWT de usuario** para refunds (anti-patrón #5). Diseño nuevo:
- `provider_credential.encrypted_api_key` = **envelope encryption** (DEK por registro, KEK en KMS/DPAPI del entorno), con `key_version` para rotación. Nunca texto plano en BD, logs, ni respuestas HTTP.
- La key se **descifra solo en memoria** dentro del handler de envío, se inyecta en un typed `HttpClient` por-request y se descarta. Nunca en un singleton compartido (a diferencia de `Smtp2GoService.cs:75-79`).
- **Prohibido persistir JWT de usuario.** Toda operación de servicio-a-servicio usa **M2M client-credentials** (audience/scope propios). El refund lo dispara un evento de dominio, no un JWT guardado.
- `webhook_secret_enc` (HMAC del webhook) también cifrado.

## 2. Webhooks — verificación de firma OBLIGATORIA
El legado exponía webhooks `[AllowAnonymous]` **sin verificar nada** (`TrackingController.cs:133-140, 238-241, 278-281`): cualquiera podía POSTear eventos falsos (falsos bounces ⇒ suppression envenenada; falsos delivered ⇒ consume indebido).
- `POST /api/email/webhooks/smtp2go` verifica **HMAC-SHA256** (`X-Smtp2go-Signature`) contra `webhook_secret_enc` **antes** de deserializar o tocar dominio. Firma inválida ⇒ 401, sin efecto, WARN con origen.
- Solo tras firma válida se persiste el evento crudo; la proyección corre async.
- `CampaignId`/`RecipientId` se resuelven por `provider_message_id` (correlación server-side), **no** se confían de campos arbitrarios del body (el legado parseaba `CampaignId` del payload, `TrackingController.cs:250,288`).

## 3. AuthN/AuthZ
- Endpoints tenant (credenciales, suppression): **JWT + `[HasPermission(...)]`** RBAC acumulativo (actor-type + permiso + tenant + ownership), sin bypass. Ver CLAUDE.md RBAC.
- Endpoint interno de estado: **M2M** con `audience="campaigns-email"` + scope de lectura.
- Webhook público y tracking pixel/click: `[AllowAnonymous]` (no hay usuario) pero con firma / token firmado + `[RateLimit]`.

## 4. Rate limiting (anti-abuso)
Todo endpoint público lleva `[RateLimit(categoría)]` (categorías nuevas: `webhook-provider`, `tracking-pixel`, `tracking-click`, `admin-read/write`) — ver `Guia_Nuevos_Servicios_Endpoints.md`. Protege contra flooding de webhooks falsos y scraping de tracking.

## 5. PII y contenido
- El **cuerpo del email no se persiste** (solo `body_hash`) — minimiza exposición de PII y contenido comercial.
- `to_address` es PII: cifrado en reposo a nivel de BD, retención acotada, logs sin address en claro fuera de debug.
- Tracking tokens son **opacos firmados** (HMAC), no `cid`/`rid` en claro en la URL (el legado los ponía en claro, `Smtp2GoService.cs:449-453`, permitiendo enumeración).
- Click tracking solo redirige a URLs **allow-listed** por el dispatch (no open-redirect; el legado redirigía a cualquier `url`, `TrackingController.cs:109`).

## 6. Multi-tenant fail-closed
- Query filter global + repos tenant-scoped; `.IgnoreQueryFilters()`+tenant explícito solo en handlers de bus con el tenant fijado en scope (`Guia_IgnoreQueryFilters...`).
- Una credencial/suppression/dispatch de un tenant es **inaccesible** para otro; el `provider_credential` se resuelve por tenant, sin fallback silencioso a otra credencial.

## 7. Anti-spam / reputación (obligaciones)
- `List-Unsubscribe` + `List-Unsubscribe-Post: One-Click` en cada envío (conservado del legado, `Smtp2GoService.cs:541-548`).
- Suppression fail-closed: hard bounce / spam complaint / unsubscribe se suprimen automáticamente y **no** son borrables por API (solo `Manual`/`Unsubscribe` reversibles).
- Solo se envía desde `from_domain_verified=true` en scope Tenant (SPF/DKIM del tenant); scope System usa dominios verificados de plataforma.

## 8. Superficie de secretos
| Secreto | Dónde | Protección |
|---|---|---|
| SMTP2GO API key | `provider_credential.encrypted_api_key` | envelope encryption + rotación |
| Webhook HMAC secret | `provider_credential.webhook_secret_enc` | cifrado |
| Tracking token key | config/KMS | firma HMAC de tokens |
| M2M client secret | vault del entorno | no en BD de la app |

## 9. Evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| API key SMTP2GO en texto plano (BD + config) | `SmtpProviderConfig.cs:7`, `Smtp2GoSettings.cs:6` | VERIFIED | 98% |
| Webhooks sin verificación de firma | `TrackingController.cs:133-140,238-241,278-281` | VERIFIED | 95% |
| CampaignId confiado del payload del webhook | `TrackingController.cs:250,288` | VERIFIED | 92% |
| Open-redirect en click tracking | `TrackingController.cs:109` | VERIFIED | 90% |
| cid/rid en claro en URLs de tracking | `Smtp2GoService.cs:449-453` | VERIFIED | 90% |
| JWT de usuario persistido para refund (suite) | `../05_Master_ADR.md` #5 | VERIFIED | 88% |
| Cifrado/HMAC/M2M nuevos | este diseño | NEW | n/a |
