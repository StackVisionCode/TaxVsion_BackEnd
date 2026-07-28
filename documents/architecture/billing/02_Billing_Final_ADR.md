# ADR-BILLING-001 — Nuevo microservicio de facturación tenant→taxpayer

Estado: APPROVED (diseño)
Fecha: 2026-07-22

## ID y contexto

El CRM legado `CRMTAXPROBACKEND` permite que un tenant (Company) emita facturas a sus clientes/taxpayers, las envíe por email con un link de pago (IntelliPay) o las marque pagadas manualmente, genere PDFs con watermark "Paid" y emita recibos verificables. Esa capacidad está repartida sin bounded context propio: el modelo y los handlers viven en `CustomerService`, el gateway de pago en `PaymentService`, el envío de correo en `EmailServices`, todos acoplados por ~8 eventos de RabbitMQ.

El CRM nuevo (`TaxVsion_BackEnd`) no tiene ningún modelo de invoice. Subscription documenta repetidamente que “el cobro real y la facturación son responsabilidad de un futuro Billing (Fase 5)”. PaymentClient ya expone el gancho `PaymentPurposeKind.InvoicePayment` + `PurposeExternalReferenceId`. Auth ya tiene seeded `billing.view`/`billing.manage`. Falta el servicio.

## Evidencia real

- Módulo legado de invoices: `CRMTAXPROBACKEND/CustomerService/Domains/Invoice/*`, `.../Applications/Handlers/InvoicesHandles/*`, `.../Controllers/Invoices/InvoiceController.cs` — VERIFIED.
- Gancho de pago del CRM nuevo: `src/Services/PaymentClient/TaxVision.PaymentClient.Domain/ValueObjects/PaymentPurpose.cs` (`Kind`, `ExternalReferenceId` opaco, ≤200) — VERIFIED.
- Permisos: `src/Services/Auth/Domain/Roles/PermissionCatalog.cs:20-21` — VERIFIED.
- Eventos de pago a consumir: `src/BuildingBlocks/Messaging/PaymentIntegrationEvents/PaymentLifecycleIntegrationEvents.cs` — VERIFIED.

## Alternativas

1. **No crear servicio; meter invoices dentro de PaymentClient o Customer.** PaymentClient ejecuta cobros pero no es dueño de la factura como documento comercial (numeración, PDF, recibo, ciclo de vida). Customer es dueño de la identidad del cliente, no de su facturación. Mezclar viola DB-per-service y ownership.
2. **Portar el diseño legado tal cual (status string, numeración client-side, `decimal`, PDF en el mismo proceso con QuestPDF, email directo).** Rápido, pero arrastra los defectos conocidos (sin `[Authorize]`, tenant por query param, paginación rota, `Customer.TaxId` usado para guardar un GUID, riesgo de colisión de número, punto flotante).
3. **Nuevo microservicio `TaxVision.Billing` que adapta el dominio legado a las convenciones del CRM nuevo** (DDD + Wolverine + EF + RabbitMQ, DB-per-service, `Money(long Cents)`, status enum + máquina de estados, numeración server-side, PDF delegado a Scribe/CloudStorage, email vía Notification, correlación de pago vía PaymentClient `PurposeKind`). **Seleccionada.**

## Opción seleccionada y motivo

**Opción 3.** Es la única coherente con el resto de la plataforma: cada capacidad de negocio con estado propio es un servicio con su BD, su outbox/inbox y sus eventos. Billing como dueño único del ciclo de vida de la invoice y del recibo, consumiendo hechos financieros de PaymentClient/PaymentApp y delegando presentación (PDF) y notificación (email) a los servicios que ya existen para eso (Scribe/CloudStorage/Notification). Reutiliza los ganchos ya sembrados (`PaymentPurposeKind.InvoicePayment`, `billing.*` permisos, `FileType.Invoice`).

## Consecuencias

Positivas:
- Ownership limpio: Billing decide y persiste la factura; PaymentClient decide y persiste el cobro; se enlazan por referencia opaca.
- Se corrigen de raíz los defectos del legado (auth, numeración, precisión monetaria, máquina de estados, paginación).
- Presentación y correo dejan de estar acoplados en proceso: Scribe/CloudStorage/Notification.

Negativas:
- Introduce un servicio nuevo (BD, compose, gateway, migraciones, tests) — costo de plataforma.
- La correlación pago↔invoice requiere un mapeo local mientras PaymentClient no eche el eco del `PurposeExternalReferenceId` (BDR-001).
- Depende de un template de invoice en Scribe que aún no existe.

## Riesgos y mitigaciones

| Riesgo | Prob. | Impacto | Mitigación |
|---|---|---|---|
| Correlación pago↔invoice frágil (envelope sin InvoiceId) | Media | Alto | Mapeo local `(PaymentSource,PaymentId)→InvoiceId` persistido por Billing al iniciar el cobro; eco futuro en PaymentClient (BDR-001) |
| Scribe sin template de invoice | Alta | Medio | Fase de implementación con fallback: render mínimo propio o diferir PDF a fase 2 |
| Portar el “sin cálculo de impuestos” del legado | Alta | Medio | Congelar montos como snapshot; gancho `Catalog.ResolvePrice` futuro; decisión abierta documentada |
| Doble emisión de recibo por redelivery | Media | Medio | Inbox durable + idempotencia por `(PaymentSource,PaymentId)` en `RecordPayment` |

## Criterios de aceptación

- `TaxVision.Billing.{Domain,Application,Infrastructure,Api}` compila y se registra en `TaxVision.slnx`.
- `Invoice` y `PaymentReceipt` modelados como aggregates con `Result<T>` en fábricas y transiciones; sin `throw` de flujo.
- Numeración server-side por tenant, `Money(long Cents)`, status enum + máquina de estados explícita.
- Ruta `/billing/*` en gateway; bloque `billing-api` en compose; `BILLING_DB_CONNECTION` en `.env`; migración inicial + línea en `apply-migrations.sh`.
- Contratos de evento `BillingIntegrationEvents` definidos; consumidores de `payments.*` diseñados.

## Archivos afectados (al implementar)

- Nuevos: `src/Services/Billing/**`, `src/BuildingBlocks/Messaging/BillingIntegrationEvents/**`, `src/BuildingBlocks/Authorization/BillingServiceScopes.cs`, `deploy/tests/TaxVision.Billing.Tests/**`.
- Modificados: `TaxVision.slnx`, `deploy/docker/docker-compose.yml`, `src/Gateway/TaxVision.Gateway/appsettings.json`, `.env`, `deploy/docker/migrations/apply-migrations.sh`, `src/BuildingBlocks/BuildingBlocks.Web/Results/ErrorHttpMapping.cs` (prefijos `Billing.*`).

## Estado

APPROVED para scaffolding. La implementación funcional sigue el plan de fases `15_Billing_Implementation_Plan.md`.
