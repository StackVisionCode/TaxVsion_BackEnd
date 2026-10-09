# Wallet/Ledger — API Contracts

- **Servicio:** `TaxVision.Wallet`
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Coherente con:** `00_Plan_And_Architecture.md §8/§8-bis`, `Security.md`, `Idempotency_Spec.md`, `Concurrency_Spec.md`.

> **Modelo prepago (ADR-WAL-005).** La superficie ya **no** es "reserve/consume/refund M2M". El cobro de campaña **no tiene endpoint público**: ocurre por el consumer del evento del PEP (`CampaignRunPendingAuthorization`). Los endpoints son de **tenant** (saldo, recargar, ledger, pricing, estimar) + uno de **PlatformAdmin** (editar precios).

---

## 1. Superficie

Endpoints de **tenant** vía el gateway (`/wallet/...`), con `[AllowActorTypes(TenantEmployee, TenantAdmin, PlatformAdmin)]`, `[RateLimit]`, `TenantId` del JWT (nunca del body), RBAC `wallet.view`/`wallet.manage` (`Security.md`). Dinero SIEMPRE en `amountCents:long` + `currency` ISO-4217. El **cobro** de campaña NO es un endpoint — lo dispara el PEP internamente (ADR-WAL-007).

Contrato de error uniforme: `Result` → `{ "error": { "code": "Wallet.InsufficientFunds", "message": "..." } }` con HTTP mapeado (`402`/`409`/`422`/`403`). Éxito → `2xx`.

## 2. Endpoints

### 2.1 `GET /wallet` — saldo

- **Permiso:** `wallet.view`
- **200:** `{ "tenantId":"guid", "currency":"USD", "balanceCents":54000, "updatedAtUtc":"..." }`
- Fail-closed: `TenantId` del JWT; sin cross-tenant.

### 2.2 `GET /wallet/ledger?page&size` — ledger (auditoría, paginado)

- **Permiso:** `wallet.view`
- **200:** lista inmutable de asientos con `direction`, `amountCents`, `balanceAfterCents`, `reason` (`TopUp`/`CampaignCharge`/`Adjustment`), `operationKey`, `referenceId`, `createdAtUtc`.

### 2.3 `POST /wallet/top-up` — iniciar recarga (money-IN)

- **Permiso:** `wallet.manage`
- **Request:** `{ "amountCents": 10000 }`  (currency = default del wallet)
- **Efecto:** crea `WalletTopUp(Pending)` + publica `WalletTopUpDueIntegrationEvent`; PaymentApp cobra Stripe off-session contra la tarjeta guardada; al éxito Wallet acredita (ADR-WAL-010, `Commands_And_Events.md §Top-up`).
- **202:** `{ "walletTopUpId":"guid", "status":"Pending", "amountCents":10000 }`
- El front refresca el saldo al recibir `WalletTopUpPaymentSucceeded` (evento/poll). `...Failed` → el top-up queda `Failed` (no acredita).
- **Requiere tarjeta guardada** (setup-intent ya existe en PaymentApp: `CreateSetupIntentCommand/Handler`, `TenantProviderCustomersController`).

### 2.4 `GET /wallet/pricing` — catálogo de tarifas vigente

- **Permiso:** `wallet.view`
- **200:** `[ { "channel":"Email", "unitPriceCents":1, "currency":"USD", "active":true }, ... ]`

### 2.5 `PUT /wallet/pricing/{channel}` — editar tarifa (solo PlatformAdmin)

- **Autorización:** `[AllowActorTypes(PlatformAdmin)]` (no es un permiso de tenant)
- **Request:** `{ "unitPriceCents": 5, "active": true }`
- **200:** la tarifa actualizada.

### 2.6 `POST /wallet/estimate` — estimar costo vs saldo (SIN efectos)

Alimenta la pre-visualización del front antes de enviar.

- **Permiso:** `wallet.view`
- **Request:** `{ "perChannelUnits": { "Email": 1200, "Sms": 300, "Push": 0, "WhatsApp": 50 } }`
- **200:**
```json
{
  "costCents": 4700,
  "balanceCents": 3000,
  "sufficient": false,
  "deficitCents": 1700,
  "currency": "USD",
  "perChannel": [
    { "channel":"Email", "units":1200, "unitPriceCents":1, "subtotalCents":1200 },
    { "channel":"Sms",   "units":300,  "unitPriceCents":5, "subtotalCents":1500 },
    { "channel":"WhatsApp","units":50, "unitPriceCents":5, "subtotalCents":250 }
  ]
}
```
- **Sin efectos** — solo calcula. No debita.

### 2.7 (Campaigns) `POST /campaigns/{id}/preview-audience` — conteos por canal (money-agnostic)

Vive en **Campaigns**, no en Wallet, pero el front lo encadena con `/wallet/estimate`.

- **Request:** `{ "sources": [...] }`  (mismas fuentes de audiencia que usaría el run)
- **200:** `{ "perChannelUnits": { "Email":1200, "Sms":300, ... }, "totalRecipients": 1500 }`
- Reusa el resolver de audiencia (depura opt-outs/duplicados). No cobra, no conoce precios.

## 3. Cobro de campaña — NO es un endpoint

El `Debit` de un run **no se expone**. Lo dispara el consumer de `CampaignRunPendingAuthorizationIntegrationEvent` (PEP, ADR-WAL-007). No hay endpoint de débito público: evita cobros arbitrarios y mantiene a Campaigns money-agnostic. La **recarga** (`Credit`) tampoco se acredita por POST arbitrario — solo por el evento `WalletTopUpPaymentSucceeded` tras cobro real (ADR-WAL-010). Única otra vía de mutación: `Adjustment` admin (auditado).

## 4. Tabla de contrato ↔ efecto

| Endpoint / Evento | Efecto en saldo | LedgerEntry |
|---|---|---|
| `GET /wallet` / `GET /wallet/ledger` | lectura | — |
| `POST /wallet/top-up` → (evento succeeded) | `Credit` | TopUp |
| `POST /wallet/estimate` | ninguno (cálculo) | — |
| `GET`/`PUT /wallet/pricing` | ninguno (catálogo) | — |
| (evento `CampaignRunPendingAuthorization`) | `Debit` o `Denied` | CampaignCharge (si autoriza) |
| (admin) `Adjustment` | `Credit`/`Debit` | Adjustment |

## 5. Tabla de evidencia

| Afirmación | Evidencia | Clasificación | Confianza |
|---|---|---|---|
| Convención `[RateLimit]`/`[AllowActorTypes]`/RBAC obligatoria | `RateLimit/Guia_Nuevos_Servicios_Endpoints.md`; `CampaignsPermissions.cs:12-21` | VERIFIED | 90% |
| TenantId del JWT, nunca del body | regla de suite; `00_Plan §8` | DOCUMENTED_ONLY | 88% |
| Setup-intent / card-on-file ya existe en PaymentApp | `CreateSetupIntent*`; `TenantProviderCustomersController.cs` | VERIFIED | 90% |
| `preview-audience` reusa resolver de audiencia (unidad=destinatario×canal) | `StartCampaignRunCommand.cs:177-192` | VERIFIED | 90% |
| Endpoints saldo/top-up/pricing/estimate + cobro por evento | `00_Plan §8` (diseño) | NEW | n/a |
