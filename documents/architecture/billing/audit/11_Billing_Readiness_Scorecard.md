# Billing — Readiness Scorecard

Auditoría: arquitecto principal. Fecha: 2026-07-22.
Regla: **no se declara READY si quedan blockers de esa etapa sin resolución.** Veredicto separado por etapa (no un estado global).

## Veredicto por etapa

| Etapa | Puntuación | Veredicto | Blockers abiertos |
|---|---|---|---|
| **Scaffolding** | **92 / 100** | ✅ **READY** | Ninguno |
| **Implementación** | **58 / 100** | ⚠️ **NOT READY** | P0-1..P0-5, P0-7, P1-9, P1-10, P1-16 |
| **Integración** | **34 / 100** | ⛔ **NOT READY** | P0-6, P0-7, P1-1..P1-7, P1-11, BILL-004/005/013/014 |
| **Producción** | **18 / 100** | ⛔ **NOT READY** | Todos los P0 + P1 + seguridad (BILL-007/016/017/019) |

## 1. Scaffolding — 92/100 — READY

**Evidencia:** los 4 proyectos compilan (`dotnet build TaxVision.slnx` = 0 errores), 3/3 smoke tests pasan, registrado en `TaxVision.slnx`, ruta `/billing/*` + cluster en gateway, bloque `billing-api` en compose, `BILLING_DB_CONNECTION` en `.env`, línea en `apply-migrations.sh`, `BillingDbContext` design-time-constructible, Wolverine outbox/inbox cableado idéntico a Growth.

**Descuento (-8):** sin migración EF (esperado, B2); `[Authorize]` plano sin policy provider; repos stub; `BillingMetrics` mínimo.

**Condición de salida (cumplida):** compila, arranca `/health/live`, estructura de folders correcta, contratos de evento definidos.

## 2. Implementación — 58/100 — NOT READY

**Bloqueadores (deben cerrarse antes de codificar el dominio):**
- P0-1 estado de refund (`Refunded`/`AmountRefunded`) — sin esto el modelo financiero es incorrecto.
- P0-2 pago-tras-void, P0-3 sobrepago — comportamientos indefinidos.
- P0-4 recálculo server-side de totales — decisión de producto exigida.
- P0-7 motor HTML→PDF — no existe.
- P1-9 `MinorUnits` por moneda, P1-10 Alternativa A (receipt fuera de RecordPayment).
- P1-16 migración EF inicial.

**Listo:** estructura DDD, convenciones, idempotency executor (patrón), máquina de estados (con las correcciones de `04`).

**Condición de salida:** las 14 contradicciones de `02` resueltas en los docs; dominio con invariantes corregidas; migración inicial aplicable; tests de dominio (T-01,T-03,T-04,T-05,T-13,T-14) verdes.

## 3. Integración — 34/100 — NOT READY

**Bloqueadores (dependen de OTROS servicios — no solo de Billing):**
- **BILL-004 / P0-6:** PaymentClient **no publica** `payments.*`. Sin PC-ISSUE-01, Billing no puede reaccionar a pagos/fallos/refunds. (P0)
- **BILL-005 / P0-7:** Scribe **no produce PDF**; no hay motor HTML→PDF en el repo. (P0)
- **BILL-013 / P1-3:** PaymentClient **sin superficie M2M**; auth Billing→PaymentClient indefinida.
- **BILL-014 / P1-5:** Customer **sin get-by-id M2M ni tax-id/dirección** → `CustomerSnapshot` incompleto.
- **BILL-015 / P1-2:** CloudStorage upload event-driven + IAM MinIO propia.
- **BILL-025 / P1-4:** sin cliente M2M outbound de Billing en Auth.

**Condición de salida:** PaymentClient publica `payments.*` (o correlación por `PaymentLinkId` verificada E2E); `IInvoiceDocumentService` funcional (render+store); Customer expone snapshot completo; clientes M2M registrados y autorizados; tests de contrato + integración (RabbitMQ/SQL) verdes.

## 4. Producción — 18/100 — NOT READY

**Bloqueadores adicionales a los anteriores:**
- Seguridad: BILL-007 (correlación sin validar tenant/monto), BILL-016 (PII en eventos), BILL-017 (`verify` enumerable), BILL-019 (authz), SEC-02 (descarga cross-tenant), query filter global.
- Resiliencia: saga con compensación (BILL-012), job de reconciliación, DLQ/poison/replay, timeouts/retries/circuit-breaker.
- Datos: migración legada con rechazo de inconsistencias (BILL-022); numeración `UPDLOCK/HOLDLOCK` (BILL-018).
- Observabilidad: métricas completas + dashboards + alertas.
- Cobertura de tests (matriz `08`) incl. todos los P0.
- `PaymentLinkUsed` "used" vs "settled" (BILL-023) resuelto (PC-ISSUE-01).

**Condición de salida:** todos los P0 y P1 cerrados; suite de tests crítica verde; seguridad multi-tenant verificada (sin acceso cross-tenant); reconciliación operativa; migración validada (shadow + reconciliación); runbook + alertas.

## Resumen ejecutivo del veredicto

- **Scaffolding: LISTO.** El esqueleto es sólido y sigue las convenciones de la casa.
- **Implementación: NO listo.** Hay que cerrar decisiones de dominio financiero (refund/void/overpayment/tax) y añadir el motor PDF antes de codificar.
- **Integración: NO listo.** Dos premisas centrales del diseño son falsas contra el código (PaymentClient no publica `payments.*`; Scribe no hace PDF), y faltan superficies M2M en PaymentClient/Customer.
- **Producción: NO listo.** Además de lo anterior, faltan seguridad multi-tenant endurecida, resiliencia (saga/reconciliación) y observabilidad.

> **El estado `READY_FOR_SCAFFOLDING` del doc `01_Billing_Executive_Summary.md` es correcto SOLO para scaffolding.** No debe leerse como readiness de implementación/integración/producción. Corregir el doc para reflejar el veredicto por etapa.
