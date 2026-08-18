# TaxVision.Sms — Architecture Decision Records

- **Servicio:** SMS (`TaxVision.Sms`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- **Padre:** `../05_Master_ADR.md` (ADR-CAMP-000 §Decisión 2: SMS = nuevo `TaxVision.Sms`)

---

## SMS-ADR-001 — Proveedor SMS (DECISIÓN ABIERTA)
**Estado:** PROPOSED (bloqueante para implementación)
**Contexto:** El legado usaba **Textmaxx** (`TextmaxxService.cs`), un proveedor de nicho con auth Basic `base64(clientApiKey:userApiToken)` (`TextmaxxService.cs:585-591`), un modelo de opt-in/verify propio y sin webhooks de estado estándar. No se porta.
**Opciones:**
| Proveedor | Pro | Contra |
|---|---|---|
| **Twilio** | DLR y webhooks maduros, 10DLC/short code/alfanumérico, idempotency keys, cobertura global, opt-out (Advanced Opt-Out) gestionado | costo por segmento mayor; vendor lock parcial |
| **AWS SNS** | barato, integra con infra AWS | opt-out/DLR más limitados, sin inbound rico, menos control de sender id |
| **Otro (MessageBird/Vonage/Sinch)** | competitivo, buena cobertura regional | otra curva de integración |
**Recomendación:** diseñar tras una **abstracción `ISmsProviderAdapter`** (un adapter por proveedor, como el patrón multi-provider de PaymentClient) y arrancar con **Twilio** por madurez de DLR/opt-out, dejando SNS como alternativa de bajo costo. **Decisión final pendiente del usuario.**
**Consecuencia:** el dominio (segmentación, opt-in, saga Wallet) es **provider-agnóstico**; sólo el adapter cambia. El costo por segmento y el mapeo de DLR se parametrizan por adapter.

---

## SMS-ADR-002 — Servicio propio vs. módulo de Campaigns
**Estado:** ACCEPTED
**Decisión:** SMS es un **microservicio independiente** (`TaxVision.Sms`), no un módulo de Campaigns.
**Razón:** requisito del Overview — SMS sirve **también envíos individuales** (fuera de campañas) consumiendo Wallet directamente. Un servicio propio permite reuso por otros contextos (recordatorios, OTP futuros) sin acoplar a Campaigns, y aísla los secretos del proveedor SMS.
**Consecuencia:** contrato dispatch/result común con Campaigns por eventos; SMS no depende del ciclo de vida de una campaña.

---

## SMS-ADR-003 — `SmsDispatch` inmutable por intento vs. log mutable
**Estado:** ACCEPTED
**Decisión:** cada intento de envío es un `SmsDispatch` nuevo (`Attempt` incremental), no una fila mutada.
**Razón:** el legado mutaba `SmsSendLog.RetryCount/LastRetryAt` (`SmsSendLog.cs:59-61`) y no tenía idempotencia por destinatario (ADR-CAMP-000 §Anti-patrón 3). La unicidad `(tenant, campaign, recipient, attempt)` hace idempotente el fan-out y auditable cada intento.
**Consecuencia:** stats derivadas de estados terminales, inmunes al doble-conteo; más filas, a cambio de auditabilidad y corrección.

---

## SMS-ADR-004 — Reserve→consume/refund con costo por segmentos reales
**Estado:** ACCEPTED
**Decisión:** reservar el estimate antes de enviar; **consumir** sólo en `Delivered` por el costo **actual** (segmentos reales); **refundar** en terminal fallido. Solo Wallet muta saldo.
**Razón:** corrige el TOCTOU y el cobro-al-crear del legado (ADR-CAMP-000 §Anti-patrón 4). La segmentación es determinística ⇒ estimate ≈ actual, con conciliación cuando el proveedor reporta MCC/MNC distinto.
**Consecuencia:** dependencia dura de `TaxVision.Wallet`; saga distribuida (ver `Transactional_Protocol.md`).

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
**Razón:** el cálculo del legado (`SmsCampaignSender.cs:402-427`) sólo miraba `c > 127`, ignorando el conjunto extendido GSM-7 y los pares surrogate ⇒ segmentos (y costo) mal calculados en casos borde.
**Consecuencia:** el costo es preciso y testeable en aislamiento; base del `quote` y de la reserva.

---

## SMS-ADR-007 — Webhooks firmados vs. polling
**Estado:** ACCEPTED
**Decisión:** estado de entrega vía **webhook DLR firmado (HMAC)** + reconciliación de respaldo; no polling.
**Razón:** el legado no tenía webhook de estado (sólo `GET /messages/{phone}`, `SmsController.cs:313`), lo que impedía conocer entrega real en tiempo/costo razonable. El webhook alimenta el `consume` exacto.
**Consecuencia:** ruta pública `[RateLimitExempt]` protegida por firma; anti-replay por `ProcessedBusinessMessage`.

---

## Tabla de evidencia (consolidada)
| ADR | Evidencia clave | Clasificación | Confianza |
|---|---|---|---|
| 001 | `TextmaxxService.cs:585-591` (auth legado) | VERIFIED (contexto) | 96% |
| 002 | Overview §Servicios #5 (SMS también individual) | VERIFIED (decisión) | 97% |
| 003 | `SmsSendLog.cs:59-61` | VERIFIED | 97% |
| 004 | ADR-CAMP-000 §Anti-patrón 4 | VERIFIED | 95% |
| 005 | `SmsCell.cs:108-115`, `SmsIncomingMessage.cs:79-89` | VERIFIED | 95% |
| 006 | `SmsCampaignSender.cs:402-427` | VERIFIED | 97% |
| 007 | `SmsController.cs:313` | VERIFIED | 92% |
