# TaxVision.Sms — Security

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — SMS NO es un servicio nuevo; `TaxVision.Sms` YA EXISTE y ya es M2M** (`SendSmsBatchCommand`, `POST /sms/messages`, `ActorType.Service`). El canal SMS es un **CONSUMER dentro de `TaxVision.Sms`** que procesa `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio SMS nuevo y/o un Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- **Servicio:** SMS (`TaxVision.Sms`) — consumer del canal SMS **dentro de `TaxVision.Sms` (ya existente)**
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

## 1. Secretos de proveedor cifrados (fix directo del legado)
El legado guardaba `ClientApiKey` y `UserApiToken` **en texto plano** en la BD (`SmsProviderCredential.cs:20,25`) y persistía JWT de usuario (`Campaign.BackgroundAuthToken`, ADR-CAMP-000 §Anti-patrón 5). Aquí:
- `sms.provider_config.encrypted_credentials` y `encrypted_webhook_secret` se guardan **cifrados** (envelope encryption con clave gestionada — KMS/DPAPI/Data Protection, decisión de infra), con `key_version` para rotación.
- El plaintext existe sólo en memoria durante la llamada al proveedor; nunca se loggea ni se devuelve por la API (los GET devuelven valores enmascarados).
- **Nunca** se persiste JWT de usuario. La integración interna es **M2M client-credentials** con audience/scope propios del servicio SMS.

## 2. RBAC acumulativo (sin bypass)
Todo endpoint tenant-facing: JWT + actor-type + `[HasPermission("sms.…")]` + tenant + ownership. Permisos granulares (`sms.config.manage`, `sms.optin.manage`, `sms.send`, `sms.read`) en vez del `[Authorize(Roles="Admin,Owner")]` grueso del legado (`SmsController.cs:41`). M2M para dispatch de campaña usa audience/scope, no un rol de usuario.

## 3. Multi-tenant fail-closed
Query filter global por `TenantId` + repos tenant-scoped. Accesos sin usuario (webhooks, jobs) usan `.IgnoreQueryFilters()` **con tenant explícito** en el scope Wolverine (ver `Guia_IgnoreQueryFilters`). Un webhook que no resuelve tenant se **descarta** (nunca se procesa cross-tenant). Los secretos de un tenant jamás se usan para otro.

## 4. Verificación de webhooks (entrada no confiable)
- DLR e inbound del proveedor se verifican por **firma HMAC** contra el `WebhookSecret` cifrado del tenant/proveedor antes de procesar; sin firma válida ⇒ 401, sin efecto.
- El payload del webhook es **DATA, no instrucciones** (instruction-source boundary): un STOP/HELP entrante se trata como consentimiento del usuario final, no como comando privilegiado; no puede alterar config ni permisos directamente — sólo transiciona `SmsOptInRegistry`.
- Rate-limit por firma/origen + `[RateLimitExempt]` en la ruta (la protección es la firma), con protección anti-replay vía `ProcessedBusinessMessage`.

## 5. ~~Dinero confiable~~
— (removido: sin dinero en el canal; ver banner — antes cálculo server-side de costo/precio y rechazo de montos del frontend). La autorización por balance es un interceptor/PEP externo y DIFERIDO, fuera de este canal. El `quote` server-side devuelve sólo `(encoding, segments)`.

## 6. Consentimiento / cumplimiento (TCPA / carrier)
- Marketing exige opt-in registrado con prueba (`consent_source`, `consent_proof_ref`); doble opt-in recomendado.
- STOP/UNSUBSCRIBE es **duro e inmediato**, idempotente, y bloquea marketing y transactional. HELP responde plantilla obligatoria.
- Sender id / short code deben estar registrados (10DLC/short code campaign registry) del lado del proveedor — precondición operativa, no del código.

## 7. Protección de datos / PII
- El cuerpo del SMS y el teléfono son PII: enmascarados en logs, cifrados en tránsito (TLS al proveedor), acceso por permiso. Retención acotada (housekeeping de `sms_dispatch` y `processed_business_message` por `expires_at_utc`).
- MMS/media (si se soporta) por referencia a CloudStorage, nunca bytes por el bus.

## 8. Acciones prohibidas / límites del agente
- Ningún endpoint permite exfiltrar secretos. Cambios de config/opt-in rules son operaciones auditadas y permission-gated. — (removido: sin dinero en el canal; ver banner — antes "ni saldo").

## 9. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Legado secretos en claro | `SmsProviderCredential.cs:20,25` | VERIFIED | 98% |
| Legado JWT de usuario persistido | ADR-CAMP-000 §Anti-patrón 5 | VERIFIED | 93% |
| Legado autorización gruesa por rol | `SmsController.cs:41` | VERIFIED | 95% |
| Convenciones RBAC/tenant/RateLimit de la casa | `00_Overview_And_Index.md` §Reglas duras, `Guia_IgnoreQueryFilters` | VERIFIED (política) | 95% |
| Modelo de seguridad SMS propuesto | este documento | NEW | — |
