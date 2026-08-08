# Email (SMTP2GO) — ADRs del servicio

- Servicio: **TaxVision.Campaigns.Email**
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Contexto padre: `../05_Master_ADR.md` (CAMP-000). Estos ADRs refinan el canal Email.

---

## ADR-EMAIL-001 — Servicio Email nuevo con SMTP2GO, NO reuso de Postmaster
**Estado:** APPROVED (deriva de CAMP-000 §2, decisión de usuario).
**Contexto:** Existe Postmaster (pipeline email de la app principal, `PostmasterEmailEvents.cs`). Tentador reusarlo.
**Decisión:** Ejecutor Email de campañas = servicio nuevo `TaxVision.Campaigns.Email` sobre SMTP2GO. Postmaster es **exclusivo de la app principal**, no se reusa ni se comparte su BD/credenciales/`SentMessage`.
**Consecuencias:** Se duplica el "shape" del pipeline (dispatch→provider→result), pero se **reusa el patrón** (contrato `CampaignId` opaco) sin acoplar los dominios. Aísla el volumen bulk de campañas del transaccional de la app principal.
**Alternativas:** Reusar Postmaster con un `Stream=Bulk` — rechazada (acopla dominios, mezcla reputación/credenciales, viola la decisión de usuario).

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
**Decisión:** El dispatch entra como evento `campaigns.email.dispatch_requested.v1` (uno por recipient), y el result sale como evento, todo por **Wolverine outbox/inbox durable** con atomicidad estado↔evento. Spacing por rate limiter, no por sleeps.
**Consecuencias:** Resiliente a reinicios, retomable, idempotente. Introduce complejidad distribuida (saga con Wallet).

---

## ADR-EMAIL-004 — Credenciales cifradas + M2M, cero JWT persistido
**Estado:** APPROVED (deriva de CAMP-000, anti-patrón #5).
**Contexto:** El legado guardaba API key en claro (`SmtpProviderConfig.cs:7`, `Smtp2GoSettings.cs:6`) y JWT de usuario para refunds.
**Decisión:** `encrypted_api_key` con envelope encryption + rotación (`key_version`); descifrado solo en memoria por-request; **nunca** JWT persistido; servicio-a-servicio por M2M client-credentials.
**Consecuencias:** Superficie de secretos controlada; rotación posible; sin fuga por dump de BD.

---

## ADR-EMAIL-005 — Webhooks con firma HMAC obligatoria + dedupe
**Estado:** APPROVED.
**Contexto:** El legado aceptaba webhooks `[AllowAnonymous]` sin verificar firma (`TrackingController.cs:133-140,238-241`), confiando `CampaignId` del body.
**Decisión:** Verificar HMAC antes de tocar dominio; persistir crudo con UNIQUE `provider_event_id`; correlacionar por `provider_message_id` server-side; proyectar con state guards monótonos.
**Consecuencias:** Bloquea envenenamiento de suppression/stats por eventos falsos; idempotente ante reintentos del proveedor.

---

## ADR-EMAIL-006 — Render por Scribe, no personalización por string.Replace
**Estado:** APPROVED (deriva de CAMP-000 §2, "Render = reusar Scribe").
**Contexto:** El legado personalizaba con `string.Replace`/regex (`Smtp2GoService.cs:420-472`): frágil, escape inconsistente, sin lógica.
**Decisión:** El cuerpo viaja pre-renderizado por Scribe (camino normal) o se renderiza vía Scribe (Fluid/Liquid) si viaja `TemplateKey`+variables. Assets inline por **referencia** (`EmailInlineAssetReference`), no bytes.
**Consecuencias:** Render consistente y seguro; ejecutor no re-renderiza si el cuerpo ya viaja (evita trabajo doble).

---

## ADR-EMAIL-007 — Costeo: el ejecutor reporta, no cobra
**Estado:** PROPOSED (depende de `../06_...` + `wallet-ledger/`).
**Contexto:** El legado cobraba al crear (prepay TOCTOU, wallet TXC en ReferralService, anti-patrón #4).
**Decisión:** El ejecutor Email **no** llama a Wallet; solo emite results (`sent`/`delivered`/`failed`/`suppressed`/...). La saga en Campaigns traduce a consume/refund; **solo Wallet muta saldo**. La política exacta (consume en `sent` vs `delivered`) se fija en `../06_...`.
**Consecuencias:** Separación limpia; el ejecutor no conoce precios ni moneda. **Blocker B-EMAIL-TX-1** hasta fijar la política.

---

## Blockers abiertos
| ID | Descripción | Bloquea |
|---|---|---|
| **B-EMAIL-TX-1** | Política de costeo consume/refund (evento gatillo) sin fijar en `../06_...`/`wallet-ledger/` | handlers de result, ADR-EMAIL-007 → APPROVED |
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
| 007 | `../05_Master_ADR.md` #4 | VERIFIED (política DOCUMENTED_ONLY) |
