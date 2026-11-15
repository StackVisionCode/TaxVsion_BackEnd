# Prompt de continuidad: TaxVision.FinancialInsights

> Copia este documento completo como contexto para un agente que vaya a cambiar
> FinancialInsights. Es una fotografía del código del **13-11-2026**, no una
> garantía de que el checkout futuro sea idéntico. Los paths siguientes permiten
> verificar solo los archivos afectados por el cambio, sin redescubrir el proyecto.

## Rol y objetivo

Trabajas en el backend TaxVision, repositorio `TaxVsion_BackEnd`, solución
`TaxVision.slnx`, SDK .NET 10.0.300. La tarea es mantener o ampliar el
microservicio `TaxVision.FinancialInsights`, que registra estados bancarios
ya escaneados de un cliente de un tenant, extrae movimientos mediante lectura
local/OCR y OpenAI, y conserva sugerencias para revisión humana. Mantén el
aislamiento multitenant, los denies efectivos por usuario, los contratos de
CloudStorage, las cuotas y la trazabilidad de costes. No presentes una
categoría sugerida como deducción fiscal aprobada.

Antes de editar, lee `AGENTS.md` y `git status --short`; conserva cambios
existentes. Usa los paths de este documento para inspección puntual. La guía
de contrato para humanos está en `documents/Financial_Insights.md` y el
README corto en `src/Services/FinancialInsights/README.md`. No imprimas ni
incluyas en archivos del repo los valores de User Secrets o `.env`.

## Mapa exacto de código

| Responsabilidad | Archivo |
| --- | --- |
| Composición DI, seguridad, HTTP clients, Wolverine, salud | `src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/Program.cs` |
| Alta, consulta y reintento de lotes | `.../TaxVision.FinancialInsights.Api/Controllers/ImportBatchesController.cs` |
| Verificación en vivo de Auth/Customer/CloudStorage con JWT del usuario | `.../TaxVision.FinancialInsights.Api/Imports/UpstreamImportAccess.cs` |
| Claim SQL, cuota y pipeline de procesamiento | `.../TaxVision.FinancialInsights.Api/Processing/FinancialOcrWorker.cs` |
| Consumidores de escaneo | `.../TaxVision.FinancialInsights.Api/Events/FileScanConsumers.cs` |
| Consumidores de Auth/Tenant/Customer/Subscription | `.../TaxVision.FinancialInsights.Api/Events/ProjectionConsumers.cs` |
| Contratos públicos de alta | `.../TaxVision.FinancialInsights.Application/Imports/ImportContracts.cs` |
| Reglas de propietario/MIME/tamaño | `.../TaxVision.FinancialInsights.Application/Imports/ImportFilePolicy.cs` |
| Lote/documento y transiciones | `.../TaxVision.FinancialInsights.Domain/Imports/ImportBatch.cs` |
| Movimiento y reserva de inferencia | `.../TaxVision.FinancialInsights.Domain/Imports/FinancialMovement.cs` |
| Proyecciones y control de versiones | `.../TaxVision.FinancialInsights.Domain/Projections/AccessProjections.cs` |
| EF, índices y filtros por tenant | `.../TaxVision.FinancialInsights.Infrastructure/Persistence/FinancialInsightsDbContext.cs` |
| Fábrica EF de diseño y User Secrets | `.../TaxVision.FinancialInsights.Infrastructure/Persistence/FinancialInsightsDbContextFactory.cs` |
| Lectores de permisos/módulos/plan | `.../TaxVision.FinancialInsights.Infrastructure/Persistence/FinancialProjectionReaders.cs`, `FinancialRateLimitReaders.cs` |
| M2M Auth y snapshot | `.../TaxVision.FinancialInsights.Infrastructure/Permissions/FinancialServiceTokenAcquirer.cs`, `FinancialPermissionsSnapshotClient.cs` |
| Descarga segura de CloudStorage | `.../TaxVision.FinancialInsights.Infrastructure/Processing/CloudStorageStatementSource.cs` |
| CSV/XLSX/PDF/Tesseract | `.../TaxVision.FinancialInsights.Infrastructure/Processing/LocalStatementExtractor.cs` |
| Solicitud y respuesta Responses API | `.../TaxVision.FinancialInsights.Infrastructure/Processing/OpenAiStatementClient.cs` |
| Límites y modelo | `.../TaxVision.FinancialInsights.Infrastructure/Processing/FinancialProcessingOptions.cs` |
| Configuración local/contenedor | `.../TaxVision.FinancialInsights.Api/appsettings*.json`, `Dockerfile`, `deploy/docker/docker-compose.yml` |
| Pruebas | `deploy/tests/TaxVision.FinancialInsights.Tests/` |

