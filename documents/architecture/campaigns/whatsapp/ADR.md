# WhatsApp — ADRs

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — WhatsApp SÍ es un servicio nuevo (`TaxVision.WhatsApp`, Meta/WABA), pero de FASE POSTERIOR y solo como CONSUMER del contrato de dispatch — sin dinero.** Consume `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un Wallet o cobro por este canal queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Sub-ADRs de `ADR-CAMP-000` (`../05_Master_ADR.md`). Prefijo de ID: **ADR-WA-###**.

---

## ADR-WA-001 — WhatsApp Business Platform (Meta Cloud API) nativa, no Twilio
**Estado:** APPROVED (deriva de ADR-CAMP-000 decisión 2).
**Contexto:** El legado usaba un `WhatsAppProvider` Twilio con `AuthToken` en texto plano (`appsettings.json:130-136`) y el sender era un stub simulado (`WhatsAppCampaignSender.cs:77-101`). La arquitectura fijada exige un ejecutor nuevo vía Meta/WhatsApp Business API.
**Decisión:** Integrar **directamente Meta Cloud API** (Graph API v-actual) como proveedor primario; el adaptador es propio y aislado (`WhatsAppProviderConfig.Provider`), dejando la puerta a otros BSP sin cambiar el dominio.
**Consecuencias:** control total de plantillas/categorías/webhooks; obligación de manejar onboarding WABA (B-WA-DEP-1) y firma de webhook. (Pricing — removido: sin dinero en el canal, ver banner.)
**Alternativas:** Twilio/otro BSP como intermediario (rechazada: capa extra y el legado ya lo hacía mal).

## ADR-WA-002 — Template-first con ventana de sesión de 24h como invariante de dominio
**Estado:** APPROVED.
**Contexto:** Fuera de la sesión de 24h, Meta solo permite plantillas aprobadas (HSM). El legado ignoraba esto por completo.
**Decisión:** Modelar `SessionWindow` (derivada de inbounds) y exigir `TemplateRef Approved` cuando la ventana está cerrada; `FreeText` solo dentro de sesión. Un dispatch que viole la regla ⇒ `Rejected` local (sin llamar a Meta). (Refund — removido: sin dinero en el canal, ver banner.)
**Consecuencias:** validación previa al POST (evita errores de Meta y ahorra cuota); necesidad de sincronizar catálogo de plantillas.
**Alternativas:** intentar el envío y dejar que Meta rechace (rechazada: gasta cuota, peor UX).

## ADR-WA-003 — ~~Costeo por conversación/categoría desde el webhook `pricing`~~ — SUPERSEDED
**Estado:** SUPERSEDED por ADR-CAMP-001.
**Decisión:** — (removido: sin dinero en el canal; no hay costeo/estimado/consume/pricing por este canal; la autorización por balance es un interceptor/PEP externo y diferido, ver banner y `../05_Master_ADR.md` D1/D3/D7).

## ADR-WA-004 — ~~Punto de consumo del saldo en `Delivered`~~ — SUPERSEDED
**Estado:** SUPERSEDED por ADR-CAMP-001.
**Decisión:** — (removido: sin dinero en el canal; no hay reserve/consume/refund de saldo en este canal; ver banner).

## ADR-WA-005 — Webhook público idempotente y firmado; entrada no confiable
**Estado:** APPROVED.
**Contexto:** Meta reenvía webhooks, fuera de orden y duplicados; el endpoint es público.
**Decisión:** Verificar HMAC-SHA256 (App Secret) + `verify_token`; persistir envelope crudo y responder 200 rápido; procesar en inbox con dedupe `(wamid,status)` y guard monotónico; tenant derivado de `PhoneNumberId`, nunca del payload.
**Consecuencias:** resiliente a reenvíos/carreras; inmune a falsificación de estados. (Falsificación de costo — removido: sin dinero en el canal, ver banner.)
**Alternativas:** procesar síncrono sin firma (rechazada: timeouts→más reenvíos, y falsificable).

## ADR-WA-006 — Contrato dispatch/result común con `CampaignId` opaco eco-intacto
**Estado:** APPROVED (deriva de ADR-CAMP-000 decisión 1).
**Contexto:** El sistema nuevo ya propaga `CampaignId` sin interpretarlo (`PostmasterEmailEvents.cs:37`).
**Decisión:** Reusar exactamente ese seam: WhatsApp recibe `CampaignId`, no lo interpreta, lo devuelve intacto en cada `WhatsAppDispatchResult`. Contratos copiados por contexto (sin tipos compartidos).
**Consecuencias:** Campaigns agrega stats sin acoplar al ejecutor; canales intercambiables.
**Alternativas:** metadata genérico (rechazada, el monorepo prefiere un campo nullable por origen).

## ADR-WA-007 — Reaper con lease atómico para `Sent`-sin-webhook
**Estado:** APPROVED.
**Contexto:** El legado tenía doble-scheduler y `Status=Sending` no-atómico (`CampaignStatus.cs:6`, §6).
**Decisión:** Worker con **lease atómico** (`UPDATE ... WHERE lease libre RETURNING`) que resuelve rezagados a `Failed(timeout)`, reconciliable si el webhook llega tarde. Owner (Scheduler central vs worker interno) se cierra en `../scheduler/ADR.md`; recomendación: worker interno. (Refund — removido: sin dinero en el canal, ver banner.)
**Consecuencias:** sin doble-envío al escalar; estado consistente ante rezagados. (Dinero atrapado — removido: sin dinero en el canal, ver banner.)
**Alternativas:** poll loop sin lease (rechazada, es el anti-patrón).

## Blockers / Open questions
- **B-WA-DEP-1**: onboarding WABA + plantillas Approved + webhook por tenant (prerequisito operativo).
- **B-WA-DOM-2**: — (removido: sin dinero en el canal; precios/estimado/pricing ya no aplican a este canal; ver banner).
- **OQ-WA-1**: ¿reaper interno o del Scheduler central? → se decide en `../scheduler/ADR.md`.
- **OQ-WA-2**: — (removido: sin dinero en el canal; consume/settlement ya no aplica; ver banner).

## Evidencia consolidada
| Decisión | Evidencia clave | Clasificación | Confianza |
|---|---|---|---|
| WA-001 | `appsettings.json:130-136`, `WhatsAppCampaignSender.cs:77-101`, `05_Master_ADR.md:28` | VERIFIED | 95% |
| WA-005 | Meta docs (firma/reenvío) | DOCUMENTED_ONLY | 87% |
| WA-006 | `PostmasterEmailEvents.cs:37` | VERIFIED | 95% |
| WA-007 | `CampaignStatus.cs:6`, `05_Master_ADR.md:49` | VERIFIED | 95% |
