# WhatsApp — Idempotency Spec

- Servicio: **TaxVision.WhatsApp** (NEW)
- Fecha: 2026-07-28
- Estado: **DISEÑO — no implementado**

## 1. Dos capas de deduplicación (obligatorio)

1. **Transporte** — Wolverine durable inbox deduplica envelopes repetidos del bus (crash/retry).
2. **Efecto de negocio** — `ProcessedBusinessMessage` (copia local del patrón `Growth/.../Idempotency/ProcessedBusinessMessage.cs:9-108`) protege una operación por `(TenantId, Operation, ScopeId, IdempotencyKey, RequestFingerprint)`. `Begin → Complete|Fail`; un segundo intento con **mismo fingerprint** devuelve la respuesta guardada; con **fingerprint distinto** ⇒ conflicto (mismo key, payload divergente) → rechazo.

`at-least-once + idempotente`, **nunca** exactly-once (regla dura del suite, `00_Overview_And_Index.md:45`).

## 2. Claves por operación

| Operación | `Operation` | `ScopeId` | `IdempotencyKey` | Garantía |
|---|---|---|---|---|
| Aceptar dispatch | `wa.accept` | `DispatchId` | `Attempt` | un `WhatsAppMessage` por `(CampaignId, RecipientRef, Attempt)` |
| POST a Meta | `wa.send` | `DispatchId` | `wamid`-once | no dos `Sent`; `wamid` UNIQUE |
| Aplicar estado webhook | `wa.status` | `wamid` | `status` | cada estado se aplica una vez; monotónico |
| Consume Wallet | `wa.settle` | `DispatchId` | `"consume"` | un solo consume |
| Refund Wallet | `wa.settle` | `DispatchId` | `"refund"` | un solo refund; excluyente con consume |
| Envío individual (HTTP) | `wa.send.api` | `TenantId`-scoped | header `Idempotency-Key` | POST repetido = mismo `DispatchId` |
| Sync plantilla | `wa.tpl.sync` | `MetaTemplateId` | `Version` | sin duplicar versiones |

**Clave maestra de destinatario:** `(CampaignId, RecipientRef, Attempt)` — corrige el anti-patrón §3 de ADR-CAMP-000 (legado marcaba `Sent` a todos los no-fallidos y doble-contaba tracking en reintento de webhook).

## 3. Idempotencia de webhooks de Meta

Meta reenvía webhooks si no recibe 200 a tiempo, y puede entregar el **mismo** status más de una vez y **fuera de orden**. Reglas:
- El handler es idempotente por `(wamid, status)` (`ProcessedBusinessMessage op="wa.status"`).
- El avance de estado es **monotónico** (`Sent<Delivered<Read`); un status ya superado se **descarta** sin efecto.
- `failed` tras estado terminal de éxito ⇒ **ignorado** (log only; no refund tras consume). Evita el doble-conteo/doble-refund.
- El endpoint persiste el envelope crudo y responde 200 rápido; el trabajo va al inbox (evita timeouts que disparan más reenvíos).

## 4. Settlement excluyente (consume XOR refund)
- `wa.settle/DispatchId` admite **una** terminal: o `consume` (Delivered) o `refund` (Failed/Rejected/timeout). Si ambos se intentan por carrera (webhook delivered tardío vs reaper timeout), gana el primero que complete `ProcessedBusinessMessage`; el segundo ve `Completed` y reconcilia (ver `Transactional_Protocol.md §6`) en vez de duplicar.

## 5. Fingerprint
`RequestFingerprint` = SHA-256 (64 hex) del payload canónico relevante (para `wa.send`: To+Template+Variables; para webhook: cuerpo normalizado). Distinto fingerprint con misma key ⇒ conflicto explícito (defensa contra replay malicioso o payload corrompido), igual que `HasSameFingerprint` en el patrón base (`ProcessedBusinessMessage.cs:107`).

## 6. Evidencia
| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón business-inbox (Begin/Complete/Fail/fingerprint) | `ProcessedBusinessMessage.cs:27-108` | VERIFIED | 97% |
| Regla at-least-once del suite | `00_Overview_And_Index.md:45` | VERIFIED | 96% |
| Anti-patrón sin idempotencia por destinatario | `05_Master_ADR.md:46` (§3) | VERIFIED | 95% |
| Reenvío/orden de webhooks Meta | Meta Cloud API docs | DOCUMENTED_ONLY | 86% |
