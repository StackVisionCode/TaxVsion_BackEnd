# Billing — Auditoría por capas

Auditoría: arquitecto principal (.NET/DDD/distribuido/SQL Server/Wolverine/RabbitMQ/multi-tenant/financiero). Fecha: 2026-07-22.
Método: los 16 docs de diseño confrontados con el código real (`src/Services/Billing` scaffold + `Growth`/`PaymentClient`/`PaymentApp`/`Scribe`/`CloudStorage`/`Notification`/`Customer`/`Auth`/`BuildingBlocks`). Toda afirmación sin evidencia se marca `NO_CONFIRMADO`.

## Resultado por capa (resumen)

| Capa | Fortalezas | Problemas críticos | Veredicto |
|---|---|---|---|
| Dominio | Aggregates/VOs bien delimitados; snapshots; numeración server-side; máquina de estados explícita | Refund→`Paid` (BILL-002), pago-tras-void (BILL-001), sobrepago (BILL-003), receipt dentro de RecordPayment (BILL-008), Money 2-dec (BILL-010) | NOT_READY_FOR_IMPLEMENTATION |
| Aplicación | Slices verticales; handlers estáticos; idempotency executor (patrón Growth) | `SendInvoice` = saga sin compensación (BILL-012); clave de idempotencia ambigua (BILL-009); totales del caller (BILL-006) | NOT_READY_FOR_IMPLEMENTATION |
| Infraestructura | Wolverine outbox/inbox ya cableado; schemas; DbContext design-time OK | Sin migración EF (BILL-029); repos stub; clientes M2M inexistentes (BILL-025) | PARCIAL (scaffold OK) |
| Persistencia/Concurrencia | RowVersion, índices únicos, owned types | Numeración RowVersion+retry subóptima (BILL-018); `AmountRefunded` faltante | READY_FOR_SCAFFOLDING |
| Dinero/Impuestos | `Money(long)` en el dominio | Sin recálculo server-side (BILL-006); 2-dec fijo (BILL-010) | NOT_READY_FOR_IMPLEMENTATION |
| Integración Payment | Gancho `PurposeKind.InvoicePayment` real | PaymentClient no publica `payments.*` (BILL-004); sin M2M (BILL-013); sin refund event | NOT_READY_FOR_INTEGRATION |
| Integración Docs | CloudStorage `FolderType.Invoices` existe | Scribe no hace PDF (BILL-005); `FileType.Invoice` inexistente (BILL-026); upload event-driven (BILL-015) | NOT_READY_FOR_INTEGRATION |
| Integración Notification | Consume eventos; `NotificationCategory.Billing` existe | Requiere email en el evento (BILL-016); attachments no soportados (BILL-024) | PARCIAL |
| Integración Customer | Evento `CustomerCreated` con nombre/email | Sin get-by-id M2M; sin tax-id/dirección (BILL-014) | NOT_READY_FOR_INTEGRATION |
| API | REST razonable; `verify` anónimo aislado | `[Authorize]` plano (BILL-019); `mark-as-paid` (BILL-021); falta ETag/versionado | NOT_READY_FOR_IMPLEMENTATION |
| Seguridad | Tenant desde JWT; ownership-404 | Correlación sin validar tenant/monto (BILL-007); `verify` enumerable (BILL-017); PII en eventos (BILL-016) | NOT_READY_FOR_PRODUCTION |
| Observabilidad | `BillingMetrics` stub; health live/ready | Métricas insuficientes (BILL-028); sin dashboards/alertas | PARCIAL |

## 1. Dominio

**Fortalezas:** bounded context único `Invoices` correcto (no se justifica multi-contexto); `Invoice`/`PaymentReceipt` como aggregate roots; snapshots congelados (buena decisión heredada del legado, limpiada); numeración server-side (corrige el defecto legado client-supplied); VOs (`Money`, `InvoiceNumber`, `VerificationHash`) bien tipados; máquina de estados explícita (reemplaza el string libre legado).

**Problemas:** ver `04_Domain_Recommendations` (análisis completo de RecordPayment/PaymentReceipt, escenarios de pago/refund, invariantes corregidas). Resumen: BILL-001/002/003/008/010/011. La consistencia entre dominio↔casos de uso↔máquina de estados↔modelo de datos↔eventos↔API tiene **14 contradicciones** (`02_Contradictions`).

**Mejora:** separar estado técnico (`InvoiceDeliveryStatus`) del comercial (`InvoiceStatus`) (BILL-027).

## 2. Aplicación

**Fortalezas:** el diseño de slices verticales + handlers estáticos con inyección por método coincide con la convención Wolverine del repo; reutiliza el `SqlBusinessIdempotencyExecutor` (patrón verificado).

**Problemas:** `SendInvoiceCommand` es una saga distribuida de 6 pasos sin compensación ni idempotencia por paso (BILL-012, ver `05_Distributed_Workflows`); la publicación de eventos debe ir por outbox en la misma txn (ya soportado por Wolverine); la validación/autorización/CancellationToken/errores están documentados pero no implementados (esperado en B2). **Decisión requerida:** ¿saga persistida vs tabla de operaciones? → tabla `InvoiceDeliveries` + reintentos de inbox (no process manager pesado).

