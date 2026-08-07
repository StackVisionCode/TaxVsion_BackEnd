# Wallet/Ledger — Domain Design

- **Servicio:** `TaxVision.Wallet` (microservicio INDEPENDIENTE)
- **Fecha:** 2026-07-28
- **Estado:** DISEÑO — no implementado (greenfield)
- **Rol en la suite:** El corazón financiero. Saldo prepago **real en USD** por tenant, con **movimientos INMUTABLES** estilo libro mayor. Reutilizable por Campaigns, envíos SMS individuales y futuros consumidores. Ver `00_Overview_And_Index.md §Servicios`, `02_Context_Map.md`, `05_Master_ADR.md §Decisión 3`.

---

## 1. Principio rector

**Solo Wallet muta saldo, y solo por movimientos inmutables.** Ningún otro contexto (Campaigns, SMS, WhatsApp, Email) toca el saldo: piden movimientos vía API M2M y reciben un resultado. Esto corrige de raíz el anti-patrón legado del **débito TOCTOU no-atómico** (`CRMTAXPROBACKEND/CampaignService/Application/Handlers/CreateCampaignCommandHandler.cs:250-320`: `GetWalletBalanceAsync` → compara `AvailableBalance < estimatedCost` → `DebitForCampaignAsync` en **dos llamadas HTTP separadas**, con el debit antes de `SaveChangesAsync`) y el **saldo mutable suelto** (`ReferralService/Domain/WalletTransaction.cs:12-14`: `Amount/BalanceBefore/BalanceAfter` + `IsActive` editables).

## 2. Bounded context y lenguaje ubicuo (local)

| Término | Significado en Wallet | Nota |
|---|---|---|
| **TenantBalance** | Aggregate root: saldo prepago de UN tenant en UNA moneda (USD). | 1 fila por `(TenantId, Currency)`. |
| **LedgerEntry** | Movimiento INMUTABLE del libro mayor. Nunca se edita ni se borra. | Recharge/Reserve/Consume/Refund/Adjust. |
| **Reservation** | Fondos apartados (held) para una operación futura (un CampaignRun, un SMS). Tiene máquina de estados propia. | Ver `State_Machines.md`. |
| **Available** | `Posted − Held`. Lo que se puede reservar/consumir ahora. | Derivado, nunca negativo. |
| **Posted** | Saldo confirmado (recargas − consumos ± ajustes). | Suma de entries confirmados. |
| **Held** | Suma de reservas activas (Held). | No se puede gastar dos veces. |
| **Minor units** | `long AmountCents`, USD. Nunca `float`, nunca monto confiado por el frontend. | Reusa contrato `Money` (copia local). |

**Money** = copia-por-contexto del VO existente (`src/Services/PaymentApp/TaxVision.PaymentApp.Domain/ValueObjects/Money.cs:6-53`: `long AmountCents`, ISO-4217 3-letras, `Create` rechaza negativos). Wallet tiene **su propia copia** (no compartir tipos entre bounded contexts, igual que Growth Codes↔Referrals).

## 3. Aggregates

### 3.1 `TenantBalance` (aggregate root)

```
TenantBalance
├─ Id: Guid
├─ TenantId: Guid                 (uno por tenant+currency)
├─ Currency: string               ("USD", ISO-4217)
├─ PostedCents: long              (saldo confirmado; caché derivada con guarda)
├─ HeldCents: long                (suma de reservas activas)
├─ Status: BalanceStatus          (Active | Frozen)
├─ RowVersion: byte[]             (optimistic concurrency; ver Concurrency_Spec)
├─ CreatedAtUtc / UpdatedAtUtc
└─ (colecciones NO cargadas por default: LedgerEntry, Reservation son entidades del mismo context)
```

**AvailableCents** = `PostedCents − HeldCents` (propiedad calculada, invariante `>= 0`).

