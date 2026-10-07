# Wallet/Ledger — Domain Design

- **Servicio:** `TaxVision.Wallet` (microservicio INDEPENDIENTE) — greenfield
- **Fecha:** 2026-10-06
- **Estado:** DISEÑO — no implementado
- **Rol en la suite:** el corazón financiero del cobro de campañas. Saldo prepago **real en USD** por tenant, con **movimientos INMUTABLES** estilo libro mayor. Cobra el envío mediante un **autorizador externo (PEP)** dejando a Campaigns money-agnostic. Reutilizable por SMS individuales y futuros consumidores.
- **Coherente con:** `00_Plan_And_Architecture.md` (autoritativo), `ADR.md` (ADR-WAL-001/002/003/005/006/007), `../05_Master_ADR.md §Decisión 3` + ADR-CAMP-001 D7, `../campaigns/Domain_Design.md` (§ scope "Campaign no cambia").

> **Modelo vigente (2026-10-06): PREPAGO sin reembolso (ADR-WAL-005), que SUPERSEDE el reserve→consume→refund.** No hay holds, reservas, `HeldCents`, consume, refund ni settle. El cobro de un run es **un solo débito** calculado por tarifa destinatario×canal, aplicado **antes del fan-out** por un autorizador externo (PEP). Campaign no cambia y no conoce al Wallet.

---

## 1. Principio rector

**Solo Wallet muta saldo, y solo por movimientos inmutables.** Ningún otro contexto (Campaigns, SMS, WhatsApp, Email) toca el saldo. Campaigns **ni siquiera conoce al Wallet**: solo publica conteos de unidades por canal y espera `Authorized`/`Denied` (ADR-WAL-007). Esto corrige de raíz dos anti-patrones legados:
- el **débito TOCTOU no-atómico** (`CRMTAXPROBACKEND/CampaignService/Application/Handlers/CreateCampaignCommandHandler.cs:250-320`: `GetWalletBalanceAsync` → compara → `DebitForCampaignAsync` en **dos llamadas HTTP separadas**, con el debit antes de `SaveChangesAsync`);
- el **saldo mutable suelto** (`ReferralService/Domain/WalletTransaction.cs:12-14`: `Amount/BalanceBefore/BalanceAfter` + `IsActive` editables).

> El legado **sí** cobraba al crear (prepago de débito único), lo cual es ahora el modelo elegido; lo que corregimos no es el "cuándo" sino el **cómo**: un débito atómico, idempotente, sobre un ledger inmutable, calculado por catálogo de precios, y desacoplado de Campaigns vía PEP.

## 2. Bounded context y lenguaje ubicuo (local)

| Término | Significado en Wallet | Nota |
|---|---|---|
| **Wallet** | Aggregate root: saldo prepago de UN tenant en UNA moneda (USD). | 1 fila por tenant (USD). |
| **LedgerEntry** | Movimiento INMUTABLE del libro mayor. Nunca se edita ni se borra. | `TopUp` / `CampaignCharge` / `Adjustment`. |
| **PostedCents** | Saldo disponible confirmado (recargas − cargos ± ajustes). | Suma de asientos; caché derivada, `>= 0`. |
| **ChannelPrice** | Tarifa por canal (`(destinatario, canal)`), catálogo de plataforma. | Editable por PlatformAdmin. Ver §5. |
| **Unidad** | `(destinatario, canal)`. | Base del cálculo de costo. |
| **OperationKey** | Clave de negocio del movimiento (`run:{RunId}`, `topup:{Id}`). | Candado de idempotencia (unique). |
| **PEP** | Policy Enforcement Point: autorizador externo que debita entre Start y fan-out. | Event-driven. Ver §6. |
| **Minor units** | `long` cents, USD. Nunca `float`, nunca monto confiado por el frontend. | Reusa contrato `Money` (copia local). |

**Money** = copia-por-contexto del VO existente (`src/Services/PaymentApp/TaxVision.PaymentApp.Domain/ValueObjects/Money.cs:6-53`: `long AmountCents`, ISO-4217, `Create` rechaza negativos). Wallet tiene **su propia copia** (no compartir tipos entre bounded contexts).

## 3. Aggregates

### 3.1 `Wallet` (aggregate root)

```
Wallet
├─ TenantId: Guid                 (identidad; uno por tenant)
├─ Currency: string               ("USD", ISO-4217)
├─ PostedCents: long              (saldo disponible; caché derivada con guarda, >= 0)
├─ RowVersion: byte[]             (optimistic concurrency; ver Concurrency_Spec)
├─ CreatedAtUtc / UpdatedAtUtc
└─ (LedgerEntry: entidades hijas, NO cargadas por default)
```