En la tabla, `.../` sustituye exactamente
`src/Services/FinancialInsights/`. No hay frontend Angular dentro de este
repositorio; el CRM está en `../TaxVsion_Front/` cuando existe en el workspace.

## Contrato HTTP vigente

Ruta base `financial/import-batches`; gateway `/financial/{**catch-all}`;
API local `http://localhost:5276`. Requiere JWT Bearer, tenant del token y
actor `TenantEmployee`, `TenantAdmin` o `PlatformAdmin`.

1. `POST /financial/import-batches`: permiso `financial.import` y cuota
   `financial.i.import_batch_create`. Body
   `{ "customerId": "UUID", "fileIds": ["UUID", "..."] }`, 1–20 UUID únicos.
   Se exige cliente activo/accesible del mismo tenant y archivos de
   `OwnerType=Customer`, `OwnerId=customerId`, estado `Available`, MIME
   **detectado** PDF/PNG/JPEG/CSV/XLSX, 1–25 MiB. Respuesta `201`:
   `{ id, customerId, createdAtUtc, documents: [{ fileId, status, failureReason }] }`.
   Los documentos se crean `Ready`. No incluye `documentId` en la respuesta.
   `400 Import.InvalidBatch`, `422 Import.FileNotEligible`,
   `409 Import.FileAlreadyRegistered`, `404` si cliente inaccesible,
   `503` si falla dependencia; también `401/403/429`.
2. `GET /financial/import-batches/{batchId}`: permiso `financial.view`,
   cuota `financial.f.import_batch_read`; revalida Auth y Customer. `200`:
   `{ id, customerId, createdAtUtc, documents: [{ id, fileId, status,
   failureReason }], movements: [{ id, importDocumentId, transactionDate,
   description, amount, currency, direction, suggestedCategory,
   sourcePage, requiresReview }] }`. `Cache-Control: no-store`.
3. `POST /financial/import-batches/{batchId}/documents/{documentId}/retry`:
   permiso `financial.import`, sin body; solo `Failed -> Ready` si el
   documento aún no tiene reserva de inferencia. `202 { id, status }`;
   `409 Import.InferenceAlreadyAttempted` o `Import.NotRetryable`.
4. `/health/live` solo indica proceso vivo; `/health/ready` prueba SQL EF.

Los enums `ImportDocumentStatus` salen hoy como **números** JSON: 0
`AwaitingScan`, 1 `Ready`, 2 `Processing`, 3 `Completed`, 4 `NeedsReview`,
5 `Rejected`, 6 `Failed`. Si cambias serialización, versiona/coordina el
contrato del frontend. `Completed` no significa aprobación de sus
movimientos: `RequiresReview=true` se establece en cada uno.

## Seguridad y autorización que debes preservar

- Los permisos son `financial.import` y `financial.view` en
  `src/BuildingBlocks/Authorization/FinancialInsightsPermissions.cs` y
  `src/Services/Auth/Domain/Roles/PermissionCatalog.cs`. El prefijo
  `financial.` se asocia a `financial_insights` en
  `src/BuildingBlocks/Authorization/PermissionModuleMap.cs`. Los permisos
  están excluidos del bundle admin por defecto: el rol debe recibirlos
  explícitamente. El módulo comercial se registra en
  `src/Services/Subscription/TaxVision.Subscription.Infrastructure/Persistence/SubscriptionModuleCatalogSeeder.cs`;
  no tiene precio ni inclusión por defecto.
