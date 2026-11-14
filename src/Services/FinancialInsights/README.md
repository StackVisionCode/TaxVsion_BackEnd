# FinancialInsights: importación de estados de cuenta

Servicio .NET 10 con Domain, Application, Infrastructure y Api, con las convenciones de Wallet: agregados que
devuelven `Result`, handlers de Wolverine, controllers delgados, código de Wolverine pregenerado (Static en
producción) y outbox. Registra archivos ya subidos a CloudStorage, los procesa con OCR local más un modelo de
OpenAI y guarda movimientos **sugeridos** que el staff revisa. No calcula impuestos ni es asesoría fiscal.

## Dominio

- `ImportBatch` (raíz del agregado): de 1 a 20 archivos distintos de un cliente, `Source` (`Staff` o `Portal`),
  `CreatedBy` (procedencia, el retiro de empleados no lo toca). Sus `ImportDocument` solo cambian de estado por
  métodos del agregado: `AwaitingScan → Ready → Processing → Completed | NeedsReview | Failed`, y
  `AwaitingScan → Rejected`. Un `Failed` se puede reintentar solo si nunca se llamó al modelo.
- Cuando el último documento llega a un estado final, el lote levanta `ImportBatchCompletedDomainEvent` una vez por
  ciclo (`CompletionNotifiedAtUtc`); un reintento manual abre un ciclo nuevo.
- `FinancialMovement` (sugerencia con `RequiresReview=true`) y `AiProcessingReservation` (cupo mensual de IA, una
  por documento: un reintento nunca vuelve a cobrar).

## Endpoints

Staff (`TenantEmployee`, `TenantAdmin`, `PlatformAdmin`):

| Método | Ruta | Permiso | Rate limit |
|---|---|---|---|
| POST | `/financial/import-batches` `{ customerId, fileIds }` | `financial.import` | `financial.i.import_batch_create` |
| GET | `/financial/import-batches?customerId=&status=&page=&pageSize=` | `financial.view` | `financial.f.import_batch_read` |
| GET | `/financial/import-batches/{batchId}` | `financial.view` | `financial.f.import_batch_read` |
| POST | `/financial/import-batches/{batchId}/documents/{documentId}/retry` | `financial.import` | `financial.i.import_document_retry` |

Portal (`CustomerPortal`, siempre sobre su propio `customer_id` del JWT):

| Método | Ruta | Permiso | Rate limit |
|---|---|---|---|
| POST | `/financial/portal/import-batches` `{ fileIds }` | `financial.portal.import` + `cloudstorage.file.upload` | `financial.i.portal_import_batch_create` |
| GET | `/financial/portal/import-batches?page=&pageSize=` | `financial.portal.import` | `financial.f.import_batch_read` |
| GET | `/financial/portal/import-batches/{batchId}` | `financial.portal.import` | `financial.f.import_batch_read` |

Los enums viajan como texto. Los errores son `{ code, message }` (`Import.*`, `Tenant.Inactive`,
`Financial.ModuleUnavailable`, `Financial.PermissionDenied`). Cupos: crear 60/10 min por usuario (portal 30/10
min) con tope de 600/10 min por oficina; reintentar 60/10 min; lecturas 300/min por usuario y 3000/min por
oficina. Las cuotas escalan por plan con `RateLimit:EnforceTierQuotas`.

## Acceso sin llamadas en vivo a Auth ni a Customer

- **Permisos**: `[HasPermission]` lee la proyección local `UserPermissionsProjection` (eventos de Auth; si falta
  la fila, pull M2M a `internal/tenants/{t}/users/{u}/permissions-snapshot`). El retiro o la desactivación de un
  usuario la inactiva (`UserLifecyclePermissionsProjectionConsumer`).
- **Módulo**: el gate de módulo (`Authorization:ModuleGate`) exige `financial_insights`; el portal además
  `documents`. Ambos se leen de `TenantEntitlementsProjection`.
- **Clientes**: directorio local `CustomerDirectoryProjection` (eventos de Customer, incluida la importación masiva)
  y visibilidad por asignación (`CustomerAssignmentProjection`, kit compartido con Notes/Tasks): sin
  `customers.view_all` el staff solo ve clientes asignados (`FinancialInsights:CustomerVisibility:Enabled`).
