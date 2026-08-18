# Campaigns — API Contracts

- **Servicio:** Campaigns (`TaxVision.Campaigns`)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado

REST público (tenant, JWT usuario) + M2M (client-credentials) hacia/desde Wallet, Subscription, Scheduler y ejecutores. Todo endpoint público lleva `[RateLimit(categoría)]` o `[RateLimitExempt]` (ver `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md`) y `[HasPermission]` (RBAC acumulativo). Dinero en minor units USD; el frontend **nunca** envía montos ni precios.

---

## 1. Convenciones

- Base: `/api/campaigns`. Versión: `v1`.
- Auth pública: JWT usuario, actor-type `tenant-user`, `[HasPermission("campaigns:*")]`, tenant del token (fail-closed).
- Auth M2M: audience `campaigns.api`, scopes `campaigns.result.write`, `campaigns.run.read`.
- Idempotencia de escritura: header `Idempotency-Key` obligatorio en POST que crean/disparan (ver `Idempotency_Spec.md`).
- Errores: `ProblemDetails` RFC7807; `409` para conflicto de estado, `422` para invariante de dominio, `402` reservado a "saldo insuficiente" (viene de la saga, no del gate).
- Paginación cursor-based en listados.

---

## 2. Endpoints públicos (tenant-user)

### 2.1 Definición de Campaign

| Método | Ruta | Permiso | RateLimit | Descripción |
|---|---|---|---|---|
| `POST` | `/v1/campaigns` | `campaigns:write` | `write` | Crea Campaign en `Draft`. Body: name, channelSpec, audienceSpec (ref), templateRef, objective. **Sin precio/costo.** |
| `GET` | `/v1/campaigns` | `campaigns:read` | `read` | Lista (cursor, filtros: status, channel). |
| `GET` | `/v1/campaigns/{id}` | `campaigns:read` | `read` | Detalle + readiness. |
| `PATCH` | `/v1/campaigns/{id}` | `campaigns:write` | `write` | Editar (solo `Draft`; `409` si no). |
| `POST` | `/v1/campaigns/{id}/ready` | `campaigns:write` | `write` | `Draft→Ready` (valida completo; `422` si falta). |
| `POST` | `/v1/campaigns/{id}/archive` | `campaigns:write` | `write` | Soft-archive. |

### 2.2 Estimación (previa a gastar)

| Método | Ruta | Permiso | RateLimit | Descripción |
|---|---|---|---|---|
| `POST` | `/v1/campaigns/{id}/estimate` | `campaigns:read` | `read` | Devuelve `recipientCount`, `unitPriceMinor` (del catálogo, server-side), `estimatedCostMinor`, `walletAvailableMinor`, `gateActive` (module.campaigns). Solo lectura; no reserva. |