- Auth publica `UserRolesChanged` con permisos **efectivos**, después de
  restar denies individuales. `RolePermissionsProjection` es informativa;
  nunca unas sus códigos al usuario. `FinancialUserPermissionsReader` exige
  proyección activa con snapshot. Si falta, se recupera un snapshot M2M de
  Auth. La API además consulta `auth/me/effective-access` en cada operación
  con el token del usuario para confirmar permiso y módulo vigentes.
- El usuario necesita visibilidad del cliente en Customer (`GET
  /customers/{id}`) y del archivo en CloudStorage (`GET /storage/files/{id}`).
  El worker usa token M2M tenant-scoped con permisos
  `cloudstorage.file.view` y `cloudstorage.file.download`; vuelve a validar
  dueño, estado `Available`, MIME y tamaño. No saltes el escaneo.
- `FinancialInsightsDbContext` aplica filtro global a entidades
  `ITenantOwned`. Los consumidores/worker usan `IgnoreQueryFilters()` por
  requerimiento de eventos, siempre con `TenantId` explícito en la query.
  La unicidad `(TenantId, FileId)` impide duplicar documentos del mismo tenant.
- La descarga de URL firmada valida esquema, puerto, host allowlist, usuario
  en URL y expiración; no sigue redirecciones. No adjuntes el token M2M a la
  URL firmada. No registres URL firmada, contenido bancario ni API key.

## Eventos y persistencia

Wolverine 6.14 consume el exchange `taxvision-events` mediante la cola
`financial-insights-events` con inbox durable y transacciones EF/outbox.
`ProjectionConsumers.cs` procesa eventos de roles/permisos y ciclo de vida
de usuarios de Auth, entitlements/`PlanCode` de Subscription, estado/nombre
de Tenant y altas/cambios/activación/desactivación/archivo de Customer.
Respeta `PermissionsVersion`, `RevisionNumber`, versión o tiempo observado;
eventos viejos no deben revertir estado nuevo. La proyección de entitlements
invalida las cachés Redis de plan y módulo. `FileScanConsumers.cs` maneja
`FileAvailable`, `FileInfectedDetected`, `FileScanFailed`, con transiciones
idempotentes para documentos ya vinculados a un lote.

La base `TaxVision_FinancialInsights` tiene tablas `ImportBatches`,
`ImportDocuments`, `FinancialMovements`, `AiProcessingReservations` y
proyecciones de usuario, rol, entitlement, tenant y cliente. Cuatro
migraciones propias, en orden: `InitialFinancialImports`,
`AddFinancialProjections`, `AddPermissionSnapshotState`,
`AddFinancialProcessing`. Auth requiere `AddFinancialInsightsPermissions` y
Subscription `AddFinancialInsightsModule`. No consultes directamente las
bases de Auth, Customer, Subscription o CloudStorage desde este servicio.

## Worker, OCR, OpenAI y coste

`FinancialOcrWorker` vive **dentro de la API** como `BackgroundService`.
Si `FinancialProcessing:Enabled=false` o falta `OpenAI:ApiKey`, queda
inactivo sin impedir que la API arranque. Cada ciclo recupera documentos
`Processing` interrumpidos tras 30 minutos y reclama por SQL un solo
documento `Ready` de los diez candidatos más antiguos. Verifica la
proyección activa del cliente, tenant y módulo. Descarga desde CloudStorage,
extrae páginas y reserva cuota **antes** de inferir. La reserva SQL con
aislamiento serializable evita superar 10 documentos/tenant/mes y tiene
índice único `(TenantId, ImportDocumentId)` para evitar doble coste. Tras
una reserva no se permite reintento automático ni por endpoint.

