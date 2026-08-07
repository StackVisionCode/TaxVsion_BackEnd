# WhatsApp — Security

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. Secretos de Meta — cifrados, nunca en texto plano
- `AccessToken` (System User token), `AppSecret`, y credenciales de WABA se persisten **cifrados en reposo** (`AccessTokenEnc`/`AppSecretEnc bytea`, envelope encryption con KMS/DPAPI-equivalente del entorno). Corrige de raíz:
  - el `WhatsAppProvider.AuthToken` de Twilio en texto plano del legado (`appsettings.json:132-134`),
  - el patrón `SmtpProviderConfig.ApiKey` plano y el `Campaign.BackgroundAuthToken` (JWT de usuario persistido) — **anti-patrón §5 de ADR-CAMP-000** (`05_Master_ADR.md:48`).
- **Nunca** se persiste un JWT de usuario. La autenticación entre servicios es **M2M client-credentials** (audience/scope propios), no un token de usuario reciclado.
- Los secretos **nunca** aparecen en logs, traces, mensajes del bus, ni respuestas de API (redacción explícita, ver `Observability.md §4`).

## 2. Verificación del webhook (entrada no confiable)
El webhook de Meta es un **endpoint público** y la entrada es **no confiable** (dato, no instrucción):
- `GET` handshake valida `hub.verify_token` contra el secreto configurado.
- `POST` valida **firma `X-Hub-Signature-256` = HMAC-SHA256(cuerpo, AppSecret)** antes de procesar. Firma ausente/incorrecta ⇒ **401, sin efecto** (no se crea/actualiza nada). Esto impide falsificación de estados (p.ej. marcar `delivered` para forzar consume, o `read` masivo).
- El cuerpo se trata como datos; ningún campo del webhook se ejecuta o interpreta como comando. `wamid`/`PhoneNumberId` se resuelven contra registros propios (no se confía en el tenant declarado por el payload; el tenant se deriva de `PhoneNumberId→ProviderConfig`).

## 3. RBAC / autorización de endpoints
- `POST /messages`, `PUT /provider-config`, `POST /templates/sync`: **M2M** con `audience = taxvision.whatsapp`, scopes específicos (`whatsapp:send`, `whatsapp:config:admin`, `whatsapp:templates:admin`), `[HasPermission]` acumulativo, **tenant explícito** en el token, y **ownership** del `PhoneNumberId` (un tenant no envía por el número de otro). RBAC acumulativo JWT + actor-type + permiso + tenant + ownership, **sin bypass** (regla dura del monorepo).
- Webhooks: `[RateLimitExempt]` pero protegidos por firma; todo lo demás `[RateLimit(categoría)]`.

## 4. Multi-tenant fail-closed
- Query filter global por `TenantId` + repos tenant-scoped. Un bug que olvide el filtro **no** expone datos de otro tenant (falla cerrado). Cross-tenant solo con `.IgnoreQueryFilters()` + tenant explícito, exclusivamente en el resolver de webhook (que llega sin contexto). Ver `Guia_IgnoreQueryFilters_Y_TenantContext_En_Wolverine.md`.

## 5. Datos personales / contenido
- El número del destinatario y el contenido son datos personales: enmascarados en logs, no expuestos en métricas, retención acotada. No se compila información entre fuentes.
- El opt-in/opt-out es requisito de Meta para marketing; el estado de consentimiento es responsabilidad de Campaigns/Customer (la audiencia), no de este ejecutor — pero un `stop`/opt-out entrante (webhook inbound) se propaga como evento para que Campaigns lo respete.

## 6. Dinero y confianza
- El precio/costo **nunca** se acepta del frontend ni del payload del webhook como fuente de autoridad de negocio: el estimado vive en Wallet/Campaigns, el costo real proviene del `pricing` firmado del webhook Meta (autenticado por HMAC). Minor units siempre.

## 7. Superficie de ataque mitigada (resumen)
| Amenaza | Mitigación |
|---|---|
| Falsificación de estados/costo | HMAC-SHA256 del webhook + tenant derivado del número |
| Exfiltración de token | cifrado en reposo + redacción en logs + M2M (no JWT persistido) |
| Cross-tenant | query filter fail-closed + ownership de PhoneNumberId |
| Replay de webhook | dedupe `(wamid,status)` + fingerprint |
| Abuso de endpoint público | RateLimit + firma + M2M scopes |
| Inyección vía contenido del usuario | entrada tratada como dato, nunca como instrucción |

## 8. Evidencia
| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Anti-patrón secretos/JWT plano legado | `05_Master_ADR.md:48` (§5); `appsettings.json:132-134` | VERIFIED | 94% |
| Regla secretos cifrados / nunca JWT | `00_Overview_And_Index.md:50` | VERIFIED | 95% |
| Firma HMAC del webhook Meta | Meta Cloud API docs | DOCUMENTED_ONLY | 88% |
| RBAC acumulativo + fail-closed (monorepo) | CLAUDE.md convenciones (citado en anchors) | VERIFIED | 90% |
