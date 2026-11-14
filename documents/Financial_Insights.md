# FinancialInsights: funcionamiento e integración

**Estado del código revisado:** 13-11-2026. **Ruta:** `src/Services/FinancialInsights/`.
Este documento describe los endpoints y el procesamiento que existen en este checkout.
El servicio registra estados bancarios de un cliente de una oficina, extrae movimientos
y guarda sugerencias para revisión humana. No presenta declaraciones fiscales ni
determina deducciones del IRS.

## Arquitectura y tecnologías

| Pieza | Uso concreto |
| --- | --- |
| .NET 10 / ASP.NET Core | API HTTP y `FinancialOcrWorker` como `BackgroundService` en el mismo proceso |
| Domain, Application, Infrastructure, Api | Entidades y reglas, contratos, EF/HTTP/OCR/OpenAI, controladores y worker |
| SQL Server / EF Core 10 | Base `TaxVision_FinancialInsights`, filtros por `TenantId`, migraciones, lotes, movimientos, proyecciones y reservas |
| Wolverine 6.14 / RabbitMQ | Cola `financial-insights-events`, inbox durable, transacciones EF y outbox |
| Redis | Denylist de sesión, caché de módulos/plan y contadores de rate limiting |
| CloudStorage / MinIO | Archivo original, metadatos y URL de descarga firmada; FinancialInsights no guarda una copia permanente |
| ClosedXML 0.105 | Lectura local de XLSX; CSV se decodifica localmente |
| Poppler / Tesseract | `pdfinfo`, `pdftotext`, `pdftoppm` y OCR inglés de páginas escaneadas e imágenes |
| OpenAI Responses API | `POST https://api.openai.com/v1/responses`, modelo configurable `gpt-5.4-mini`, salida JSON con esquema estricto |
| Docker Compose | Servicio `financial-insights-api` bajo perfil `financial`; imagen con Poppler y Tesseract |

El frontend Angular CRM está en otro repositorio. Aquí no hay aún una pantalla
FinancialInsights ni gráficos. Connectors tampoco es una dependencia del servicio:
se utilizó su configuración local de SQL/Redis/RabbitMQ como referencia para el
entorno de desarrollo, manteniendo una base independiente.

## Flujo de datos

1. El usuario staff sube PDF, PNG, JPEG, CSV o XLSX a CloudStorage para un
   `OwnerType=Customer` y espera el escaneo de ClamAV hasta `Available`.
2. `POST /financial/import-batches` recibe el cliente y los identificadores de
   archivo. Auth confirma actor, permiso efectivo y módulo; Customer confirma
   cliente activo y accesible; CloudStorage confirma propietario, MIME detectado,
   estado y tamaño. La API crea el lote y los documentos `Ready`.
3. El worker reclama un documento `Ready` con actualización condicional en SQL.
   Antes de procesarlo vuelve a revisar tenant, cliente y entitlement local. Obtiene
   un token M2M de Auth, consulta CloudStorage y solo descarga si el archivo sigue
   `Available` y pertenece al cliente. La URL firmada debe usar host permitido;
   las redirecciones HTTP están desactivadas.
4. CSV/XLSX se leen sin OCR. PDF usa texto embebido si alcanza el umbral; el resto
   se convierte en imagen y pasa por Tesseract. PNG/JPEG pasan por Tesseract.
   Se envía texto a OpenAI; solo las páginas con OCR insuficiente incluyen imagen.
5. Antes de la llamada al modelo se reserva una posición en la cuota mensual del
   tenant, en una transacción SQL serializable. La llamada usa `store=false`,
   esquema JSON estricto y un prompt que trata el documento como dato no confiable.
   Se validan fecha ISO, importe positivo, moneda, dirección y página. Se guardan
   movimientos con `RequiresReview=true` y uso de tokens cuando llega respuesta.
6. `GET /financial/import-batches/{batchId}` vuelve a validar permiso, módulo y
   acceso al cliente antes de devolver estados y movimientos.

El worker procesa **un documento por ciclo** (10 segundos por defecto), no un
lote completo en una sola llamada. Una carga de 20 archivos puede producir 20
documentos, sujetos a la cuota mensual de procesamiento. Estados:

