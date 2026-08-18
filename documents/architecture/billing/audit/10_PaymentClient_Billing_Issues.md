# Issues a abrir en PaymentClient (para habilitar la integración con Billing)

Auditoría: arquitecto principal. Fecha: 2026-07-22.
**Evidencia de código (verificada):** `src/Services/PaymentClient/**`, `src/BuildingBlocks/Messaging/PaymentClientIntegrationEvents/**`, `src/BuildingBlocks/Messaging/PaymentIntegrationEvents/PaymentLifecycleIntegrationEvents.cs`.

## Hallazgo raíz (reescribe el diseño de integración de Billing)

Los docs de Billing (`03_Context_Map`, `09_Commands_And_Events`, `13_Payment_Prerequisite_Issues`) asumen que Billing **consume `payments.payment_succeeded/failed/refunded/cancelled`** publicados por PaymentClient y correlaciona por `(PaymentSource, PaymentId)`. **Esto es CONTRADICHO por el código:**

- **PaymentClient NO publica ninguno de los eventos `payments.*`.** Cero referencias a `PaymentIntegrationEvents` en `src/Services/PaymentClient`. `ChargeTenantPaymentHandler` en éxito solo registra métrica + auditoría — **no publica evento de integración**.
- Publica sus **propios** eventos (`BuildingBlocks.Messaging.PaymentClientIntegrationEvents`): `PaymentLinkCreatedIntegrationEvent`, `PaymentLinkUsedIntegrationEvent`, `PaymentLinkExpiredIntegrationEvent`, payouts y connect-account. **Ningún** evento del ciclo de vida del cobro directo (succeeded/failed/refunded/cancelled/chargeback).
- Un **cobro directo** (`POST payments-client/payments`) **no emite nada** al bus. Solo observable por `GET payments-client/payments/{id}`.
- **No hay superficie M2M**: cero `internal/…` y cero `[HasServiceScope]` en PaymentClient. Todo endpoint es JWT humano + `[HasPermission("payment_client.*")]`, token público, o webhook del provider.
- **Refund/cancel de un cargo** existen solo como métodos de dominio disparados por **webhook del provider**; no hay endpoint/comando. El permiso `payment_client.payment.refund` está definido pero **no se aplica a ningún endpoint**. Billing **no puede** disparar un reembolso vía API de PaymentClient hoy.

Corolario: la ruta de integración MVP realista de Billing es **vía PaymentLink** (el cliente hace clic en un link de pago), correlacionando por `PaymentLinkId` (no `(PaymentSource,PaymentId)`), consumiendo `PaymentLinkUsedIntegrationEvent`. Los reembolsos y fallos **no tienen evento** hoy → requieren cambios de contrato en PaymentClient.

---

## PC-ISSUE-01 — Publicar el ciclo de vida del cobro como eventos `payments.*` (o `payment_client.*` equivalentes)

- **Título:** PaymentClient debe publicar eventos de integración en succeeded/failed/refunded/cancelled/chargeback de `TenantPayment`.
- **Problema:** Billing (y cualquier consumidor) no puede reaccionar al resultado de un cobro tenant→taxpayer. Hoy solo `PaymentLinkUsedIntegrationEvent` fira (y solo para link, no para cobro directo), sin identificar el propósito/factura.
- **Cambio de contrato:** al alcanzar estado terminal, publicar `PaymentSucceededIntegrationEvent`/`PaymentFailedIntegrationEvent`/`PaymentRefundedIntegrationEvent`/`PaymentCancelledIntegrationEvent`/`PaymentChargebackChangedIntegrationEvent` (el envelope genérico ya existe en `PaymentIntegrationEvents`), poblando `PaymentSource="PaymentClient"`, `PaymentId`, `TenantId`, montos, `PaidAtUtc`. **Precedente idéntico:** PaymentApp ya lo hace en `SaaSPaymentChargeOutcome.PublishSubscriptionRenewalResultAsync` (publica el genérico además del tipado). Copiar ese patrón en `ChargeTenantPaymentHandler`/`ProcessTenantWebhookHandler`.
- **Compatibilidad:** aditivo. No cambia los eventos `PaymentClient*` existentes; agrega la publicación del genérico. Los consumidores actuales no se ven afectados.
- **Aceptación:** un cobro directo y un cobro por link que llegan a `Succeeded` publican `payments.payment_succeeded`; un refund por webhook publica `payments.payment_refunded`; verificado en tests de integración (RabbitMQ) y consumido por Growth y Billing.

## PC-ISSUE-02 — Echar `PurposeKind` + `PurposeExternalReferenceId` en el envelope de pago

- **Título:** Incluir el propósito (y la referencia externa, p.ej. `InvoiceId`) en los eventos de pago.
- **Problema (BDR-001):** el envelope `PaymentLifecycleIntegrationEvent` no tiene `InvoiceId` ni `PurposeExternalReferenceId`. `TenantPayment.Purpose.ExternalReferenceId` vive en el aggregate pero **no se expone en ningún evento ni API**. Billing no puede correlacionar un pago con su factura desde el evento.
- **Cambio de contrato:** agregar al envelope base (aditivo, nullable): `PaymentPurposeKind? PurposeKind`, `string? PurposeExternalReferenceId`. PaymentClient los puebla desde `TenantPayment.Purpose`.
- **Compatibilidad:** aditivo/nullable → los consumidores existentes (Growth) siguen funcionando (no leen esos campos). `EventVersion` sube a 2.
- **Aceptación:** un pago con `PurposeKind=InvoicePayment, ExternalReferenceId=<InvoiceId>` produce un evento que Billing correlaciona **directamente por `InvoiceId`** (sin tabla de mapeo local), y con `TenantId` para validar tenant.

