# Email (SMTP2GO) — ADRs del servicio

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — Email NO es un ejecutor dedicado nuevo; es un CONSUMER dentro del servicio EXISTENTE `Notification`** (reusa `SendEmailCommand` con el seam `CampaignId`; SMTP2GO es solo el proveedor que usa Notification). Campaign es un orquestador agnóstico que **no envía**: publica `campaign.dispatch.requested.v1` por destinatario y este consumer lo procesa (`ActorType.Service`) y responde `campaign.dispatch.result.v1`. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio dedicado `TaxVision.Campaigns.Email` y/o un Wallet queda **superseded** por esta nota. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- Componente: **Consumer/handler dentro del servicio EXISTENTE `Notification`** (SMTP2GO = proveedor que usa Notification); persistencia dentro de Notification.
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Contexto padre: `../05_Master_ADR.md` (CAMP-000). Estos ADRs refinan el canal Email.

---

## ADR-EMAIL-001 — Canal Email = consumer dentro de `Notification` con SMTP2GO, NO reuso de Postmaster
**Estado:** APPROVED (deriva de CAMP-000 §2 + ADR-CAMP-001, decisión de usuario).
**Contexto:** Existe Postmaster (pipeline email de la app principal, `PostmasterEmailEvents.cs`). Tentador reusarlo.
**Decisión:** El canal Email de campañas es un **consumer/handler dentro del servicio EXISTENTE `Notification`** (reusa `SendEmailCommand` con el seam `CampaignId`), usando **SMTP2GO como proveedor**; NO es un microservicio dedicado nuevo `TaxVision.Campaigns.Email` ni tiene BD propia (la persistencia vive dentro de Notification). Postmaster es **exclusivo de la app principal**, no se reusa ni se comparte su `SentMessage`.
**Consecuencias:** Se **reusa el patrón** (contrato `CampaignId` opaco) sin duplicar un servicio nuevo. El tráfico **bulk** de campañas se aísla del transaccional dentro de Notification vía `Stream=Bulk`; ADEMÁS, como la cuenta/quota SMTP2GO es compartida, se debe **presupuestar el rate del proveedor** (una cola separada no cubre por sí sola el límite de la cuenta compartida) — ver `Concurrency_Spec.md §4`.
**Alternativas:** Reusar Postmaster tal cual — rechazada (acopla dominios, mezcla reputación/credenciales, viola la decisión de usuario).

---

