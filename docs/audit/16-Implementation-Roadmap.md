# Roadmap de implementación

## P0 — antes de exponer producción

1. Inspeccionar `.env.zip` sin divulgar valores; retirar del historial y rotar secretos si aplica.
2. Crear E2E A–D con SQL Server/RabbitMQ/Stripe fake y asserts en las cuatro bases/proyección PDF.
3. Restringir endpoint onboarding PaymentApp por scope/client/audience específico.
4. Cambiar poison invoice request a retry/DLQ/alerta auditable.

## P1 — integridad financiera

1. Persistir saga comercial y evento `InvoiceCreated`.
2. Resolver ventana Stripe-before-DB mediante payment intent local/reconciler.
3. Quote autorizado/firma y validación de neto en PaymentApp.
4. Reserva atómica de stack + compensaciones.
5. Pruebas SQL concurrentes para last-use, idempotency, sequence y doble webhook.
6. Reconciler 1 onboarding : 0/1 payment : 1 invoice : N reservations.

## P2 — claridad y operación

Ledger/instrumentos settlement, contrato nullable para carril cero, política explícita de stacking, tests golden del PDF, dashboards de divergencias y ADRs.

## Criterio de salida

Los cuatro escenarios deben pasar repetidamente con retries, doble entrega y crash injection; ninguna divergencia debe requerir edición manual de DB; todos los estados deben ser trazables por CorrelationId/OnboardingId y compensables.

