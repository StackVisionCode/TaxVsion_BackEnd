# TaxVision.Sms — Idempotency Spec

- **Servicio:** SMS (`TaxVision.Sms`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

Dos capas independientes (mismo principio que el Overview §Reglas duras):
1. **Transporte:** el durable inbox de Wolverine deduplica envelopes por su MessageId.
2. **Efecto de negocio:** `ProcessedBusinessMessage` (copia local del patrón `Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-124`) protege el efecto material aunque el envelope sea distinto (retry con nuevo MessageId, reproceso manual, etc.). "Nunca exactly-once": at-least-once + dedupe.

## 1. Claves de idempotencia por operación

| Operación (`operation`) | `scopeId` | `idempotencyKey` | Constraint único |
|---|---|---|---|
| Dispatch de campaña | `DispatchId` | `sms-dispatch-{campaignRunId}-{recipientId}-{attempt}` | `(tenant, campaign_id, recipient_key, attempt)` en `sms_dispatch` |
| Envío individual | `DispatchId` | header `Idempotency-Key` del caller | `(tenant, idempotency_key)` en `sms_dispatch` |
| Wallet reserve | `DispatchId` | `sms-reserve-{dispatchId}` | dedupe en Wallet |
| Wallet consume | `DispatchId` | `sms-consume-{dispatchId}` | dedupe en Wallet |
| Wallet refund | `DispatchId` | `sms-refund-{dispatchId}` | dedupe en Wallet |
| DLR (delivery receipt) | `DispatchId` | `sms-dlr-{provider}-{providerMessageId}-{eventType}` | `ProcessedBusinessMessage` |
| Inbound STOP/START | `OptInRegistryId` | `sms-inbound-{provider}-{providerMessageId}` | `ProcessedBusinessMessage` |

## 2. Protocolo `ProcessedBusinessMessage`
Por cada efecto de negocio dedupable (DLR, inbound, y la creación de dispatch individual):
1. `Begin(tenantId, operation, scopeId, idempotencyKey, requestFingerprint, now, expiresAt)` con `requestFingerprint` = SHA-256 hex(64) del payload canónico (el aggregate lo exige, `ProcessedBusinessMessage.cs:52-56`).
2. Si ya existe con **mismo fingerprint** ⇒ replay: devolver la respuesta persistida (`ResponseJson`/status) sin re-ejecutar.
3. Si existe con **fingerprint distinto** ⇒ conflicto (`sms.idempotency.conflict`, 409): misma clave, payload distinto.
4. Ejecutar el efecto; `Complete(...)` o `Fail(...)` en la misma tx (`ProcessedBusinessMessage.cs:76-105`).
5. `expiresAtUtc` acota la ventana de dedupe (housekeeping); tras expirar se purga.

## 3. Idempotencia por destinatario (fix del anti-patrón central)
El legado **no tenía idempotencia por destinatario** y marcaba `Sent` a todos los no-fallidos, con contadores de tracking que **doble-contaban en reintento** (ADR-CAMP-000 §Anti-patrón 3; conteo en `SmsCampaignSender.cs:307-317`). Aquí:
- La `UNIQUE (tenant, campaign_id, recipient_key, attempt)` hace que reprocesar `SmsDispatchRequested` sea un no-op idempotente (INSERT que colisiona ⇒ se carga el existente).
- Cada reintento es `attempt+1` ⇒ un dispatch nuevo, nunca una mutación del anterior.
- Los contadores de stats se derivan de estados terminales de `SmsDispatch`, no de incrementos imperativos ⇒ inmunes al doble-conteo.

## 4. Webhooks idempotentes (DLR e inbound)
- Un proveedor puede reentregar el mismo DLR varias veces. La clave `(provider, providerMessageId, eventType)` garantiza un consume/refund único.
- STOP repetido ⇒ `SmsOptInRegistry.Stop()` es idempotente por diseño (estado ya `StoppedByUser` ⇒ no-op), reforzado por `ProcessedBusinessMessage`.

## 5. Idempotencia frente al proveedor externo
Al enviar, se pasa un **client reference** determinístico (`clientRef = dispatchId`) al proveedor cuando soporta idempotencia de envío (ej. Twilio idempotency / `X-Idempotency-Key`), para que un reintento tras crash no genere dos SMS reales. Si el proveedor no lo soporta, la reconciliación por estado (`Transactional_Protocol.md` §5) es la red de seguridad.

## 6. Tabla de evidencia
| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| `ProcessedBusinessMessage` con fingerprint SHA-256 y Begin/Complete/Fail | `ProcessedBusinessMessage.cs:27-105` | VERIFIED | 97% |
| Legado sin idempotencia por destinatario / doble-conteo | `SmsCampaignSender.cs:307-317`, ADR-CAMP-000 §Anti-patrón 3 | VERIFIED | 95% |
| `IdempotencyKey` VO ≤200 chars | `PaymentApp.Domain/ValueObjects/IdempotencyKey.cs:20-26` | VERIFIED | 97% |
| Claves y protocolo SMS concretos | este documento | NEW | — |