**Decisión de modelado (saldo cacheado con guardas, no puro event-sourcing):** `PostedCents`/`HeldCents` se mantienen en el aggregate como **caché derivada** de los `LedgerEntry`, actualizada en la MISMA transacción que inserta el entry, protegida por `RowVersion` + `CHECK` constraints. No es una suma recalculada en cada lectura (costoso) ni un saldo mutable suelto (anti-patrón legado). El ledger inmutable es la **fuente de verdad auditable**; el saldo cacheado es la vista consistente. Un job de reconciliación puede reverificar `PostedCents == Σ(entries confirmados)` (ver `Observability.md §Reconciliación`).

**Invariantes (garantizadas por métodos del aggregate que devuelven `Result`):**
- I1. `AvailableCents >= 0` siempre. Sin saldo negativo (nunca). Una reserva/consumo que lo violaría → `Result.Failure(Wallet.InsufficientFunds)`.
- I2. `HeldCents == Σ(reservas en estado Held).RemainingCents`.
- I3. `PostedCents == Σ(Recharge) − Σ(Consume) + Σ(Refund) ± Σ(Adjust)` (todos confirmados).
- I4. Toda mutación de saldo produce **exactamente un** `LedgerEntry` inmutable en la misma transacción.
- I5. Moneda única por balance; un movimiento en otra currency → `Result.Failure(Wallet.CurrencyMismatch)`.

### 3.2 `Reservation` (entidad del context, referida por scopeId opaco)

```
Reservation
├─ Id: Guid
├─ TenantId: Guid
├─ Currency: string
├─ AmountCents: long              (monto originalmente reservado)
├─ ConsumedCents: long            (consumo acumulado; <= AmountCents)
├─ RemainingCents: long           (= AmountCents − ConsumedCents; held vivo)
├─ Status: ReservationStatus      (Held | Consumed | Released | Expired)
├─ ConsumerContext: string        ("campaigns" | "sms" | ...)  — solo etiqueta/observabilidad
├─ ScopeId: Guid                  (id opaco del consumidor: CampaignRunId, SmsSendId...)
├─ IdempotencyKey: string         (clave de la operación reserve original)
├─ ExpiresAtUtc: DateTime?        (para expiración de holds abandonados)
├─ RowVersion: byte[]
└─ CreatedAtUtc / UpdatedAtUtc
```

`Reservation` NO tiene FK cross-context hacia Campaign/CampaignRun. Guarda `ScopeId` **opaco** (como el seam `CampaignId` que fluye Notification→Postmaster sin ser interpretado, `PostmasterEmailEvents.cs:37,104`). Wallet no sabe qué es un CampaignRun; solo reserva/consume/devuelve contra un scope.

### 3.3 `LedgerEntry` (value-object-like, INMUTABLE, append-only)

```
LedgerEntry
├─ Id: Guid
├─ TenantId: Guid
├─ Currency: string
├─ Kind: LedgerEntryKind          (Recharge | Reserve | Consume | Refund | Adjust)
├─ SignedAmountCents: long        (+recharge/+refund/+reserve-release; −consume; ± adjust)
├─ BalanceAfterPostedCents: long  (snapshot del Posted tras el entry — auditoría)
├─ BalanceAfterHeldCents: long    (snapshot del Held tras el entry — auditoría)
├─ ReservationId: Guid?           (para Reserve/Consume/Refund ligados a una reserva)
├─ Operation: string              (op de negocio: "reserve"|"consume"|"refund"|"recharge"|"adjust")
├─ ScopeId: Guid                  (scope del consumidor)
├─ IdempotencyKey: string         (clave de negocio que originó el movimiento)
├─ SourceReference: string?       (ej. SaaSPaymentId del top-up; adjustReason del admin)
├─ ActorType: string              (m2m-client | admin | system)
├─ ActorId: string?
└─ CreatedAtUtc: DateTime         (append-only; nunca UpdatedAt)
```

**Inmutabilidad forzada:** sin setters públicos; sin `Update`/`Delete` en el repo; a nivel BD, revocar UPDATE/DELETE (ver `Data_Model.md §Grants`). Una corrección NO edita un entry: inserta un `Adjust` o un `Refund` compensatorio. Esto contrasta con `WalletTransaction.IsActive` mutable del legado (`ReferralService/Domain/WalletTransaction.cs:21`).