**Decisión de modelado (saldo cacheado con guardas, no puro event-sourcing):** `PostedCents` se mantiene en el aggregate como **caché derivada** de los `LedgerEntry`, actualizada en la MISMA transacción que inserta el asiento, protegida por `RowVersion` + `CHECK (PostedCents >= 0)`. El ledger inmutable es la **fuente de verdad auditable**; `PostedCents` es la vista consistente. Un job de reconciliación reverifica `PostedCents == Σ(asientos)` (ver `Observability.md §Reconciliación`). **Ya no hay `HeldCents`** (sin reservas, ADR-WAL-003 modificado).

**Métodos (mutaciones → `Result`):**

| Método | Precondición | Efecto | Falla si |
|---|---|---|---|
| `Credit(amountCents, opKey, reason)` | `amount > 0` | `PostedCents += amount`; emite `LedgerEntry(Credit, reason)` | currency mismatch |
| `Debit(amountCents, opKey, reason)` | `PostedCents >= amount` | `PostedCents -= amount`; emite `LedgerEntry(Debit, reason)` | `PostedCents < amount` → `InsufficientFunds` |

Ambos son **idempotentes por `opKey`** (índice único `(TenantId, OperationKey)` en el ledger): reintentar la misma operación no duplica el asiento (ver `Idempotency_Spec.md`).

**Invariantes (garantizadas por los métodos):**
- I1. `PostedCents >= 0` siempre. Un débito que lo violaría → `Result.Failure(Wallet.InsufficientFunds)` (gate del run).
- I2. `PostedCents == Σ(Credit) − Σ(Debit)` (todos los asientos).
- I3. Toda mutación produce **exactamente un** `LedgerEntry` inmutable en la misma transacción.
- I4. Moneda única por wallet; un movimiento en otra currency → `Result.Failure(Wallet.CurrencyMismatch)`.

### 3.2 `LedgerEntry` (INMUTABLE, append-only)

```
LedgerEntry
├─ Id: Guid
├─ TenantId: Guid
├─ Direction: LedgerDirection     (Credit | Debit)
├─ AmountCents: long              (siempre positivo; el signo lo da Direction)
├─ BalanceAfterCents: long        (snapshot de PostedCents tras el asiento — auditoría)
├─ Reason: LedgerReason           (TopUp | CampaignCharge | Adjustment)
├─ OperationKey: string           (clave de negocio; UNIQUE con TenantId — idempotencia)
├─ ReferenceId: Guid              (topUpId para TopUp; runId para CampaignCharge)
├─ ActorType: string              (system | admin)   -- opcional, auditoría
├─ ActorId: string?
└─ CreatedAtUtc: DateTime         (append-only; nunca UpdatedAt)
```

**Inmutabilidad forzada:** sin setters públicos; sin `Update`/`Delete` en el repo; a nivel BD, revocar UPDATE/DELETE (ver `Data_Model.md §Grants`). Una corrección NO edita un asiento: inserta un `Adjustment` compensatorio. Esto contrasta con `WalletTransaction.IsActive` mutable del legado (`ReferralService/Domain/WalletTransaction.cs:21`).

### 3.3 `ChannelPrice` (catálogo — ver §5)

## 4. Cobro de un run (modelo prepago, ADR-WAL-005)

El costo de un run se **debita una sola vez, antes del fan-out**, calculado sobre la audiencia ya depurada (sin opt-outs ni duplicados):

```
costo_run = Σ_canal ( unidades_del_canal × ChannelPrice[canal].UnitPriceCents )
          donde unidad = (destinatario, canal)
```

El Wallet aplica `Debit(costo_run, opKey="run:"+RunId, reason=CampaignCharge, referenceId=RunId)`. Si `PostedCents < costo_run` → `InsufficientFunds` y el run no despacha. **No hay reembolso** por entregas fallidas/omitidas: el `CampaignRunCompletedIntegrationEvent` (contadores Delivered/Failed/Skipped) se ignora para dinero.

> **Prepago de débito único** reemplaza el reserve→consume→refund. Simpler: sin saga de compensación, sin sweep de holds, sin `HeldCents`, sin estados de reserva. El run paga por el **envío autorizado**, no por el resultado de entrega del proveedor.

## 5. Catálogo de precios (`ChannelPrice`)

Aggregate de plataforma (no por tenant salvo override futuro), editable **solo por PlatformAdmin**:

```
ChannelPrice
├─ Channel: ChannelKind           (Email | Sms | Push | WhatsApp)
├─ UnitPriceCents: long           (precio por (destinatario, canal))
├─ Currency: string               ("USD")
├─ Active: bool
└─ UpdatedAtUtc
```