## 3. Infraestructura

**Fortalezas:** Billing ya está cableado a Wolverine con outbox/inbox durable idéntico a Growth (`Program.cs` verificado); schemas `billing/integration/audit`; `BillingDbContext` design-time-constructible; health `live`/`ready`; `apply-migrations.sh` ya lista Billing.

**Problemas:** sin migración EF (no-op hasta B2, BILL-029); repos son stubs (B2); **no existen los clientes M2M** hacia Scribe/CloudStorage/Customer/PaymentClient ni el cliente outbound registrado en Auth (BILL-025). Políticas de timeout/retry/circuit-breaker/DLQ/cleanup/replay documentadas en `05`/`07` — implementar en B4/B5.

## 4. Persistencia y concurrencia

Ver `07_Data_And_Concurrency`. Esquema correcto salvo `AmountRefunded`, `MinorUnits`, `InvoicePaymentLinks` enriquecido, `InvoiceDeliveries`. Numeración → `UPDLOCK/HOLDLOCK` (patrón `SqlReferralRewardQuota`), **no** SQL SEQUENCE (sin precedente en el repo).

## 5. Dinero, impuestos, descuentos

Ver `04` §5. **Billing debe recalcular server-side** desde componentes (BILL-006, decisión de producto exigida por la auditoría); `MinorUnits` por moneda (BILL-010). Riesgo de manipulación si el caller controla los totales — Critical.

## 6. Integración PaymentClient

Ver `10_PaymentClient_Billing_Issues`. **Gap raíz:** PaymentClient no publica `payments.*` y no tiene M2M. La ruta MVP es por `PaymentLink` (`PaymentLinkUsedIntegrationEvent`, correlación por `PaymentLinkId`). Refunds/fallos/chargebacks no tienen evento → PC-ISSUE-01 (P0).

## 7. API

REST correcto en su mayoría; correcciones: `[Authorize]`→`[HasPermission]` (BILL-019), `mark-as-paid`→`payments/manual` (BILL-021), añadir versionado `/v1`, ETag/If-Match↔RowVersion, ProblemDetails, límites de `pageSize`, paginación real (ya prevista). `verify` anónimo endurecido (`06_Security`).

## 8. Seguridad multi-tenant

Ver `06_Security_Review`. Fortalezas: tenant desde JWT, ownership-404. Riesgos P0/P1: correlación de pago sin validar tenant (BILL-007), descarga de PDF ajeno (SEC-02), `verify` enumerable (BILL-017), PII en eventos (BILL-016), `[Authorize]` plano (BILL-019), falta query filter global.

## 9. PII y verificación pública

Ver `06` §3-4. Tensión real: Notification necesita el email **del evento** (patrón `SignerInvitedConsumer` usa `evt.Email`) → el evento de Billing debe llevar el email, en conflicto con "no PII en el fan-out". Resolución: evento dedicado mínimo o que Notification resuelva vía Customer. `verify` con token opaco + rate limit + tiempo constante + respuesta mínima.

## 10. Observabilidad

`BillingMetrics` stub (3 contadores). Ampliar a las métricas del prompt: invoices creadas/emitidas/enviadas/pagadas, tiempo de emisión, pagos no correlacionados, eventos duplicados, fallos Scribe/CloudStorage, links fallidos, outbox pendiente, inbox fallido, reintentos, errores de numeración, conflictos de concurrencia. TraceId/CorrelationId/CausationId ya provistos por BuildingBlocks. Health live/ready ya cableado.

## 11. Pruebas

Ver `08_Test_Strategy`. Hoy solo `ScaffoldSmokeTests`. Falta toda la matriz (dominio/property/integración SQL+RabbitMQ/contract/API/seguridad/concurrencia/idempotencia/resiliencia/migración/E2E).

## 12. Migración

Ver `14_Migration` (diseño) + `08` T-22. **Regla dura:** rechazar import con inconsistencia monetaria (BILL-022). Conservar hashes de recibos legados (no recalcular). Avanzar la sequence al máximo histórico.

## Fortalezas transversales (lo que está bien)

1. La separación de negocio (Billing=factura tenant→taxpayer; Subscription/PaymentApp=SaaS; PaymentClient=cobro) es correcta y respeta el ownership.
2. El scaffold compila, se registra en la solución, gateway/compose/.env/migrations wired; DbContext design-time OK.
3. Reutiliza patrones verificados de la casa (Wolverine outbox/inbox, idempotency executor, ownership-404, tenant-desde-JWT, `UPDLOCK/HOLDLOCK`).
4. La documentación (16 docs) es sustancial y honesta sobre varios gaps (BDR-001..004) — el problema es que subestimó la profundidad de los gaps de integración (PaymentClient/Scribe/Customer).