`estimate` es la respuesta honesta que el legado no daba: separa **gate** (`gateActive`) de **balance** (`walletAvailableMinor`) — son ortogonales (ADR-CAMP-000 §Decisiones/#5).

### 2.3 Schedule / disparo

| Método | Ruta | Permiso | RateLimit | Descripción |
|---|---|---|---|---|
| `POST` | `/v1/campaigns/{id}/schedule` | `campaigns:send` | `write` | Fija ScheduleSpec (immediate/scheduled/recurring). Delega el reloj al Scheduler. |
| `POST` | `/v1/campaigns/{id}/trigger` | `campaigns:send` | `write` | Dispara ya: crea un **CampaignRun**. Requiere `Idempotency-Key`. `202 Accepted` + `runId`. |
| `POST` | `/v1/campaigns/{id}/unschedule` | `campaigns:send` | `write` | Quita el schedule (`Scheduled→Ready`). |

### 2.4 Runs y resultados (lectura)

| Método | Ruta | Permiso | RateLimit | Descripción |
|---|---|---|---|---|
| `GET` | `/v1/campaigns/{id}/runs` | `campaigns:read` | `read` | Runs de la campaña (inmutables, con estado + contadores). |
| `GET` | `/v1/runs/{runId}` | `campaigns:read` | `read` | Detalle de run: snapshot, cost estimate/actual, RunCounters, wallet reservation id. |
| `GET` | `/v1/runs/{runId}/recipients` | `campaigns:read` | `read` | Destinatarios + DispatchState + tracking (cursor). |
| `POST` | `/v1/runs/{runId}/cancel` | `campaigns:send` | `write` | Cancela un run en curso (`Dispatching→Cancelling`); liquida lo entregado. |

### 2.5 Tracking público (webhook de canal / pixel-redirect)

| Método | Ruta | Auth | RateLimit | Descripción |
|---|---|---|---|---|
| `GET` | `/v1/t/o/{token}` | anónimo (token firmado) | `[RateLimitExempt]` (pixel) | Open tracking (pixel 1×1). Idempotente set-once. |
| `GET` | `/v1/t/c/{token}` | anónimo (token firmado) | `tracking` | Click → 302 a destino. Idempotente. |

Los tokens son opacos firmados (HMAC), resuelven `(runId, recipientId)` server-side. **Nunca** PII ni ids crudos en la URL (regla de privacidad). El open pixel es exempt de rate-limit categórico pero protegido por validez de token + dedupe.

---

## 3. Contratos M2M salientes (Campaigns → otros)

Campaigns **llama** a estos como cliente (no expone estos endpoints):

| Destino | Operación | Idempotencia | Notas |
|---|---|---|---|
| Wallet | `RESERVE(walletAccountId, runId, amountMinor, key)` | `(reserve, runId)` | movimiento inmutable; devuelve `reservationId` |
| Wallet | `CONSUME(reservationId, amountMinor, key)` | `(consume, runId)` | consume parcial (entregados) |
| Wallet | `REFUND/RELEASE(reservationId, amountMinor, key)` | `(refund, runId)` | libera no-entregados |
| Subscription | `GET entitlement(tenantId, "module.campaigns")` | — (idempotente por naturaleza) | gate; cacheable corto |
| Customer | `RESOLVE audience(audienceSpec)` | — | materializa contactos del run (no snapshot en Campaign) |
| Scheduler | `REGISTER schedule(campaignId, spec)` | `(schedule, campaignId, version)` | owner del reloj/lease |

Preferentemente vía **eventos Wolverine** (outbox durable) donde el flujo es asíncrono (reserve/consume/refund, dispatch). Las lecturas (entitlement, resolve) pueden ser request/response M2M sincrónico. Ver `Commands_And_Events.md`.

---

## 4. Contratos entrantes (otros → Campaigns)

| Origen | Mensaje | Efecto |
|---|---|---|
| Ejecutores (Email/SMS/WA/Push) | `ChannelDispatchResult{ dispatchIdempotencyKey, outcome, providerMessageId }` | avanza DispatchState del recipient (idempotente) |
| Ejecutores | `ChannelTrackingEvent{ dispatchIdempotencyKey, kind(open/click/bounce), providerEventId }` | tracking set-once/dedupe |
| Wallet | `ReservationConfirmed` / `Rejected(insufficient)` | avanza RunStatus (`Reserving→Dispatching`/`Rejected`) |
| Scheduler | `RunDue(campaignId, triggerKind, leaseToken)` | `StartCampaignRun` |
| PaymentApp→Wallet | (no toca Campaigns; top-up es Wallet) | — |

El contrato result **común por destinatario** generaliza el seam que hoy existe Notification↔Postmaster: el definidor pone una correlación opaca (`CampaignId`/`dispatchIdempotencyKey`) y el ejecutor la devuelve intacta sin interpretarla (`PostmasterEmailEvents.cs:37,104,120,137,151,169`).

---

## 5. Ejemplo: crear + estimar + disparar

```http
POST /api/campaigns/v1/campaigns
Authorization: Bearer <jwt>
Idempotency-Key: 6f1c...

{ "name":"Aviso vencimiento IVA",
  "channelSpec":{"channel":"Email","schemaVersion":1,"subject":"Tu declaración vence"},
  "audienceSpec":{"kind":"Segment","audienceRef":"seg_9a..."},
  "templateRef":{"scribeTemplateKey":"campaign.tax_due.v3"},
  "objective":"Retention" }
→ 201 { "id":"cmp_..","status":"Draft" }

POST /api/campaigns/v1/campaigns/cmp_../estimate
→ 200 { "recipientCount":812, "unitPriceMinor":100,        // $0.001? -> ver nota
        "estimatedCostMinor":812, "walletAvailableMinor":50000,
        "gateActive":true }

POST /api/campaigns/v1/campaigns/cmp_../trigger
Idempotency-Key: a11e...
→ 202 { "runId":"run_..","status":"Reserving" }
```

Nota de precio: `unitPriceMinor` es server-side, congelado en el CampaignRun al disparar; el frontend lo muestra pero nunca lo provee (corrige `CreateCampaignCommandHandler.cs:219` que confiaba el costo calculado localmente).

---

## 6. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Result events con correlación opaca devuelta = modelo del contrato entrante | `PostmasterEmailEvents.cs:91-172` | VERIFIED | 97% |
| Legado confiaba costo local al crear | `CreateCampaignCommandHandler.cs:219` | VERIFIED | 95% |
| Legado hacía check+debit en 2 HTTP calls (se reemplaza por saga reserve) | `CreateCampaignCommandHandler.cs:250-295` | VERIFIED | 96% |
| Convención `[RateLimit]`/`[HasPermission]` obligatoria | `documents/RateLimit/Guia_Nuevos_Servicios_Endpoints.md`, CLAUDE.md | DOCUMENTED_ONLY | 90% |
| Endpoints REST/M2M de Campaigns | diseño (este doc) | NEW | 85% |