| Código JSON actual | Estado | Significado |
| --- | --- | --- |
| 0 | `AwaitingScan` | Espera un evento de escaneo; el POST público actual exige `Available` y crea `Ready` |
| 1 | `Ready` | Elegible para el worker |
| 2 | `Processing` | Reclamado por el worker |
| 3 | `Completed` | Extracción terminada sin bandera global de revisión; los movimientos individuales siguen `RequiresReview=true` |
| 4 | `NeedsReview` | Resultado ambiguo, sin movimientos o límite/restricción que requiere intervención |
| 5 | `Rejected` | Rechazado por evento de escaneo antes del procesamiento |
| 6 | `Failed` | Fallo técnico; solo se permite reintento si no hubo reserva de inferencia |

ASP.NET Core serializa este enum como **número** en los endpoints actuales; no
se configuró `JsonStringEnumConverter` en este servicio. Las fechas de movimiento
son `YYYY-MM-DD` y las fechas UTC se serializan como ISO 8601.

## API pública

Base local: `http://localhost:5276`. Vía gateway: prefijo `/financial`.
Todas las operaciones requieren JWT Bearer, actor `TenantEmployee`, `TenantAdmin`
o `PlatformAdmin`, contexto de tenant y módulo `financial_insights` habilitado.
Los permisos `financial.import` y `financial.view` se asignan explícitamente;
no vienen en el paquete de administrador por defecto.

### `POST /financial/import-batches`

Permiso: `financial.import`. Rate limit: `financial.i.import_batch_create`
(base 10 solicitudes/minuto por usuario; superposición 60/minuto por tenant,
ajustable por plan en Subscription). Cuerpo JSON:

```json
{
  "customerId": "11111111-1111-1111-1111-111111111111",
  "fileIds": ["22222222-2222-2222-2222-222222222222"]
}
```

`customerId` es un UUID no vacío. `fileIds` contiene de 1 a 20 UUID distintos,
no vacíos. Cada archivo debe pertenecer al cliente, estar `Available`, tener MIME
**detectado** PDF/PNG/JPEG/CSV/XLSX y tamaño mayor que cero y hasta 25 MiB.
El usuario debe poder ver el cliente (`customers.view`) y el archivo
(`cloudstorage.file.view`) en los servicios correspondientes. Respuesta `201`:

```json
{
  "id": "33333333-3333-3333-3333-333333333333",
  "customerId": "11111111-1111-1111-1111-111111111111",
  "createdAtUtc": "2026-11-13T21:00:00Z",
  "documents": [
    { "fileId": "22222222-2222-2222-2222-222222222222", "status": 1, "failureReason": null }
  ]
}
```

El `POST` no devuelve todavía el `documentId`; se obtiene con el `GET` del lote.
Respuestas de control: `400 Import.InvalidBatch`, `401` sin autenticación,
`403` permiso/módulo/tenant inactivo, `404` cliente no accesible,
`422 Import.FileNotEligible`, `409 Import.FileAlreadyRegistered`, `503`
cuando Auth, Customer o CloudStorage no están disponibles, y `429` por cuota.

### `GET /financial/import-batches/{batchId}`

Permiso: `financial.view`. `batchId`: UUID. Rate limit
`financial.f.import_batch_read` (base 300/minuto por usuario y 3000/minuto por
tenant). Respuesta `200`, con `Cache-Control: no-store`:

```json
{
  "id": "33333333-3333-3333-3333-333333333333",
  "customerId": "11111111-1111-1111-1111-111111111111",
  "createdAtUtc": "2026-11-13T21:00:00Z",
  "documents": [
    { "id": "44444444-4444-4444-4444-444444444444", "fileId": "22222222-2222-2222-2222-222222222222", "status": 3, "failureReason": null }
  ],
  "movements": [
    { "id": "55555555-5555-5555-5555-555555555555", "importDocumentId": "44444444-4444-4444-4444-444444444444", "transactionDate": "2026-11-12", "description": "Deposit", "amount": 125.00, "currency": "USD", "direction": "credit", "suggestedCategory": "Income", "sourcePage": 1, "requiresReview": true }
  ]
}
```

`documents` y `movements` pueden estar vacíos o incompletos mientras el worker
procesa. `404` indica lote inexistente o cliente no accesible; `403` permiso o
módulo, `503` dependencia de autorización no disponible, `429` rate limit.

### `POST /financial/import-batches/{batchId}/documents/{documentId}/retry`

