# Wallet/Ledger — API Contracts

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado
- Ver `Security.md` (M2M audience/scopes), `Idempotency_Spec.md`, `Concurrency_Spec.md`.

---

## 1. Superficie

Wallet expone **API M2M interna** (service-to-service, client-credentials) — NO endpoints de usuario final. Audience `taxvision-wallet`, scopes por operación (`Security.md §Scopes`). Todos los endpoints mutantes exigen header **`Idempotency-Key`**. Todos los endpoints públicos llevan `[RateLimit(categoría)]` o `[RateLimitExempt]` (ver `RateLimit/Guia_Nuevos_Servicios_Endpoints.md`). Dinero SIEMPRE en `amountCents:long` + `currency` ISO-4217; **el frontend nunca envía montos** — los consumidores M2M (Campaigns/SMS) envían el costo calculado server-side.

Contrato de error uniforme: `Result` → `{ "error": { "code": "Wallet.InsufficientFunds", "message": "..." } }` con HTTP mapeado (`409`/`422`/`404`/`403`). Éxito → `2xx` con el recurso.

## 2. Endpoints

### 2.1 `POST /api/wallet/reservations` — Reserve

Aparta fondos para un scope. Idempotente por `Idempotency-Key`.

- **Scope:** `wallet:reserve` · **Rate limit:** `Internal` · **Idempotency-Key:** requerido
- **Request:**
```json
{
  "tenantId": "guid",
  "amountCents": 15000,
  "currency": "USD",
  "scopeId": "guid",              // CampaignRunId / SmsSendId (opaco)
  "consumerContext": "campaigns", // etiqueta, no autoriza nada
  "expiresAtUtc": "2026-08-04T18:00:00Z"  // opcional; hold auto-expira
}
```
- **200:**
```json
{ "reservationId":"guid","amountCents":15000,"currency":"USD",
  "status":"Held","availableCentsAfter":48000,"heldCentsAfter":15000 }
```
- **409 `Wallet.InsufficientFunds`** si `Available < amountCents`. **422 `Wallet.CurrencyMismatch`** / **`Wallet.BalanceFrozen`**.
- **Replay** (misma key + mismo fingerprint) → **200** con el mismo `reservationId` (no crea otra). Misma key + fingerprint distinto → **409 `Wallet.IdempotencyConflict`**.

### 2.2 `POST /api/wallet/reservations/{id}/consume` — Consume (acumulativo)

Consume (parcial o total) de una reserva Held. Convierte held → posted gastado.

- **Scope:** `wallet:consume` · **Idempotency-Key:** requerido
- **Request:** `{ "amountCents": 9000, "currency":"USD", "reason":"delivered:600" }`
- **200:** `{ "reservationId":"guid","status":"Held","consumedCents":9000,"remainingCents":6000 }`
  o `"status":"Consumed"` si `remainingCents==0`.
- **422 `Wallet.ConsumeExceedsReservation`** si `amount > remaining`. **409 `Wallet.ReservationNotConsumable`** si terminal (clave nueva).

### 2.3 `POST /api/wallet/reservations/{id}/refund` — Release / RefundRemainder

Libera el remanente no consumido de vuelta a Available.

- **Scope:** `wallet:refund` · **Idempotency-Key:** requerido
- **Request:** `{ "currency":"USD", "reason":"run-completed" }`  (libera todo el remaining)
- **200:** `{ "reservationId":"guid","status":"Released","releasedCents":6000,"availableCentsAfter":54000 }`
- Idempotente: re-refund de una reserva ya Released/Expired → **200** replay (no doble-devuelve).

### 2.4 `POST /api/wallet/adjustments` — Adjust (admin/system)

Ajuste manual (corrección, cortesía, corrección de reconciliación). Nunca edita entries; inserta un `Adjust`.

- **Scope:** `wallet:adjust` (solo actor admin/platform) · **Idempotency-Key:** requerido
- **Request:** `{ "tenantId":"guid","signedAmountCents":-500,"currency":"USD","reason":"chargeback-XYZ" }`
- **200:** `{ "postedCentsAfter":..., "availableCentsAfter":... }`
- **422 `Wallet.AdjustWouldGoNegative`** si dejaría `Available < 0`.

### 2.5 `GET /api/wallet/balances/{tenantId}` — GetBalance

- **Scope:** `wallet:read` · **Rate limit:** `Internal`
- **200:**
```json
{ "tenantId":"guid","currency":"USD","postedCents":54000,
  "heldCents":6000,"availableCents":48000,"status":"Active" }
```
- Fail-closed: si el actor M2M no tiene autorización sobre ese tenant → **403**.

### 2.6 `GET /api/wallet/ledger/{tenantId}` — Ledger (auditoría, paginado)

- **Scope:** `wallet:read` · Query: `?from&to&kind&scopeId&cursor&limit`
- **200:** lista inmutable de `LedgerEntry` con `balanceAfterPostedCents/balanceAfterHeldCents`, `kind`, `signedAmountCents`, `operation`, `scopeId`, `sourceReference`, `actorType`, `createdAtUtc`.

## 3. Ingreso de saldo (Recharge) — NO es un endpoint público

**Recharge no se expone como POST arbitrario.** Se acredita **solo** por evento de PaymentApp tras un top-up cobrado exitosamente (`Commands_And_Events.md §Top-up`). Esto evita crear saldo sin cobro. El único camino a `Recharge` es el consumer de `WalletTopUpPaymentSucceededIntegrationEvent`. (Excepción: `Adjust` admin para correcciones auditadas.)

## 4. Tabla de contrato ↔ estado

| Endpoint | Método aggregate | Estado reserva resultante | LedgerEntry |
|---|---|---|---|
| reservations POST | `Reserve` | Held | Reserve |
| .../consume | `ConsumeReservation` | Held(parcial)/Consumed | Consume |
| .../refund | `Release`/`RefundRemainder` | Released | Refund |
| adjustments | `Adjust` | — | Adjust |
| (evento top-up) | `Recharge` | — | Recharge |

## 5. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Convención `[RateLimit]`/`[RateLimitExempt]` obligatoria | `RateLimit/Guia_Nuevos_Servicios_Endpoints.md`; `00_Overview:48` | VERIFIED | 90% |
| M2M audience/scopes propios por servicio | `00_Overview:48`, `02_Context_Map:38` | DOCUMENTED_ONLY | 85% |
| Legado exponía `debit-for-campaign`/`refund-for-campaign` sin idempotency key | `WalletServiceClient.cs:101,198` (sin header idem) | VERIFIED | 93% |
| Diseño reserve/consume/refund/adjust/get + recharge-by-event | diseño | NEW | n/a |