`LocalStatementExtractor`: CSV UTF-8 con fallback Latin-1, XLSX ClosedXML,
PDF con Poppler (`pdfinfo`/`pdftotext`), y `pdftoppm` + Tesseract TSV para
páginas sin texto; PNG/JPEG con Tesseract. Texto legible requiere al menos
80 caracteres alfanuméricos y OCR ponderado >=70. La imagen de una página
queda en el request solo si OCR es insuficiente. Límites: archivo 25 MiB,
10 páginas, 3 imágenes de respaldo, 30 000 caracteres, imagen PDF renderizada
<=4 MiB y llamada local con timeout. Dockerfile instala
`poppler-utils`, `tesseract-ocr` y `tesseract-ocr-eng`.

`OpenAiStatementClient` usa Responses API `POST /v1/responses`, modelo
configurable por `OpenAI:Model` (default `gpt-5.4-mini`), `store=false`,
`max_output_tokens=4000` por defecto y `text.format` JSON Schema estricto.
Cada página aporta `input_text` y, si corresponde, `input_image` PNG/JPEG
como data URL. El esquema pide `movements[]` con `transactionDate` ISO,
`description`, importe positivo, `currency` de tres letras, `direction`
`credit|debit`, `suggestedCategory` y `sourcePage`, más `needsReview`.
El código limita a 500 movimientos, valida el contenido antes de persistir,
guarda tokens de entrada/salida cuando la respuesta termina y marca
`RequiresReview=true`. Las instrucciones prohíben obedecer texto dentro
del documento, inventar transacciones o decidir deducciones fiscales.

La cuota de documentos **no es presupuesto monetario**. No hay aún interfaz
de aprobación, reglas IRS, Schedule A/C, 1040, FBAR/FATCA, Tax Summary,
exportación ni gráficos. Una mejora debe modelar por separado revisión,
auditoría, reglas fiscales y control de gasto por tenant.

## Configuración y arranque

`UserSecretsId` de API: `taxvision-financialinsights-development`. Config
local requerida: `ConnectionStrings:Default` (base propia, mismo servidor
y credenciales de desarrollo que Connectors), `ConnectionStrings:Redis`,
`RabbitMq:Uri`, `ServiceAuthClient:ClientId/ClientSecret`. En Auth debe
existir el cliente 31 con la **misma** credencial y permisos de descarga
CloudStorage. `appsettings.Development.json` fija URLs de Auth `5124`,
Customer `5263`, CloudStorage `5330` y `Jwt:PublicKeyPath` hacia
`dev-keys/jwt-public.pem`; Auth usa el par privado/público del mismo
directorio. `SubscriptionClient:BaseUrl` apunta a `5360`. Puerto de la API:
`5276`. Redis local `6379`, RabbitMQ `5672`, SQL Server `1433`.
`scripts/start-fleet.ps1` y `scripts/restart-down.ps1` arrancan el DLL
`bin/Release/net10.0/TaxVision.FinancialInsights.Api.dll` en `5276`;
`scripts/stop-fleet.ps1` lo incluye en su lista de puertos. El gateway local
ya apunta a `5276`. `scripts/migrate-local.ps1` descubre el DbContext de
FinancialInsights por su snapshot y proyecto con User Secrets; no requiere
una lista manual. El script `scripts/codegen/regenerate-wolverine-code.ps1`
no incluye este servicio porque no tiene la prueba de código pregenerado.

El secreto OpenAI se define **solo** en User Secrets de la API. Comando seguro
desde la raíz, en PowerShell:

```powershell
$key = Read-Host 'Pega la API key de OpenAI' -AsSecureString; @{ 'OpenAI:ApiKey' = [System.Net.NetworkCredential]::new('', $key).Password } | ConvertTo-Json -Compress | dotnet user-secrets set --project 'src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj'; Remove-Variable key
```