Permiso: `financial.import`; mismos rate limit y comprobación de cliente que el
alta. No tiene cuerpo. Solo cambia `Failed` a `Ready` cuando no existe una
`AiProcessingReservation` para ese documento. Respuesta `202`:

```json
{ "id": "44444444-4444-4444-4444-444444444444", "status": 1 }
```

`409 Import.InferenceAlreadyAttempted` evita volver a pagar una inferencia;
`409 Import.NotRetryable` indica otro estado. También puede devolver `401`,
`403`, `404`, `503` o `429`.

### Portal del cliente: envío automático tras el escaneo

`POST /financial/portal/import-batches` recibe `{ "fileIds": ["UUID", ...] }` (1–20).
Solo admite `CustomerPortal`; `customerId` se toma exclusivamente del JWT. El
cliente necesita `financial.portal.import` y `cloudstorage.file.upload` efectivos
(la oficina puede desactivar el primero para cada cliente desde sus accesos),
los módulos `documents` y `financial_insights` activos,
y una proyección activa de su cliente. Cada archivo debe ser suyo, tener un
tipo declarado permitido y tamaño admitido. Si sigue `PendingScan` o `Scanning`,
el lote lo registra `AwaitingScan`; `FileAvailable` lo pasa a `Ready` y el worker
verifica de nuevo MIME detectado, propietario y estado antes de procesar. Si ya
está `Available`, se registra `Ready`. La API relee los archivos pendientes
después de guardar el lote para cerrar la carrera entre escaneo y registro.
Devuelve `201` con el mismo resumen del alta de staff. El portal sube a
CloudStorage y llama a este endpoint tras completar la carga, sin esperar en
el navegador a que termine el antivirus.

`GET /financial/portal/import-batches/{batchId}` devuelve solo el lote y los
estados de sus documentos al propio cliente del JWT. No entrega movimientos
ni categorías al portal; la revisión de sugerencias sigue en el CRM. Ambos
endpoints revalidan el acceso efectivo en Auth y aplican las cuotas del módulo.

El CRM muestra un resumen derivado de los movimientos extraídos y exporta
un CSV de revisión. No hay saldo inicial/final, aprobación contable ni PDF
de informe en el contrato actual.

### Salud

`GET /health/live` comprueba que la API responde. `GET /health/ready`
comprueba SQL Server mediante EF. **No** certifica conectividad efectiva de
RabbitMQ, Redis, CloudStorage, Auth u OpenAI ni calidad del OCR.

## Integraciones internas y eventos

| Origen/destino | Contrato usado |
| --- | --- |
| Auth | `GET /auth/me/effective-access` con JWT del usuario; M2M para `internal/tenants/{tenantId}/users/{userId}/permissions-snapshot` y token de servicio |
| Customer | `GET /customers/{customerId}` con JWT del usuario, exige tenant correcto y estado activo |
| CloudStorage | `GET /storage/files/{fileId}` con JWT del usuario al registrar; en worker, metadatos y `POST /storage/files/{fileId}/download-url` con M2M `cloudstorage.file.view` y `.download` |
| Subscription | Catálogo `PlanRateLimits` leído por el rate limiter M2M usando `PlanCode` proyectado |
| RabbitMQ | Exchange `taxvision-events`, cola `financial-insights-events`, durable inbox y transacciones Wolverine |
| OpenAI | `POST /v1/responses`; texto y, si falla OCR, imágenes PNG/JPEG en data URL; respuesta JSON estricta `movements[]` y `needsReview` |

Los consumidores de `ProjectionConsumers.cs` reciben `UserRolesChanged`,
`RolePermissionsChanged`, `UserDeactivated`, `UserOffboarded`, `UserReactivated`,
`TenantEntitlementsChanged`, `TenantCreated`, `TenantUpdated`,
`TenantStatusChanged`, `CustomerCreated`, `CustomerUpdated`,
`CustomerActivated`, `CustomerReactivated`, `CustomerDeactivated` y
`CustomerArchived`. `FileScanConsumers.cs` atiende `FileAvailable`,
`FileInfectedDetected` y `FileScanFailed`. Se descartan versiones o marcas de
tiempo anteriores donde el agregado las tiene. Auth ya aplica el deny individual
antes de publicar permisos efectivos; **no unir** la proyección de roles a la
del usuario. Si falta la proyección de usuario, se solicita snapshot M2M a Auth.

## Persistencia, seguridad y límites

