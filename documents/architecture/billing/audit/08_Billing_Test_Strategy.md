# Billing — Estrategia de pruebas

Auditoría: arquitecto principal. Fecha: 2026-07-22.
Convención del repo (verificada): xUnit bajo `deploy/tests/TaxVision.<Svc>.Tests/`, `Microsoft.EntityFrameworkCore.InMemory` para tests de aplicación, fakes de repositorio in-memory (patrón `InMemoryCodeDefinitionRepository` en Growth.Tests). Los tests de dominio ejercen `Result<T>` y transiciones sin infraestructura.

## Matriz por capa

| Capa | Tipo | Alcance | Herramienta |
|---|---|---|---|
| Dominio | Unitaria | Fábricas, transiciones, invariantes, redondeo, numeración | xUnit puro |
| Dominio | Property-based | Aritmética de dinero, redondeo, suma de líneas = total, refund ≤ pagado | FsCheck/CsCheck |
| Aplicación | Unitaria | Handlers con fakes (repos, executor idempotencia, clientes M2M) | xUnit + fakes |
| Infraestructura | Integración SQL | EF config, owned types, índices únicos, RowVersion, filtros por tenant, secuencia concurrente | Testcontainers SQL Server / SQL local |
| Mensajería | Integración RabbitMQ | outbox/inbox durable, dedupe, retry, dead-letter | Testcontainers RabbitMQ |
| Contratos | Contract tests | Eventos publicados/consumidos (shape, versión) contra `BillingIntegrationEvents`/`PaymentClientIntegrationEvents` | snapshot/schema |
| API | Integración HTTP | rutas, ProblemDetails, idempotency-key, paginación, validación | `WebApplicationFactory` |
| Seguridad | Autorización | permisos, scopes, cross-tenant, `verify` anónimo | `WebApplicationFactory` |
| Resiliencia | Caos | fallo de Scribe/CloudStorage/PaymentClient/RabbitMQ en cada paso de la saga | fault injection |
| Migración | Datos | mapping legado, hashes, redondeos, rechazo de inconsistencias | scripts + asserts |
| E2E | Extremo a extremo | crear→emitir→enviar→pagar→recibo→verificar | stack docker |

## Pruebas obligatorias (con criterio de aprobación)

| ID | Prueba | Setup | Aprobación |
|---|---|---|---|
| T-01 | **Numeración concurrente** | N=200 `Issue` paralelos, mismo tenant/período | 0 duplicados; secuencia contigua; sin deadlock no manejado; latencia p99 acotada |
| T-02 | **Dos tenants, mismo número** | Tenant A y B emiten `INV-…-001` simultáneo | Ambos éxito; el único `(TenantId, InvoiceNumber)` no colisiona entre tenants |
| T-03 | **Pago duplicado** | Mismo `PaymentLinkId`/`ProviderEventId` dos veces | Segundo → no-op; 1 solo recibo; métrica `duplicate_payment++` |
| T-04 | **Mismo Idempotency-Key, payload distinto** | POST crear factura, misma key, body distinto | 409 `Billing.Idempotency.FingerprintMismatch`; no se crea segunda factura |
| T-05 | **Evento fuera de orden** | `payment_failed(terminal)` llega tras `payment_succeeded` ya aplicado | El failed NO revierte el pago (patrón Growth `InvalidTransition` swallow); estado permanece `Paid` |
| T-06 | **Reembolso acumulado excesivo** | refunds que suman > `AmountPaid` | Se rechaza el que excede (`RefundExceedsPrincipal`); `AmountRefunded ≤ AmountPaid` siempre |
| T-07 | **Acceso cross-tenant (lectura)** | Tenant B pide `GET invoice` de A por Id | 404 (ownership, no revela existencia) |
| T-08 | **Descarga de PDF de otro tenant** | Tenant B pide `GET invoices/{A}/pdf` | 404; nunca stream del `FileId` ajeno; test que el `FileId` se resuelve con `TenantId` |
| T-09 | **Manipulación del hash de recibo** | `verify` con hash alterado 1 char | `valid=false`, `tampered=true`; respuesta mínima; comparación en tiempo constante |
| T-10 | **Scribe cae después de crear el PaymentLink** | saga Send: link OK, render falla | Factura queda `Sent` con `DeliveryStatus=PdfPending`; link NO se pierde; reintento de render; no doble link |
| T-11 | **RabbitMQ cae después del commit** | commit local OK, publish al bus falla | outbox durable retiene el evento; al recuperar RabbitMQ se entrega exactamente una vez; sin evento fantasma si el commit falló |
| T-12 | **Replay del inbox** | mismo mensaje entregado 2 veces | Handler idempotente (dedupe por `EventId`/`PaymentLinkId`); efecto una sola vez |
| T-13 | **Pago menor / igual / mayor que el saldo** | 3 casos | `<` → PartiallyPaid; `==` → Paid; `>` → `Overpayment` rechazado |
| T-14 | **Reembolso total → estado** | refund == pagado | Estado `Refunded` (NO `Paid`); `AmountDue` no reabre (regresión de C-01/C-02) |
| T-15 | **Pago recibido después de anular** | `payment_succeeded` sobre `Voided` | Se registra el hecho; dispara refund automático; `payment_after_void` emitido; alerta (C-09) |
| T-16 | **Void de PartiallyPaid con dinero pagado** | `AmountPaid>0` + Void | Exige/dispara reembolso antes de `Voided`, o se rechaza (C-11) |
| T-17 | **Recálculo server-side de totales** | caller manda `Total` manipulado ≠ suma de componentes | Billing ignora el `Total` del caller y recalcula; si el caller no manda componentes → 422 (C-07) |
| T-18 | **Moneda no-2-decimales** | factura en JPY (0) y BHD (3) | Redondeo y representación correctos por exponente ISO-4217 (C-13) |
| T-19 | **Correlación imposible** | `PaymentLinkUsed` sin link conocido | no-op idempotente; métrica `uncorrelated_payment++`; no crash |
| T-20 | **Pago de otro tenant** | evento con `TenantId` ≠ dueño del link | Rechazado; nunca aplica a la factura; alerta de seguridad |
| T-21 | **Monto/moneda incorrectos en el evento** | `AmountCents`/`Currency` ≠ esperado del link | Rechazo/discrepancia registrada; no aplica pago silenciosamente |
| T-22 | **Migración: inconsistencia monetaria** | factura legada con `Subtotal+Tax-Discount ≠ Total` | Import **rechazado** (no silencioso); a cola de revisión (C-07/migración) |
| T-23 | **Verificación pública: enumeración** | barrido de tokens `verify` | rate-limited; respuesta mínima idéntica para no-existe vs inválido; sin oráculo de timing |

## Criterios de aprobación global

- Cobertura de dominio (transiciones + invariantes) 100% de ramas.
- Todas las pruebas P0 (T-01, T-03, T-04, T-05, T-07, T-08, T-11, T-12, T-14, T-15, T-17, T-20) verdes antes de integración.
- Contract tests contra el shape real de `PaymentLinkUsedIntegrationEvent` (hoy) y `payments.*` (cuando PC-ISSUE-01 exista).
- Sin test que dependa de `Date.now()` real (usar `TimeProvider` inyectado — convención del repo).
