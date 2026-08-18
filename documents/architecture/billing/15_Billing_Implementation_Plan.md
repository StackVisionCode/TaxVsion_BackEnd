# Billing — Plan de implementación

Fecha: 2026-07-22

Fases B1–B6. La fase actual (scaffolding) es **B1**. Cada fase compila y pasa `dotnet build TaxVision.slnx`.

## B1 — Scaffolding compilable (fase actual)

- Crear `src/Services/Billing/TaxVision.Billing.{Domain,Application,Infrastructure,Api}` (plantilla obligatoria de 4 proyectos, README §23).
- Estructura de folders espejo de Growth: Domain (`Invoices/`, `Receipts/`, `Settings/`, `Numbering/`, `ValueObjects/`, `Events/`, `Common/`), Application (slices + `Abstractions/` + `Common/`), Infrastructure (`Persistence/{BillingDbContext,BillingDbContextFactory,BillingSchemas,Configurations,Migrations,Repositories}`, `Idempotency/`, `Clients/` (PaymentClient/Scribe/CloudStorage), `Observability/`, `DependencyInjection.cs`), Api (`Controllers/`, `IntegrationEvents/`, `Authorization/`, `RateLimiting/`, `Common/`, `Program.cs`).
- Base classes desde BuildingBlocks; `Money`/enums/VOs stub compilando.
- Registrar en `TaxVision.slnx`; bloque `billing-api` en compose; ruta `/billing/*` + cluster en gateway + `depends_on`; `BILLING_DB_CONNECTION` en `.env`; línea en `apply-migrations.sh`; `BillingIntegrationEvents/` con los contratos; `BillingServiceScopes.cs`; `TaxVision.Billing.Tests` en `deploy/tests/`.
- Sin lógica de negocio: fábricas y handlers pueden devolver `Result.Failure("Billing.NotImplemented")`.
- **Salida**: solución compila; servicio arranca `/health/live`.

## B2 — Dominio Invoice + numeración + persistencia

- Implementar `Invoice`, `InvoiceLineItem`, `Discount`, snapshots, `InvoiceNumberSequence` con la máquina de estados (`08_…`) y `Result`.
- `BillingDbContext` + configuraciones EF (owned types, índices, `RowVersion`) + migración `InitialBilling`.
- Idempotencia `SqlBillingIdempotencyExecutor` (patrón Growth).
- UC-01/02/03/10/11/12/13 (borrador, emitir, listar, ver). Sin PDF ni pago aún.
- Tests de dominio (transiciones, invariantes, numeración concurrente).

## B3 — PaymentReceipt + pago manual

- `PaymentReceipt` aggregate + hash + verificación pública.
- UC-05 (mark-as-paid manual), UC-16/17/18/19/20.
- Emitir `billing.invoice.paid`, `billing.receipt.issued` al outbox.
- Tests de recibo (hash, void, refund).

## B4 — Integración PaymentClient (online) + eventos

- Cliente M2M a PaymentClient (crear/cancelar PaymentLink con `PurposeKind=InvoicePayment`). Token de servicio (patrón `GrowthServiceTokenAcquirer`).
- `InvoicePaymentLinks` + consumers de `payments.payment_succeeded/failed/refunded/cancelled` (inbox durable, idempotencia por `PaymentReference`, correlación BDR-001).
- UC-04 (send con link), UC-06/07/08 (eventos), UC-09 (void + cancel).
- Registrar cliente M2M en compose (`ServiceAuth__Clients__N` para `billing-*` y/o scopes de PaymentClient para Billing).

## B5 — PDF (Scribe + CloudStorage) + notificación

- Template de invoice/recibo en Scribe (portar layouts + watermark "Paid").
- Cliente M2M a Scribe (render) y CloudStorage (guardar `FileId`).
- UC-14/19 (descargar PDF); poblar `PdfFileId`/`PaidPdfFileId`.
- Verificar que Notification consuma `billing.invoice.sent`/`billing.receipt.issued` para el email al cliente.

## B6 — Config, observabilidad, hardening

- `TenantBillingSettings` (UC-21), `BillingMetrics`, rate limiting en endpoints públicos, auditoría (`audit.AuditEntries`).
- Overdue derivado en queries; paginación/filtizado completo.
- Suite de tests de integración; readiness checklist (audit doc).

## Dependencias entre fases

```
B1 (scaffold) → B2 (dominio) → B3 (recibo/manual) → B4 (PaymentClient) → B5 (PDF/notif) → B6 (hardening)
                                   └───────────────── B4 y B5 pueden solaparse tras B3 ─────────┘
```

## Checklist README §23 mapeado

BD propia (B1/B2) · migración inicial (B2) · `IUnitOfWork` (B1) · filtro TenantId fail-closed (B2) · correlación HTTP+eventos (B1/B4) · outbox (B3) · inbox+idempotencia (B4) · health live/ready (B1) · JWT+authz (B1) · OTLP (B1) · Dockerfile pinneado (B1) · red `taxvision-network` (B1) · ruta YARP (B1) · tests (B2+).