Semilla inicial (placeholder, ajustable por el usuario): Email 1¢, Push 1¢, SMS 5¢, WhatsApp 5¢. **El diseño no hardcodea los números.** Expuesto por `GET /wallet/pricing` (tenant, lectura) y `PUT /wallet/pricing/{channel}` (PlatformAdmin). Alimenta el cálculo de costo del PEP (§6) y la estimación del front (`POST /wallet/estimate`).

## 6. Autorizador externo / PEP (visión Wallet)

Wallet actúa como **autorizador**: consume el evento que Campaigns publica entre `CampaignRun.Start` y el fan-out, debita y responde. Campaigns no conoce dinero (solo cuenta unidades).

```
Campaigns ── CampaignRunPendingAuthorizationIntegrationEvent { TenantId, CampaignId, RunId,
                PerChannelUnits:{Email,Sms,Push,WhatsApp}, TriggeredBy } ──► Wallet (PEP)
   Wallet: costo = Σ canal (PerChannelUnits[canal] × ChannelPrice[canal])
           Debit(costo, opKey="run:"+RunId, reason=CampaignCharge)
   ├─ suficiente  ──► CampaignRunAuthorizedIntegrationEvent { RunId }          ──► Campaigns ejecuta fan-out
   └─ insuficiente──► CampaignRunAuthorizationDeniedIntegrationEvent { RunId,
                        Reason="InsufficientFunds" } (no debita)                ──► Campaigns Reject(run)
```

Idempotencia: `opKey="run:"+RunId` ⇒ reentrega de la autorización no duplica el débito; re-autorizar un run ya autorizado devuelve `Authorized` sin re-debitar. Scheduler: cada fire programado/recurrente es un run nuevo ⇒ su propio `PendingAuthorization` ⇒ su propio débito. Ver `State_Machines.md`, `Commands_And_Events.md`, `Transactional_Protocol.md`.

## 7. Recarga (top-up, money-IN)

La recarga clona el flujo plan-change de PaymentApp (ADR-WAL-010): `POST /wallet/top-up { amountCents }` crea un `WalletTopUp` (Pending) + publica `WalletTopUpDueIntegrationEvent`; PaymentApp cobra Stripe off-session y emite `WalletTopUpPaymentSucceeded/Failed`; Wallet hace `Credit(amount, opKey="topup:"+topUpId, reason=TopUp)` al éxito. Ver `Commands_And_Events.md §Top-up`.

## 8. Tabla de evidencia

| Afirmación | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Wallet/Ledger no existe hoy en el backend nuevo | Glob `src/Services/Wallet*` → 0; `05_Master_ADR.md:14-15` | VERIFIED | 98% |
| VO `Money` (long cents, ISO) disponible para copiar | `PaymentApp.Domain/ValueObjects/Money.cs:6-53` | VERIFIED | 97% |
| Legado usa saldo mutable + decimal | `ReferralService/Domain/WalletTransaction.cs:12-21` | VERIFIED | 96% |
| Legado: débito TOCTOU en 2 HTTP calls, debit antes de SaveChanges | `CampaignService/.../CreateCampaignCommandHandler.cs:250,264,278,320` | VERIFIED | 95% |
| Legado: cobro **al crear** (prepago único) — ahora el modelo elegido | `CreateCampaignCommandHandler.cs:233-320` | VERIFIED | 94% |
| Unidad = (destinatario, canal) | `StartCampaignRunCommand.cs:177-192` (`ExpandUnits`) | VERIFIED | 90% |
| Punto de inserción del gate (Start→fan-out) | `StartCampaignRunCommand.cs:83`; `106-133`; `139` | VERIFIED | 95% |
| `CampaignRun.Rejected` + `RejectionReason` ya existen | `RunEnums.cs:8-17`; `CampaignRun.cs:36` | VERIFIED | 99% |
| Modelo prepago single-debit (Credit/Debit, sin holds) | `00_Plan §2/§3` (diseño) | NEW | n/a |

## 9. Blockers / dependencias

- **BLOCKER-WAL-1:** Wallet debe existir y estar desplegado **antes** de que Campaigns pueda cobrar (`05_Master_ADR.md:57`).
- **BLOCKER-WAL-2:** el top-up depende de un nuevo `SaaSPaymentType.WalletTopUp` + eventos `WalletTopUpDue/Succeeded/Failed` en PaymentApp (ver `Commands_And_Events.md`, `Deployment.md`).
- **DEP:** `Money` se copia por contexto (no se comparten tipos).