`FinancialInsightsDbContext` contiene `ImportBatches`, `ImportDocuments`,
`FinancialMovements`, `AiProcessingReservations`, `UserPermissionsProjections`,
`RolePermissionsProjections`, `TenantEntitlementsProjections`,
`TenantDirectoryProjections` y `CustomerDirectoryProjections`. Los filtros EF
por tenant se aplican a entidades `ITenantOwned`; el worker usa
`IgnoreQueryFilters()` solo junto con filtros explícitos por `TenantId`.
La unicidad `(TenantId, FileId)` impide duplicar un archivo en lotes; la unicidad
`(TenantId, ImportDocumentId)` impide repetir la reserva del modelo.

Las migraciones propias son `InitialFinancialImports`,
`AddFinancialProjections`, `AddPermissionSnapshotState` y
`AddFinancialProcessing`. Auth añade `AddFinancialInsightsPermissions` y
`AddFinancialPortalImportPermission`; Subscription añade
`AddFinancialInsightsModule`. El módulo comercial no tiene
precio ni inclusión automática en planes.
Al iniciar Auth, `SystemRolePermissionsSyncService` incorpora el permiso nuevo
al rol de portal de los tenants existentes; los bloqueos por cliente se conservan.

Por defecto: 10 documentos reservados por tenant y mes, 25 MiB/archivo,
10 páginas, 3 imágenes de respaldo, 30 000 caracteres de texto, 500 movimientos
de salida como máximo y 4000 tokens de salida solicitados. La respuesta del
modelo se considera sugerencia, no dato contable aprobado. No hay todavía
presupuesto en dólares, revisión/confirmación de movimientos, mapeo 1040,
Schedule A/C, FBAR/FATCA, Tax Summary exportable ni gráficos.

## Configuración y ejecución

En desarrollo la API usa `UserSecretsId=taxvision-financialinsights-development`.
Necesita `ConnectionStrings:Default` (base propia), `ConnectionStrings:Redis`,
`RabbitMq:Uri` y `ServiceAuthClient:ClientId/ClientSecret`. Las URLs de Auth,
Customer y CloudStorage y la ruta de la clave pública JWT están en
`appsettings.Development.json`. La credencial M2M debe coincidir con el cliente
31 de Auth. La clave OpenAI se lee de `OpenAI:ApiKey`; si está vacía, el worker
queda inactivo y la API puede arrancar. Desde la raíz, PowerShell:

```powershell
$key = Read-Host 'Pega la API key de OpenAI' -AsSecureString; @{ 'OpenAI:ApiKey' = [System.Net.NetworkCredential]::new('', $key).Password } | ConvertTo-Json -Compress | dotnet user-secrets set --project 'src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj'; Remove-Variable key
dotnet run --project src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj --launch-profile http
```

Para Docker se requieren variables `FINANCIAL_INSIGHTS_DB_CONNECTION`,
`FINANCIAL_INSIGHTS_SERVICE_CLIENT_SECRET` compartida con Auth y
`FINANCIAL_INSIGHTS_OPENAI_API_KEY`; los User Secrets locales no se montan en
contenedores. La URL firmada de MinIO debe usar un host de
`FinancialProcessing:AllowedDownloadHosts` (`MINIO_PUBLIC_ENDPOINT` en Compose).
El wrapper `compose.ps1` requiere un `.env` completo y el par JWT de desarrollo.

```powershell
dotnet ef database update --project src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/TaxVision.FinancialInsights.Infrastructure.csproj --startup-project src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj
dotnet build src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj
dotnet build src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj -c Release
dotnet test deploy/tests/TaxVision.FinancialInsights.Tests/TaxVision.FinancialInsights.Tests.csproj
.\deploy\docker\compose.ps1 local --profile financial up -d --build financial-insights-api
```

La imagen Docker instala Poppler y Tesseract. Para ejecutar OCR de imágenes
directamente en Windows, ambos programas deben estar disponibles en `PATH`.
La flota local de PowerShell usa `scripts/start-fleet.ps1`,
`scripts/restart-down.ps1` y `scripts/stop-fleet.ps1`; FinancialInsights usa
el puerto `5276` y los scripts de inicio cargan el DLL **Release**.
Las pruebas unitarias cubren contratos, proyecciones, lectura CSV/XLSX,
selección de imagen para OpenAI y rechazo de archivos sin escaneo; no sustituyen
una prueba integral con los servicios y una clave OpenAI real.
