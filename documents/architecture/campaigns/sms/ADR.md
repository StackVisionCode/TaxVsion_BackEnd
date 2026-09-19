# TaxVision.Sms — Architecture Decision Records

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — SMS NO es un servicio nuevo; `TaxVision.Sms` YA EXISTE y ya es M2M** (`SendSmsBatchCommand`, `POST /sms/messages`, `ActorType.Service`). El canal SMS es un **CONSUMER dentro de `TaxVision.Sms`** que procesa `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un microservicio SMS nuevo y/o un Wallet queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

- **Servicio:** SMS (`TaxVision.Sms`) — consumer del canal SMS **dentro de `TaxVision.Sms` (ya existente)**
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- **Padre:** `../05_Master_ADR.md` (ADR-CAMP-001, APPROVED — SUPERSEDE a ADR-CAMP-000 §Decisión 2: el SMS ya no es un servicio nuevo sino un consumer dentro de `TaxVision.Sms`)

---

## SMS-ADR-001 — Proveedor SMS (DECISIÓN ABIERTA)
**Estado:** PROPOSED (bloqueante para implementación)
**Contexto:** El legado usaba **Textmaxx** (`TextmaxxService.cs`), un proveedor de nicho con auth Basic `base64(clientApiKey:userApiToken)` (`TextmaxxService.cs:585-591`), un modelo de opt-in/verify propio y sin webhooks de estado estándar. No se porta.
**Opciones:**
| Proveedor | Pro | Contra |
|---|---|---|
| **Twilio** | DLR y webhooks maduros, 10DLC/short code/alfanumérico, idempotency keys, cobertura global, opt-out (Advanced Opt-Out) gestionado | vendor lock parcial — (removido: sin dinero en el canal; ver banner — antes comparativa de costo por segmento) |
| **AWS SNS** | barato, integra con infra AWS | opt-out/DLR más limitados, sin inbound rico, menos control de sender id |
| **Otro (MessageBird/Vonage/Sinch)** | competitivo, buena cobertura regional | otra curva de integración |
**Recomendación:** diseñar tras una **abstracción `ISmsProviderAdapter`** (un adapter por proveedor, como el patrón multi-provider de PaymentClient) y arrancar con **Twilio** por madurez de DLR/opt-out, dejando SNS como alternativa. **Decisión final pendiente del usuario.**
**Consecuencia:** el dominio (segmentación, opt-in) es **provider-agnóstico**; sólo el adapter cambia. El mapeo de DLR se parametriza por adapter. — (removido: sin dinero en el canal; ver banner — antes saga Wallet y costo por segmento).

---

## SMS-ADR-002 — Servicio propio vs. módulo de Campaigns
**Estado:** SUPERSEDED por ADR-CAMP-001 (ver banner) — el SMS **no es un servicio nuevo**: `TaxVision.Sms` YA EXISTE y el canal se añade como **consumer dentro de él**.
**Decisión (revisada):** el canal SMS es un **consumer dentro de `TaxVision.Sms` (ya existente)**, que consume `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`; Campaigns es orquestador agnóstico que no envía.
**Razón:** `TaxVision.Sms` ya es M2M-ready (`ActorType.Service` per `MessagesController.cs:21`, `SendSmsBatchCommand`, `POST /sms/messages`); sirve **también envíos individuales** (fuera de campañas). — (removido: sin dinero en el canal; ver banner — antes "consumiendo Wallet directamente").
**Consecuencia:** contrato dispatch/result común con Campaigns por eventos; el canal SMS no depende del ciclo de vida de una campaña.

---

## SMS-ADR-003 — `SmsDispatch` inmutable por intento vs. log mutable
**Estado:** ACCEPTED
**Decisión:** cada intento de envío es un `SmsDispatch` nuevo (`Attempt` incremental) — la materialización SMS del `CampaignDispatchAttempt` canónico —, no una fila mutada. `recipient_id` es **estable por unidad** (mismo entre intentos; contactos manuales ⇒ id generado, nunca el literal `"manual"`).
**Razón:** el legado mutaba `SmsSendLog.RetryCount/LastRetryAt` (`SmsSendLog.cs:59-61`) y no tenía idempotencia por destinatario (ADR-CAMP-000 §Anti-patrón 3). La unicidad `(tenant, campaign_run, recipient_id, attempt)` ≡ `UNIQUE(run_id, recipient_id, attempt_no)` hace idempotente el fan-out y auditable cada intento.
**Consecuencia:** stats derivadas de estados terminales, inmunes al doble-conteo; más filas, a cambio de auditabilidad y corrección.

---

## SMS-ADR-004 — ~~Reserve→consume/refund con costo por segmentos reales~~
**Estado:** SUPERSEDED por ADR-CAMP-001 (ver banner) — **sin dinero en el canal**.
**Decisión:** — (removido: sin dinero en el canal; ver banner — antes reserve/consume/refund contra Wallet por segmentos reales). La autorización por balance es un **interceptor/PEP externo + Wallet, DIFERIDO y fuera de este canal** (ver `../05_Master_ADR.md` D1/D3/D7).
**Consecuencia:** el canal SMS **no** depende de `TaxVision.Wallet` ni ejecuta saga de saldo; sólo segmenta, envía y reporta `Outcome = Accepted | Delivered | Failed | Skipped | Unknown` (canónico; timeout ⇒ `Unknown`, nunca `Failed`).

---

## SMS-ADR-005 — Opt-in/STOP como registry propio idempotente
**Estado:** ACCEPTED
**Decisión:** modelar consentimiento en `SmsOptInRegistry` por `(tenant, phone)`, con STOP/START/HELP procesados por webhook inbound idempotente; STOP es duro (corta marketing y transactional).
**Razón:** el legado dispersaba esto entre `SmsCell.OptInStatus` (`SmsCell.cs:108-115`) y `SmsIncomingMessage.SystemMessageType` (`SmsIncomingMessage.cs:79-89`) sin dedupe. El cumplimiento TCPA/carrier exige una fuente única y auditable.
**Consecuencia:** SMS no depende del modelo de contactos de Customer para el consentimiento; guarda sólo el mínimo (estado + prueba), no PII de negocio.

---

## SMS-ADR-006 — Segmentación GSM-7/Unicode como servicio de dominio
**Estado:** ACCEPTED
**Decisión:** `SmsSegmentation` VO/servicio puro con soporte correcto de GSM-7 base + extendido (caracteres de 2 septetos) y UCS-2 (surrogate pairs).
**Razón:** el cálculo del legado (`SmsCampaignSender.cs:402-427`) sólo miraba `c > 127`, ignorando el conjunto extendido GSM-7 y los pares surrogate ⇒ segmentos mal calculados en casos borde.
**Consecuencia:** la segmentación es precisa y testeable en aislamiento; base del `quote` `(encoding, segments)`. **"Cuántos segmentos" es relevante para facturación del proveedor pero Campaign NO lo tarifa** (sin dinero en el canal): es un atributo del envío/métrica, distinto de la entrega. — (removido: sin dinero en el canal; ver banner — antes base del costo y de la reserva).

---

## SMS-ADR-007 — Webhooks firmados vs. polling
**Estado:** ACCEPTED
**Decisión:** estado de entrega vía **webhook DLR firmado (HMAC)** + reconciliación de respaldo; no polling.
**Razón:** el legado no tenía webhook de estado (sólo `GET /messages/{phone}`, `SmsController.cs:313`), lo que impedía conocer la entrega real en tiempo razonable. El webhook alimenta el terminal `Delivered|Failed` exacto. — (removido: sin dinero en el canal; ver banner — antes alimentaba el `consume`).
**Consecuencia:** ruta pública `[RateLimitExempt]` protegida por firma; anti-replay por `ProcessedBusinessMessage`.

---

## Tabla de evidencia (consolidada)
| ADR | Evidencia clave | Clasificación | Confianza |
|---|---|---|---|
| 001 | `TextmaxxService.cs:585-591` (auth legado) | VERIFIED (contexto) | 96% |
| 002 | `MessagesController.cs:21` (`ActorType.Service`), `SendSmsBatch.cs` — `TaxVision.Sms` ya existe/M2M (SUPERSEDE la idea de servicio nuevo) | VERIFIED (decisión) | 96% |
| 003 | `SmsSendLog.cs:59-61` | VERIFIED | 97% |
| 004 | — (removido: sin dinero en el canal; ver banner — SUPERSEDED) | — | — |
| 005 | `SmsCell.cs:108-115`, `SmsIncomingMessage.cs:79-89` | VERIFIED | 95% |
| 006 | `SmsCampaignSender.cs:402-427` | VERIFIED | 97% |
| 007 | `SmsController.cs:313` | VERIFIED | 92% |
