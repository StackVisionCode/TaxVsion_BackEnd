# WhatsApp — Idempotency Spec

> **REVISIÓN 2026-09-16 (ADR-CAMP-001, APPROVED) — WhatsApp SÍ es un servicio nuevo (`TaxVision.WhatsApp`, Meta/WABA), pero de FASE POSTERIOR y solo como CONSUMER del contrato de dispatch — sin dinero.** Consume `campaign.dispatch.requested.v1` y responde `campaign.dispatch.result.v1`. Campaign es un orquestador agnóstico que **no envía**. **Sin dinero:** este doc NO reserva/consume/cobra saldo; la autorización por balance es un interceptor/PEP externo y DIFERIDO (ver `../05_Master_ADR.md` D1/D3/D7). Todo lo que abajo asuma un Wallet o cobro por este canal queda **superseded**. Canónico: `../campaigns/` + `../05_Master_ADR.md`.

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
| Envío individual (HTTP) | `wa.send.api` | `TenantId`-scoped | header `Idempotency-Key` | POST repetido = mismo `DispatchId` |
| Sync plantilla | `wa.tpl.sync` | `MetaTemplateId` | `Version` | sin duplicar versiones |

> Operaciones `wa.settle` de **Consume/Refund Wallet** — (removidas: sin dinero en el canal; ver banner).

**Clave maestra de destinatario:** `(CampaignId, RecipientRef, Attempt)` — corrige el anti-patrón §3 de ADR-CAMP-000 (legado marcaba `Sent` a todos los no-fallidos y doble-contaba tracking en reintento de webhook).

## 3. Idempotencia de webhooks de Meta

Meta reenvía webhooks si no recibe 200 a tiempo, y puede entregar el **mismo** status más de una vez y **fuera de orden**. Reglas:
- El handler es idempotente por `(wamid, status)` (`ProcessedBusinessMessage op="wa.status"`).
- El avance de estado es **monotónico** (`Sent<Delivered<Read`); un status ya superado se **descarta** sin efecto.
- `failed` tras estado terminal de éxito ⇒ **ignorado** (log only). Evita el doble-conteo. (Efecto de refund/consume — removido: sin dinero en el canal, ver banner.)
- El endpoint persiste el envelope crudo y responde 200 rápido; el trabajo va al inbox (evita timeouts que disparan más reenvíos).

## 4. Settlement excluyente (consume XOR refund) — (removido)
- — (removido: sin dinero en el canal; no hay settlement/consume/refund de saldo en este canal; la autorización por balance es externa/diferida, ver banner). La reconciliación de **estado** ante carrera webhook-tardío↔reaper se mantiene por `DispatchId` (ver `Transactional_Protocol.md §6`).

## 5. Fingerprint
`RequestFingerprint` = SHA-256 (64 hex) del payload canónico relevante (para `wa.send`: To+Template+Variables; para webhook: cuerpo normalizado). Distinto fingerprint con misma key ⇒ conflicto explícito (defensa contra replay malicioso o payload corrompido), igual que `HasSameFingerprint` en el patrón base (`ProcessedBusinessMessage.cs:107`).

## 6. Evidencia
| Hecho | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Patrón business-inbox (Begin/Complete/Fail/fingerprint) | `ProcessedBusinessMessage.cs:27-108` | VERIFIED | 97% |
| Regla at-least-once del suite | `00_Overview_And_Index.md:45` | VERIFIED | 96% |
| Anti-patrón sin idempotencia por destinatario | `05_Master_ADR.md:46` (§3) | VERIFIED | 95% |
| Reenvío/orden de webhooks Meta | Meta Cloud API docs | DOCUMENTED_ONLY | 86% |