## 4. Métodos del aggregate (mutaciones → `Result`)

Todos en `TenantBalance`, cada uno emite su `LedgerEntry` + evento de dominio, y respeta I1–I5:

| Método | Precondición | Efecto | Falla si |
|---|---|---|---|
| `Recharge(Money, source, key)` | Status=Active | `Posted += amount`; entry `Recharge (+)` | Frozen; currency mismatch |
| `Reserve(Money, scopeId, ctx, key, expiresAt?)` | `Available >= amount`, Active | crea `Reservation(Held)`; `Held += amount`; entry `Reserve (+held)` | `Available < amount` → InsufficientFunds |
| `ConsumeReservation(reservationId, Money, key)` | reserva Held/parcial, `consume <= Remaining` | `Posted -= consume`; `Held -= consume`; `Reservation.ConsumedCents += consume`; entry `Consume (−)` | consume > Remaining; reserva no Held |
| `ReleaseReservation(reservationId, key)` | reserva Held/parcial | `Held -= Remaining`; reserva → Released; entry `Refund (+held liberado, neto 0 en Posted)` | reserva ya terminal |
| `RefundReservationRemainder(reservationId, key)` | tras consumo parcial | libera `Remaining` no consumido (idéntico a Release del resto) | — |
| `Adjust(SignedMoney, reason, key, actor)` | admin/system | `Posted += signed` (con guarda I1); entry `Adjust (±)` | resultaría `Available < 0` |
| `Freeze()/Unfreeze()` | admin | bloquea/permite Recharge y Reserve | — |

**Reserve vs Consume separados** es la corrección clave: el legado cobraba **al crear** (prepay, débito único), sin poder ajustar por resultado real de entrega. Aquí: se reserva el costo estimado, se consume lo realmente entregado por destinatario, se devuelve el resto — atómico y auditable por movimiento.

## 5. Tabla de evidencia

| Afirmación | Evidencia (file:line) | Clasificación | Confianza |
|---|---|---|---|
| Wallet/Ledger no existe hoy en el backend nuevo | Glob `src/Services/Wallet*` → 0; `05_Master_ADR.md:14-15` | VERIFIED | 98% |
| VO `Money` (long cents, ISO) disponible para copiar | `PaymentApp.Domain/ValueObjects/Money.cs:6-53` | VERIFIED | 97% |
| Legado usa saldo mutable + decimal | `ReferralService/Domain/WalletTransaction.cs:12-21` | VERIFIED | 96% |
| Legado: débito TOCTOU en 2 HTTP calls, debit antes de SaveChanges | `CampaignService/.../CreateCampaignCommandHandler.cs:250,264,278,320` | VERIFIED | 95% |
| Legado: cobro **al crear** (prepay único), sin consume/refund por resultado | `CreateCampaignCommandHandler.cs:233-320` | VERIFIED | 94% |
| Legado: JWT persistido para refund | `CreateCampaignCommandHandler.cs:67` (`BackgroundAuthToken`); `WalletServiceClient.cs:179-180` | VERIFIED | 95% |
| Patrón scope opaco end-to-end (modelo para ScopeId) | `PostmasterEmailEvents.cs:37,104` | VERIFIED | 95% |
| Reserve→Consume/Refund como aggregate methods → Result | diseño (este doc) | NEW | n/a |

## 6. Blockers / dependencias

- **BLOCKER-WAL-1:** Wallet debe existir y estar desplegado **antes** de que Campaigns pueda ejecutar (dependencia dura, `05_Master_ADR.md:57`). MVP: entregar Wallet primero.
- **BLOCKER-WAL-2:** El top-up depende de un nuevo `SaaSPaymentType` en PaymentApp (ver `Commands_And_Events.md`, `Deployment.md`). Sin él, no hay recarga vía pago.
- **DEP:** `Money`, `IdempotencyKey`, `ProcessedBusinessMessage` se copian por contexto (no se comparten tipos).
