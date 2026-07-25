# Billing — Registro de riesgos

Auditoría: arquitecto principal. Fecha: 2026-07-22.
Clasificación: Defect (defecto real) · Risk (riesgo) · Improvement (mejora) · Product (decisión de producto).
Severidad: Critical / High / Medium / Low. Prioridad: P0 (corrupción financiera/cross-tenant o impide scaffolding) · P1 (impide integración/producción segura) · P2 (operabilidad/mantenibilidad) · P3 (futuro).
Evidencia = documental (docs de diseño) + código (repo verificado).

| ID | Riesgo | Capa | Clas. | Sev | Prob | Impacto | Mitigación | Bloquea scaffolding | Bloquea producción | Prio |
|---|---|---|---|---|---|---|---|---|---|---|
| BILL-001 | Pago tras anulación → pago huérfano sin recibo (no definido) | Dominio | Defect | Critical | Media | Dinero cobrado sin factura; disputa | Definir: pago sobre `Voided` ⇒ refund automático + `payment_after_void` + alerta | No | Sí | P0 |
| BILL-002 | Reembolso total deja invoice en `Paid` (no hay `Refunded`) | Dominio | Defect | Critical | Alta | Reportes/impuestos incorrectos | Agregar `Refunded`/`PartiallyRefunded`; `AmountRefunded` separado | No | Sí | P0 |
| BILL-003 | Sobrepago (`amount > AmountDue`) sin manejo | Dominio | Defect | High | Media | Estado corrupto/excepción | Rechazar `Overpayment` + alerta (MVP) | No | Sí | P0 |
| BILL-004 | PaymentClient NO publica `payments.*`; Billing consume un contrato inexistente | Integración | Defect | Critical | Alta (hoy) | Billing no reacciona a pagos/fallos/refunds | Correlación por `PaymentLinkId`/`PaymentLinkUsedIntegrationEvent`; PC-ISSUE-01 | No | Sí | P0 |
| BILL-005 | Scribe no produce PDF (email-HTML only); no hay motor HTML→PDF en el repo | Integración | Defect | Critical | Alta | Sin factura entregable | Añadir motor HTML→PDF tras `IInvoiceDocumentService`; issue en Scribe si se le delega | No | Sí | P0 |
| BILL-006 | Billing confía en `Total`/`Tax` del caller (frontend) | Dominio/App | Product | High | Alta | Manipulación fiscal/fraude | Recibir componentes y recalcular server-side | No | Sí | P0 |
| BILL-007 | Correlación de pago sin validar `TenantId`+monto+moneda → cross-tenant/monto incorrecto | Seguridad | Risk | Critical | Media | Aplicar pago a factura ajena | Validaciones estrictas en el consumer | No | Sí | P0 |
| BILL-008 | `PaymentReceipt` creado dentro de `Invoice.RecordPayment` (2 ARs, 1 método) | Dominio | Improvement | High | — | Acoplamiento, testabilidad, límite txn | Alternativa A (handler crea el recibo) | No | No | P1 |
| BILL-009 | Clave de idempotencia ambigua (`PaymentReference` vs `PaymentId`; manual sin `PaymentId`) | App | Defect | High | Media | Doble recibo o pago perdido | Clave canónica: online `(Source,PaymentId)`/`PaymentLinkId`; manual `Idempotency-Key` | No | Sí | P1 |
| BILL-010 | `Money.AmountCents` asume 2 decimales | Dominio | Defect | High | Media | JPY/BHD/KWD mal redondeados | `MinorUnits` + exponente ISO-4217, o restringir a 2-dec y documentar | No | Sí | P1 |
| BILL-011 | Void de `PartiallyPaid` pone `AmountDue=0` e ignora `AmountPaid>0` | Dominio | Defect | High | Media | Dinero pagado y factura anulada sin refund | Exigir/disparar refund antes de `Voided` | No | Sí | P1 |
| BILL-012 | `SendInvoice` es saga distribuida sin compensación/estado técnico | App | Risk | High | Alta | Estado inconsistente ante fallo parcial | Tabla `InvoiceDeliveries` + pasos idempotentes + `IInvoiceDocumentService` | No | Sí | P1 |
| BILL-013 | No hay superficie M2M en PaymentClient; auth Billing→PaymentClient indefinida | Integración | Defect | High | Alta | Billing no puede crear links de forma segura | PC-ISSUE-04 (endpoint service-scope) | No | Sí | P1 |
| BILL-014 | Customer sin get-by-id M2M ni tax-id/dirección → `CustomerSnapshot` incompleto | Integración | Defect | High | Alta | Snapshot fiscal incompleto | Nuevo endpoint M2M en Customer o evento enriquecido | No | Sí | P1 |
| BILL-015 | CloudStorage upload M2M es event-driven + IAM MinIO propia (no simple HTTP) | Integración | Risk | Medium | Alta | Mayor esfuerzo; cuota/límite por tenant requerido | Cliente MinIO + `SaveFileRequestedIntegrationEvent` (patrón Scribe) | No | Sí | P1 |
| BILL-016 | PII (email/token) en eventos del fan-out `taxvision-events` | Seguridad | Risk | High | Alta | Sobre-exposición de PII | Notification necesita el email del evento (patrón de la casa) → evento dedicado/minimizado; el `Token` NUNCA al bus | No | Sí | P1 |
| BILL-017 | `verify` público enumerable (hash/ReceiptNumber predecible) | Seguridad | Risk | High | Alta | Scraping de recibos, fuga PII | Token opaco + rate limit + respuesta mínima + tiempo constante | No | Sí | P1 |
| BILL-018 | Numeración con RowVersion+retry (tormenta bajo contención) | Persistencia | Improvement | Medium | Media | Latencia/errores bajo carga | `UPDLOCK/HOLDLOCK` upsert+increment (patrón `SqlReferralRewardQuota`) | No | No | P2 |
| BILL-019 | `[Authorize]` plano en el scaffold (sin `perm:`) | Seguridad | Defect | High | Alta (si no se corrige) | Cualquier autenticado accede | `BillingAuthorizationPolicyProvider` + `[HasPermission]` (B2) | No | Sí | P1 |
| BILL-020 | PDF timing inconsistente (07/08 vs 13/15) | Doc | Defect | Medium | — | Confusión de implementación | PDF best-effort; `PdfFileId` nullable; corregir docs | No | No | P2 |
| BILL-021 | `mark-as-paid` con soporte de pago parcial (nombre engañoso) | API | Improvement | Medium | — | Semántica confusa | `POST …/payments/manual` | No | No | P2 |
| BILL-022 | Migración puede importar facturas con inconsistencia monetaria en silencio | Migración | Risk | Medium | Media | Datos corruptos productivos | Rechazar import si `Subtotal+Tax-Discount≠Total`; cola de revisión | No | Sí | P1 |
| BILL-023 | `PaymentLinkUsed` es "used", no "settled" (3DS resuelve después por webhook) | Integración | Risk | Medium | Media | Marcar pagado antes de settlement | Estado `Provisional` o esperar `payments.payment_succeeded` (PC-ISSUE-01) | No | Sí | P1 |
| BILL-024 | Adjuntar PDF al email no soportado (Notification Phase-4) | Integración | Risk | Low | Alta | Sin PDF adjunto en el email MVP | Enviar link de descarga (presigned) en el HTML | No | No | P2 |
| BILL-025 | Sin cliente M2M outbound de Billing registrado en Auth | Integración | Defect | Medium | Alta | Billing no puede autenticarse a Scribe/CloudStorage/Customer | Registrar `ServiceAuth__Clients__N` + token acquirer | No | Sí | P1 |
| BILL-026 | Docs referencian `FileType.Invoice` (no existe) | Doc | Defect | Low | — | Error de implementación | Usar `OwnerType.Invoice`+`FolderType.Invoices`+`TaxYear` | No | No | P2 |
| BILL-027 | `InvoiceStatus` mezclaría estado técnico (PDF/link) si no se separa | Dominio | Improvement | Medium | — | Máquina de estados contaminada | `InvoiceDeliveryStatus` separado | No | No | P2 |
| BILL-028 | Sin métricas/observabilidad desde B1 (BillingMetrics mínimo) | Infra | Improvement | Medium | — | Ceguera operativa | Ampliar métricas (ver `01_Layer_Audit`/prompt) | No | No | P2 |
| BILL-029 | No hay migración EF de Billing (apply-script no-op) | Infra | Risk | Low | Alta | Nada que aplicar hasta B2 | Migración inicial en B2 | No | No (esperado) | P2 |
| BILL-030 | `estado READY_FOR_SCAFFOLDING` oculta blockers de implementación | Doc | Risk | Medium | — | Falsa sensación de listo | Veredicto por etapa (ver `11_Scorecard`) | No | Sí | P1 |

## Distribución

- **P0 (5)**: BILL-001, 002, 003, 004, 005, 006, 007 → dominio financiero + integración inexistente (PaymentClient/Scribe) + manipulación de totales.
- **P1 (14)**: contratos, seguridad, saga, Customer/CloudStorage/Auth M2M, migración.
- **P2 (9)**: operabilidad, docs, numeración óptima.

Ningún hallazgo bloquea **scaffolding** (ya está compilando). Múltiples bloquean **implementación**, **integración** y **producción** (ver `11_Readiness_Scorecard`).
