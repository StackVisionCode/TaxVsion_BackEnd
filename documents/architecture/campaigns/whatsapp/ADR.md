# WhatsApp — ADRs

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**
- Sub-ADRs de `ADR-CAMP-000` (`../05_Master_ADR.md`). Prefijo de ID: **ADR-WA-###**.

---

## ADR-WA-001 — WhatsApp Business Platform (Meta Cloud API) nativa, no Twilio
**Estado:** APPROVED (deriva de ADR-CAMP-000 decisión 2).
**Contexto:** El legado usaba un `WhatsAppProvider` Twilio con `AuthToken` en texto plano (`appsettings.json:130-136`) y el sender era un stub simulado (`WhatsAppCampaignSender.cs:77-101`). La arquitectura fijada exige un ejecutor nuevo vía Meta/WhatsApp Business API.
**Decisión:** Integrar **directamente Meta Cloud API** (Graph API v-actual) como proveedor primario; el adaptador es propio y aislado (`WhatsAppProviderConfig.Provider`), dejando la puerta a otros BSP sin cambiar el dominio.
**Consecuencias:** control total de plantillas/categorías/webhooks/pricing; obligación de manejar onboarding WABA (B-WA-DEP-1) y firma de webhook.
**Alternativas:** Twilio/otro BSP como intermediario (rechazada: capa extra, costo, y el legado ya lo hacía mal).

## ADR-WA-002 — Template-first con ventana de sesión de 24h como invariante de dominio
**Estado:** APPROVED.
**Contexto:** Fuera de la sesión de 24h, Meta solo permite plantillas aprobadas (HSM). El legado ignoraba esto por completo.
**Decisión:** Modelar `SessionWindow` (derivada de inbounds) y exigir `TemplateRef Approved` cuando la ventana está cerrada; `FreeText` solo dentro de sesión. Un dispatch que viole la regla ⇒ `Rejected` local (sin gastar) con refund.
**Consecuencias:** validación previa al POST (ahorra costo y evita errores de Meta); necesidad de sincronizar catálogo de plantillas.
**Alternativas:** intentar el envío y dejar que Meta rechace (rechazada: gasta cuota, peor UX, sin control de costo).

## ADR-WA-003 — Costeo por conversación/categoría desde el webhook `pricing`, no tarifa plana
**Estado:** APPROVED (deriva de ADR-CAMP-000 decisión 3).
**Contexto:** El legado cobraba 0.005/0.01 plano al "enviar" (`CostService.cs:17`, `appsettings.json:141`), ignorando categoría, país, conversación, y sin confirmar entrega.
**Decisión:** El **estimado** de reserva se parametriza por `(Category, Country)` en Wallet/Campaigns; el **costo real** se toma del `pricing`/`conversation` del webhook y se envía a Wallet como `consume`. Migración de Meta a per-message pricing (jul-2025) se absorbe en la tabla de estimado; el real siempre viene firmado del webhook.
**Consecuencias:** costo exacto y auditable; dependencia del webhook para settlement (mitigada por reaper).
**Alternativas:** tarifa plana local (rechazada, es el bug del legado); confiar precio del frontend (prohibido por convención de dinero).

## ADR-WA-004 — Punto de consumo del saldo en `Delivered` (no en `Sent`)
**Estado:** APPROVED (default), con override configurable por política de tenant.
**Contexto:** ¿Cuándo se cobra? Al aceptar (prepay, como el TOCTOU del legado), al enviar, o al entregar.
**Decisión:** **`reserve` al aceptar** (garantiza saldo, no lo debita) y **`consume` en `Delivered`** con el costo real; **`refund` en `Failed/Rejected/timeout`**. Consume XOR refund por `DispatchId`. Un tenant puede optar por consume-en-`Sent` si su contabilidad lo requiere, sin cambiar la máquina de estado.
**Consecuencias:** no se cobra lo no entregado; ventana entre reserve y consume cubierta por la reserva inmutable del Wallet.
**Alternativas:** debit al crear (rechazada: es el anti-patrón §4, cobra fallidos y es TOCTOU).

## ADR-WA-005 — Webhook público idempotente y firmado; entrada no confiable
**Estado:** APPROVED.
**Contexto:** Meta reenvía webhooks, fuera de orden y duplicados; el endpoint es público.
**Decisión:** Verificar HMAC-SHA256 (App Secret) + `verify_token`; persistir envelope crudo y responder 200 rápido; procesar en inbox con dedupe `(wamid,status)` y guard monotónico; tenant derivado de `PhoneNumberId`, nunca del payload.
**Consecuencias:** resiliente a reenvíos/carreras; inmune a falsificación de estados/costo.
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
**Decisión:** Worker con **lease atómico** (`UPDATE ... WHERE lease libre RETURNING`) que resuelve rezagados a `Failed(timeout)`+refund, reconciliable si el webhook llega tarde. Owner (Scheduler central vs worker interno) se cierra en `../scheduler/ADR.md`; recomendación: worker interno.
**Consecuencias:** sin doble-envío al escalar; sin dinero atrapado indefinidamente.
**Alternativas:** poll loop sin lease (rechazada, es el anti-patrón).

## Blockers / Open questions
- **B-WA-DEP-1**: onboarding WABA + plantillas Approved + webhook por tenant (prerequisito operativo).
- **B-WA-DOM-2**: precios de Meta cambian (per-message jul-2025); tabla de estimado debe mantenerse; real siempre del webhook.
- **OQ-WA-1**: ¿reaper interno o del Scheduler central? → se decide en `../scheduler/ADR.md`.
- **OQ-WA-2**: ¿consume-en-Delivered global o política por tenant? (ADR-WA-004 deja override).

## Evidencia consolidada
| Decisión | Evidencia clave | Clasificación | Confianza |
|---|---|---|---|
| WA-001 | `appsettings.json:130-136`, `WhatsAppCampaignSender.cs:77-101`, `05_Master_ADR.md:28` | VERIFIED | 95% |
| WA-003 | `CostService.cs:17`, `appsettings.json:141`, `05_Master_ADR.md:29` | VERIFIED | 95% |
| WA-004 | ADR-CAMP-000 §4 `05_Master_ADR.md:47` | VERIFIED | 93% |
| WA-005 | Meta docs (firma/reenvío) | DOCUMENTED_ONLY | 87% |
| WA-006 | `PostmasterEmailEvents.cs:37` | VERIFIED | 95% |
| WA-007 | `CampaignStatus.cs:6`, `05_Master_ADR.md:49` | VERIFIED | 95% |