- **CloudStorage**: la única consulta remota del registro es `GET storage/files/{id}` con el token del usuario,
  porque el dueño y el estado del escaneo viven allá (`IImportFileGateway`).

## Plan y cupo mensual de IA

La proyección del plan guarda módulos, `PlanCode` y `financial.documents_per_month` (tope de documentos con IA por
mes; si el plan no envía la clave se usa `FinancialProcessing:MaxDocumentsPerTenantMonth`). Se alimenta con
`TenantEntitlementsChangedIntegrationEvent` y se repara por M2M con
`GET internal/tenants/{tenantId}/entitlements` de Subscription (`SubscriptionClient:BaseUrl`):

- **Al vuelo**: si falta la fila, staff, portal y worker la traen antes de decidir. Solo si Subscription no
  responde el registro devuelve 503 `Import.EntitlementsUnavailable` (nunca se acepta un lote que el worker
  luego dejaría esperando).
- **`TenantEntitlementsReconciliationJob`**: un minuto después del arranque y luego cada 6 h, refresca los
  tenants conocidos (directorio u oficinas con lotes) sin fila o con fila más vieja que el intervalo. Un tenant
  por scope; idempotente (aplica revisiones iguales o más nuevas). `FinancialInsights:EntitlementsReconciliation`.

## Reconciliaciones de clientes

- `TenantCustomerFullReconciliationJob` (12 h): re-pagina `internal/customers/reconciliation` (todos los
  tenants, token de la PlatformTenant) y agrega o refresca filas. Cubre clientes creados antes del servicio.
- `CustomerDirectoryReconciliationJob` (6 h): completa nombres de clientes que llegaron sin él.
- `CustomerAssignmentReconciliationJob` (kit compartido, 12 h): asignaciones desde Customer.

## Worker de OCR

`FinancialOcrWorker` toma un documento listo por vuelta con un UPDATE condicionado (dos instancias no toman el
mismo), revisa oficina, cliente y plan, descarga con URL firmada de host permitido, extrae texto localmente
(CSV/XLSX; `pdftotext` y Tesseract en PDF e imágenes), reserva el cupo en una transacción serializable y llama
al modelo. El resultado se registra con los comandos `CompleteImportDocumentCommand` / `StopImportDocumentCommand`:
una unidad por transacción con outbox. Nunca registra URLs firmadas ni contenido del documento.

## Eventos

Consume (cola `financial-insights-events`): permisos y ciclo de vida de usuarios (Auth), entitlements
(Subscription), alta/estado/nombre de oficinas (Tenant), clientes y asignaciones (Customer), y el resultado del
escaneo de archivos (CloudStorage).

Publica `FinancialImportBatchCompletedIntegrationEvent` (exchange `taxvision-events`) una vez por ciclo de cierre
de un lote, en la misma transacción que el cambio de estado: `BatchId`, `CustomerId`, `CreatedByUserId`, `Source`
(`Staff`/`Portal`), conteos `CompletedDocuments`, `NeedsReviewDocuments`, `FailedDocuments`,
`RejectedDocuments` y `CompletedAtUtc`.

## Configuración

`ConnectionStrings__Default`, `ConnectionStrings__Redis`, `RabbitMq__Uri`, `Jwt__PublicKeyPath`,
`Services__Auth__BaseUrl`, `Services__Customer__BaseUrl`, `Services__CloudStorage__BaseUrl`,
`SubscriptionClient__BaseUrl`, `ServiceAuthClient__ClientId/ClientSecret` (cliente M2M `financial-insights-worker`
registrado en Auth) y `OpenAI__ApiKey` (sin clave el worker queda inactivo). En desarrollo todo sale de los
user-secrets `taxvision-financialinsights-development`; no pegar secretos en `appsettings.json`.

## Verificación

```powershell
dotnet build src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj -c Release
dotnet test deploy/tests/TaxVision.FinancialInsights.Tests/TaxVision.FinancialInsights.Tests.csproj
# Tras cambiar un handler:
powershell -ExecutionPolicy Bypass -File scripts\codegen\regenerate-wolverine-code.ps1 -Service FinancialInsights
```