## PC-ISSUE-03 — Incluir `ProviderEventId` y `PaymentAttemptId` en los eventos

- **Título:** Identificadores de idempotencia/orden en el envelope.
- **Problema:** el envelope no trae `ProviderEventId` ni `PaymentAttemptId`. Billing no puede deduplicar de forma robusta ni ordenar eventos fuera de secuencia con la garantía que tiene PaymentClient internamente (que sí deduplica webhooks por `(TenantId, ProviderCode, ProviderEventId)` y numera `TenantPaymentAttempt.AttemptNumber`).
- **Cambio de contrato:** agregar `string? ProviderEventId`, `Guid? PaymentAttemptId` (o `int? AttemptNumber`) al envelope.
- **Compatibilidad:** aditivo/nullable, `EventVersion=2`.
- **Aceptación:** Billing deduplica por `ProviderEventId` cuando está presente (cae a `EventId` si no), y descarta un `succeeded` de un intento anterior si ya aplicó uno posterior.

## PC-ISSUE-04 — Endpoint M2M (service-scope) para crear/revocar PaymentLink

- **Título:** Superficie M2M para que Billing (servicio) cree y revoque links sin ser un principal humano.
- **Problema:** PaymentClient no tiene `internal/…` ni `[HasServiceScope]`. Crear un link exige `[HasPermission("payment_client.payment_link.manage")]` (claim `perm` humano). Un token de servicio de Billing no encaja en el modelo `perm:` (la convención M2M de la casa usa `scope`+audience, ver `GrowthServiceScopes`).
- **Cambio de contrato:** exponer `POST internal/payment-client/payment-links` y `POST internal/payment-client/payment-links/{id}/revoke` con `[HasServiceScope("payment_client.payment_link.manage")]` (audience `taxvision-payment-client`), tomando `TenantId` del token de servicio. Registrar un cliente M2M `billing-paymentclient` en `ServiceAuth__Clients__N` (compose) con ese scope.
- **Compatibilidad:** aditivo. No toca los endpoints humanos existentes.
- **Aceptación:** Billing, con su token de servicio, crea y revoca links; verificado con test de autorización (rechaza sin el scope, rechaza cross-tenant).

## PC-ISSUE-05 — Endpoint/comando para reembolso iniciado por el owner de la factura (opcional/futuro)

- **Título:** Permitir que Billing solicite un reembolso (hoy solo webhook-driven).
- **Problema:** `RefundPartial/RefundFull` no tienen wiring de comando/endpoint; `payment_client.payment.refund` está sin usar. Billing no puede iniciar un reembolso (p.ej. al anular una factura ya pagada, C-11).
- **Cambio de contrato:** `POST internal/payment-client/payments/{id}/refunds` `[HasServiceScope("payment_client.payment.refund")]` con `{ amountCents, reason, idempotencyKey }`; al confirmarse (webhook), publica `payments.payment_refunded` (PC-ISSUE-01).
- **Compatibilidad:** aditivo.
- **Aceptación:** Billing solicita un reembolso parcial/total; PaymentClient lo ejecuta contra el provider y publica el evento; Billing marca el recibo `Refunded`.
- **Prioridad:** P2 (el MVP puede vivir sin reembolsos iniciados por Billing si define que los reembolsos se hacen desde PaymentClient/soporte; pero sin PC-ISSUE-01 Billing **no se entera** del reembolso de ninguna forma → PC-ISSUE-01 es P0).

## Ruta MVP sin cambios en PaymentClient (mitigación provisional)

Mientras PC-ISSUE-01/02 no existan, Billing puede integrarse **solo por PaymentLink**:
1. Billing crea el link (necesita PC-ISSUE-04 o un token humano provisional).
2. Guarda `PaymentLinkId ↔ InvoiceId` (+ `TenantId`, `ExpectedAmountCents`, `Currency`) en `InvoicePaymentLinks`.
3. Consume `PaymentLinkUsedIntegrationEvent` (`{TenantId, PaymentLinkId, TenantPaymentId, AmountCents, Currency, UsedAtUtc}`), correlaciona por **`PaymentLinkId`** (no `(PaymentSource,PaymentId)`), valida `TenantId`+`AmountCents`+`Currency`, y ejecuta `RecordPayment`.
4. **Limitaciones sin PC-ISSUE-01:** sin evento de **fallo**, **reembolso** ni **chargeback**; `PaymentLinkUsedIntegrationEvent` es "used" (puede resolverse por 3DS después vía webhook), no un "settled" definitivo. Estas lagunas son bloqueantes para producción → PC-ISSUE-01 es P0 para integración productiva.

## Resumen de prioridad

| Issue | Prioridad | Bloquea integración Billing | Bloquea producción Billing |
|---|---|---|---|
| PC-ISSUE-01 (publicar `payments.*`) | P0 | Sí (refund/fail/chargeback) | Sí |
| PC-ISSUE-02 (purpose/InvoiceId en envelope) | P1 | Parcial (hay workaround por `PaymentLinkId`) | No (con workaround) |
| PC-ISSUE-03 (ProviderEventId/AttemptId) | P1 | No | Sí (dedup/orden robusto) |
| PC-ISSUE-04 (M2M service-scope) | P1 | Sí (auth de Billing→PaymentClient) | Sí |
| PC-ISSUE-05 (refund iniciado por Billing) | P2 | No | No (si soporte hace refunds) |