Compose tiene perfil `financial` y variables
`FINANCIAL_INSIGHTS_DB_CONNECTION`,
`FINANCIAL_INSIGHTS_SERVICE_CLIENT_ID/SECRET`,
`FINANCIAL_INSIGHTS_OPENAI_API_KEY`,
`FINANCIAL_INSIGHTS_OPENAI_MODEL`, `MINIO_PUBLIC_ENDPOINT`. User Secrets
**no** se trasladan al contenedor. El runner
`deploy/docker/migrations/apply-migrations.sh` aplica la base propia si la
variable de conexión tiene valor. La imagen del servicio instala OCR.
El wrapper `deploy/docker/compose.ps1 local` requiere `.env` completo y el
par `dev-keys/jwt-*.pem`. El `GET /health/ready` comprueba SQL, no todas
las dependencias.

Comandos de validación desde la raíz:

```powershell
dotnet build src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj
dotnet test deploy/tests/TaxVision.FinancialInsights.Tests/TaxVision.FinancialInsights.Tests.csproj
dotnet ef migrations has-pending-model-changes --project src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/TaxVision.FinancialInsights.Infrastructure.csproj --startup-project src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj
dotnet ef database update --project src/Services/FinancialInsights/TaxVision.FinancialInsights.Infrastructure/TaxVision.FinancialInsights.Infrastructure.csproj --startup-project src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj
dotnet run --project src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj --launch-profile http
```

Al cierre del 13-11-2026: compilación sin errores, 20 pruebas unitarias
aprobadas y EF sin cambios pendientes. La conexión SQL local se alineó con
Connectors manteniendo base propia; se generó y verificó el par JWT dev;
Redis/RabbitMQ y la credencial M2M de Auth coinciden. La clave OpenAI local
estaba vacía; el `.env` local ignorado por Git recibió la conexión Docker
a la base propia y la credencial M2M compartida con Auth. No había servicios
de infraestructura escuchando, por lo que
**no hubo llamada real a OpenAI ni prueba integral del stack**. Verifica de
nuevo estas condiciones antes de afirmar que una modificación funciona de
punta a punta.

## Guía de cambio sin redescubrir arquitectura

| Si cambia... | Toca y verifica... |
| --- | --- |
| Un campo de movimiento o del lote | Domain, contrato/API, EF mapping + migración, pruebas, documentos y frontend si consume el campo |
| Un endpoint | Controller, permisos `FinancialInsightsPermissions` y Auth, mapa módulo, policy de rate limit, gateway si cambia prefijo, contrato documentado y pruebas de autorización |
| Un permiso/deny | Auth PermissionCatalog, eventos efectivos/snapshot, `FinancialUserPermissionsReader`; nunca derivar del rol local |
| Un módulo/plan/cuota | Subscription ModuleCatalogSeeder, entitlements con `PlanCode`, invalidación de caché, `RateLimitPolicyCatalog.FinancialInsights.cs`, reserva mensual si afecta OpenAI |
| Un evento | Contrato BuildingBlocks, consumidor en `Events`, `TenantId`, versión/idempotencia y pruebas de reentrega |
| Un formato de archivo | ImportFilePolicy, verificación CloudStorage, LocalStatementExtractor, límites MIME/tamaño y pruebas con archivo sintético |
| OCR o visión | LocalStatementExtractor, opciones, Dockerfile, `OpenAiStatementClient`, límite de páginas y coste; no enviar más imágenes por defecto |
| Modelo o schema OpenAI | `OpenAiStatementClient`, `FinancialProcessingOptions`, migración si cambia persistencia, validación de salida, fake HTTP test y prueba real controlada con clave |
| Despliegue/secretos | `appsettings*`, User Secrets de la API, Auth M2M, Compose, runner de migraciones y README; nunca pegar valores reales |

Resultado esperado de cualquier cambio: código compilado, pruebas específicas
pasadas, migración coherente cuando cambia el modelo, documentación de
contrato actualizada y declaración explícita de lo que no pudo validarse.
