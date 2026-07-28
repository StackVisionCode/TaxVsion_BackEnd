# Billing — Contratos API conceptuales

Fecha: 2026-07-22

Rutas públicas bajo el Gateway con prefijo `/billing`. M2M interno fuera del Gateway (`internal/…`). Tenant/actor siempre del JWT, nunca de query param (corrige el gap legado). `Idempotency-Key` por header en toda escritura.

## Públicos / autenticados por Gateway (`/billing/*`)

| Método / ruta | Permiso | Descripción |
|---|---|---|
| `POST /billing/invoices` | `billing.manage` | Crear borrador (UC-01) |
| `PUT /billing/invoices/{id}` | `billing.manage` | Editar borrador (UC-02) |
| `POST /billing/invoices/{id}/issue` | `billing.manage` | Emitir (UC-03) |
| `POST /billing/invoices/{id}/send` | `billing.manage` | Enviar (UC-04) |
| `POST /billing/invoices/{id}/mark-as-paid` | `billing.manage` | Pago manual (UC-05) |
| `POST /billing/invoices/{id}/void` | `billing.manage` | Anular (UC-09) |
| `DELETE /billing/invoices/{id}` | `billing.manage` | Eliminar borrador (UC-10) |
| `POST /billing/invoices/{id}/resend` | `billing.manage` | Reenviar (UC-15) |
| `GET /billing/invoices/{id}` | `billing.view` | Detalle (UC-11) |
| `GET /billing/invoices?status&customerId&search&from&to&page&pageSize` | `billing.view` | Listar con paginación real (UC-12) |
| `GET /billing/invoices/{id}/pdf` | `billing.view` | Descargar PDF (UC-14) |
| `GET /billing/customers/{customerId}/invoices` | `billing.view` | Facturas de un cliente (UC-13) |
| `GET /billing/invoices/{id}/receipts` | `billing.view` | Recibos de la factura (UC-16) |
| `GET /billing/customers/{customerId}/receipts` | `billing.view` | Recibos de un cliente (UC-17) |
| `GET /billing/receipts/{id}/pdf` | `billing.view` | PDF de recibo (UC-19) |
| `POST /billing/receipts/{id}/resend` | `billing.manage` | Reenviar recibo (UC-20) |
| `GET /billing/settings` | `billing.view` | Config de billing (UC-21) |
| `PUT /billing/settings` | `billing.manage` | Actualizar config (UC-21) |
| `POST /billing/receipts/verify` | **`[AllowAnonymous]`** | Verificación pública por hash (UC-18) — único endpoint sin auth, solo lectura/validación |

## Internos, no Gateway (`internal/billing/*`)

M2M con audience `taxvision-billing`, `[HasServiceScope(...)]`, `actor_type=Service`. Para MVP Billing es mayormente **cliente** de otros servicios (PaymentClient/Scribe/CloudStorage), pero expone un endpoint interno para reprocesar/reconciliar:

| Método / ruta | Audience/scope conceptual | Descripción |
|---|---|---|
| `POST internal/billing/invoices/{id}/reconcile-payment` | `taxvision-billing` / `billing.payment.reconcile` | Reaplica un hecho de pago por `(PaymentSource,PaymentId)` (operación de recuperación ante evento perdido) |

Billing como **cliente M2M** llama (con su propio token de servicio):
- PaymentClient `POST internal/payment-client/links` (crear PaymentLink con `PurposeKind=InvoicePayment`, `ExternalReferenceId=InvoiceId`) y `.../cancel`.
- Scribe `POST internal/scribe/render` (invoice/recibo → bytes PDF).
- CloudStorage `POST internal/cloudstorage/files` (`FolderType.Invoices`, `FileType.Invoice`) → `FileId`; y `GET .../files/{id}` para descargar.

## Errores (taxonomía HTTP)

| Código Billing | HTTP | Caso |
|---|---|---|
| `Billing.Invoice.NotFound` (ownership) | 404 | no existe o no es del tenant (no filtra existencia) |
| `Billing.Invoice.InvalidState` | 409 | transición no permitida (p.ej. `Issue` dos veces) |
| `Billing.Invoice.CannotVoidPaid` | 409 | anular una pagada |
| `Billing.Invoice.NotPayable` | 409 | pago sobre estado inválido |
| `Billing.Invoice.Concurrency` | 409 | conflicto `RowVersion` |
| `Billing.Idempotency.FingerprintMismatch` | 409 | misma key, payload distinto |
| `Billing.Invoice.ValidationError` | 422 | líneas/moneda/fecha inválidas |
| (sin permiso) | 403 | falta `billing.view`/`manage` |
| (sin token) | 401 | JWT ausente/ inválido |

Notas de respuesta: incluir siempre `X-Correlation-Id`; nunca devolver el `VerificationHash` completo salvo en el endpoint de verificación; los montos siempre como `*Cents` + `Currency`.

## Diferencias con la API legada (`InvoiceController`)

- Legado: `api/Invoice` **sin `[Authorize]`**, tenant por `companyId` query. Nuevo: bajo Gateway, `[Authorize]` + permiso, tenant del JWT.
- Legado: `GET /Customer/All` calculaba paginación pero devolvía todo. Nuevo: `Skip/Take` real.
- Legado: `GET /SendEmail/{id}` (GET con efecto de escritura). Nuevo: `POST /{id}/send` (verbo correcto).
- Legado: `PaymentReceipts` en controller separado con `verify` anónimo. Nuevo: igual criterio, `verify` `[AllowAnonymous]`.