## ADR-EMAIL-002 — Estado por-destinatario en `EmailDispatch`, no en el Campaign
**Estado:** APPROVED.
**Contexto:** El legado tenía un `Campaign.Status` global con `Sending` no-atómico; marcaba `Sent` a todos los no-fallidos y doble-contaba tracking en reintento (anti-patrones #3, #6, #8).
**Decisión:** El estado de entrega vive en `EmailDispatch`, **una fila inmutable en identidad por `(run, recipient, attempt)`**, con UNIQUE y state guards. El estado del Campaign/Run vive en Campaigns, desacoplado.
**Consecuencias:** Elimina double-send al escalar y double-count en reintento; auditoría por intento. Más filas, a cambio de correctitud.

---

## ADR-EMAIL-003 — Contrato dispatch/result event-driven, no fan-out HTTP síncrono
**Estado:** APPROVED.
**Contexto:** El legado hacía fan-out en memoria (`SendBatchAsync`, `Task.Delay`, `Smtp2GoService.cs:367-406`), perdido al reiniciar.
**Decisión:** El dispatch entra como evento canónico `campaign.dispatch.requested.v1` (uno por recipient), y el result sale como `campaign.dispatch.result.v1`, todo por **Wolverine outbox/inbox durable** con atomicidad estado↔evento. Spacing por rate limiter, no por sleeps.
**Consecuencias:** Resiliente a reinicios, retomable, idempotente. Introduce complejidad distribuida (mensajería durable entre Campaign y el consumer). — (removido: saga con Wallet; sin dinero en el canal; ver banner).

---

## ADR-EMAIL-004 — Credenciales cifradas + M2M, cero JWT persistido
**Estado:** APPROVED (deriva de CAMP-000, anti-patrón #5).
**Contexto:** El legado guardaba API key en claro (`SmtpProviderConfig.cs:7`, `Smtp2GoSettings.cs:6`) y JWT de usuario para refunds.
**Decisión:** `encrypted_api_key` con envelope encryption + rotación (`key_version`); descifrado solo en memoria por-request; **nunca** JWT persistido; servicio-a-servicio por M2M client-credentials.
**Consecuencias:** Superficie de secretos controlada; rotación posible; sin fuga por dump de BD.

---

## ADR-EMAIL-005 — Webhooks con credencial verificada (mecanismo real del proveedor) + dedupe
**Estado:** APPROVED.
**Contexto:** El legado aceptaba webhooks `[AllowAnonymous]` sin verificar nada (`TrackingController.cs:133-140,238-241`), confiando `CampaignId` del body.
**Decisión:** Verificar la credencial con el **mecanismo que SMTP2GO REALMENTE soporta** — su setup de webhooks documenta `Authorization` (Bearer/Basic), **no** una cabecera `X-Smtp2go-Signature` HMAC-SHA256; confirmar contra la doc vigente antes de implementar. Exigir HTTPS; validar la credencial antes de aplicar efectos; persistir crudo con UNIQUE `provider_event_id`; correlacionar por `provider_message_id` server-side; proyectar con state guards monótonos. Si una infra interna añade su propia firma, autentica el salto interno (no a SMTP2GO); se documenta aparte.
**Consecuencias:** Bloquea envenenamiento de suppression/stats por eventos falsos; idempotente ante reintentos del proveedor.

---

## ADR-EMAIL-006 — Render por Scribe, no personalización por string.Replace
**Estado:** APPROVED (deriva de CAMP-000 §2, "Render = reusar Scribe").
**Contexto:** El legado personalizaba con `string.Replace`/regex (`Smtp2GoService.cs:420-472`): frágil, escape inconsistente, sin lógica.
**Decisión:** El contenido viaja como **`ContentRef` inmutable + `EmailPayload{ ScribeTemplateKey(revisión/hash CONGELADO), Subject, Variables }`** (no una `ScribeTemplateKey` mutable sola): el cuerpo viaja pre-renderizado por Scribe (camino normal) o se renderiza vía Scribe (Fluid/Liquid) contra esa revisión congelada. Assets inline por **referencia** (`EmailInlineAssetReference`), no bytes.
**Consecuencias:** Render consistente y seguro; ejecutor no re-renderiza si el cuerpo ya viaja (evita trabajo doble).

---

## ADR-EMAIL-007 — Costeo
**Estado:** SUPERSEDED por ADR-CAMP-001.
— (removido: costeo/consume/refund/Wallet del canal; sin dinero en el canal; ver banner). Este consumer solo emite result events de entrega; cualquier autorización por balance es un interceptor/PEP externo y DIFERIDO, fuera de este canal.

---

## Blockers abiertos
| ID | Descripción | Bloquea |
|---|---|---|
| **B-EMAIL-TX-2** | Confirmar (in)existencia de idempotencia client-key en SMTP2GO `email/send`; define si el reconciliador es obligatorio para MVP | `Transactional_Protocol.md §4-5` |
| **B-EMAIL-3** | Decidir si se hostea open/click propio o se usa el tracking nativo de SMTP2GO (impacta `email_tracking_event` y endpoints) | `API_Contracts.md §2`, `Data_Model.md §2.6` |

## Evidencia consolidada
| ADR | Evidencia clave | Clasificación |
|---|---|---|
| 001 | `PostmasterEmailEvents.cs`; CAMP-000 §2 | DECISION/VERIFIED |
| 002 | `../05_Master_ADR.md` #3,#6,#8 | VERIFIED |
| 003 | `Smtp2GoService.cs:367-406` | VERIFIED |
| 004 | `SmtpProviderConfig.cs:7`, `Smtp2GoSettings.cs:6` | VERIFIED |
| 005 | `TrackingController.cs:133-140,238-241,250` | VERIFIED |
| 006 | `Smtp2GoService.cs:420-472`; `PostmasterEmailEvents.cs:83-88` | VERIFIED |
| 007 | — (removido: costeo; sin dinero en el canal; ver banner) | SUPERSEDED |
