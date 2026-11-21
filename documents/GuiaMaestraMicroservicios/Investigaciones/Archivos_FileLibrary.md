# Archivos y File Library (biblioteca de archivos)

> Investigación de solo lectura. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd\`.
> Etiquetas: **[Código verificado]**, **[Documentación histórica]**, **[Recomendación]**, **[Decisión pendiente]**.
> Pregunta que responde: qué tiene que cumplir un servicio que guarda archivos en CloudStorage para que esos archivos aparezcan bien (o no aparezcan) en la File Library.

## 1. Estado actual

**[Código verificado]** CloudStorage guarda el objeto y su fila `Files`. El servicio de negocio solo guarda el `FileId` (Guid) en su propia tabla. La File Library es una vista de solo consulta de CloudStorage que junta los archivos de todos los módulos. Para clasificarlos usa el **origen declarado** por el servicio dueño: servicio, tipo de recurso, id del recurso, papel del archivo y cliente relacionado.

| Pieza | Dónde | Qué hace |
|---|---|---|
| Declaración de origen | `FileSourceDeclaredIntegrationEvent` (`cloudstorage.file_source_declared.v1`) o campos opcionales de `SaveFileRequestedIntegrationEvent` | El dueño dice de qué recurso nace el archivo |
| Lista blanca de declaraciones | `FileSourceCatalog` (CloudStorage Application) | Pares (recurso, papel) admitidos con sus dueños y carpeta; lo demás se descarta |
| Lista blanca de la biblioteca | `LibrarySourceCatalog` (CloudStorage Application) | Qué filas entran en cada origen de la biblioteca, con qué permiso y módulo |
| Visibilidad por permiso | `FileReadGate` + permisos del origen | CloudStorage decide (Documents, Signature, Billing, FinancialInsights) |
| Visibilidad por fila | `POST /{svc}/files/visibility` con el token del usuario | El dueño decide cada fila (Tasks, Correspondence, Notes, Communication) |
| Reconciliación | `FileSourceClaimReconciliationJob` + `FileSourcesClaimRequestedIntegrationEvent` | Pide a los dueños que declaren los archivos sin origen |
| Descarga mediada | `FileReadGate.IsOwnerMediated` + endpoint del dueño | Lo privado no se abre por id en CloudStorage |
| Borrado | `FilesDeletionRequestedIntegrationEvent` (cola `cloudstorage-file-deletions`) | Retención y limpieza pedidas por el dueño |

Orígenes de la biblioteca hoy: Documents, Signature, Billing, Financial, Tasks, Forms, Mail, Chat, Recordings, Notes (`src/Services/CloudStorage/TaxVision.CloudStorage.Application/Library/LibrarySource.cs:8-20`).

## 2. Evidencia

### 2.1 Guardar el FileId con índice

**[Código verificado]** Cada dueño indexa la columna del `FileId` para que la biblioteca y la reconciliación lo encuentren sin pasar por el recurso padre. El índice lleva `TenantId` cuando la tabla lo tiene.

| Servicio | Migración | Índice |
|---|---|---|
| Correspondence | `src/Services/Correspondence/TaxVision.Correspondence.Infrastructure/Persistence/Migrations/20261120174550_AddDraftAttachmentFileIndex.cs:13-16` | `IX_DraftAttachments_TenantId_FileId` (`TenantId`, `FileId`) |
| Correspondence | `.../Migrations/20261120225134_AddIncomingAttachmentFileIndex.cs:13-16` | `IX_IncomingEmailAttachments_TenantId_CloudStorageFileId` |
| Notes | `src/Services/Notes/TaxVision.Notes.Infrastructure/Persistence/Migrations/20261120221439_AddNoteAttachmentFileIndex.cs:13-16` | `IX_NoteAttachments_CloudStorageFileId` (la hija no tiene `TenantId`; el tenant está en la nota) |
| Tasks | `src/Services/Tasks/TaxVision.Tasks.Infrastructure/Persistence/Migrations/20261120175008_AddFormRevisionPdfFileIndex.cs:13-18` | `IX_FormSubmissionRevisions_PdfFileId` con filtro `[PdfFileId] IS NOT NULL` |

La configuración EF deja el porqué en un comentario corto: `NoteAttachmentConfiguration.cs:29-30` ("La biblioteca pregunta por adjunto con su FileId, sin la nota") y `FormSubmissionRevisionConfiguration.cs:43-47`.

### 2.2 Declarar el origen

**[Código verificado]**
- Contrato: `src/BuildingBlocks/Messaging/CloudStorageIntegrationEvents/CloudStorageIntegrationEvents.cs:398-407` (`FileId`, `SourceService`, `SourceResourceType`, `SourceResourceId`, `SourceRole`, `RelatedCustomerId?`).
- Alternativa cuando CloudStorage guarda por el servicio: campos opcionales de `SaveFileRequestedIntegrationEvent` (`CloudStorageIntegrationEvents.cs:345-353`). Ejemplo: Signature los llena en `src/Services/Signature/TaxVision.Signature.Infrastructure/Sealing/HttpClients/SignatureCloudStorageClient.cs:111-114`. CloudStorage los aplica en `src/Services/CloudStorage/TaxVision.CloudStorage.Application/Files/Commands/SaveFileFromSourceHandler.cs:322-336`; si no encajan en el catálogo, guarda el archivo sin origen y deja un warning.
- Ejemplo de declaración del dueño: `src/Services/Notes/TaxVision.Notes.Application/Notes/NoteFileSources.cs:7-39` (servicio `notes`, recurso `note`, papel `attachment`, cliente solo si la nota es de un cliente).
- Ruta explícita al fanout en cada dueño: Notes `Program.cs:158`, Tasks `Program.cs:182`, Correspondence `Program.cs:166`, Signature `Program.cs:302`, FinancialInsights `Program.cs:115`, Billing `Program.cs:178`.
- Consumidor en CloudStorage: `src/Services/CloudStorage/TaxVision.CloudStorage.Application/Files/Commands/DeclareFileSourceHandler.cs:19-86`.
  - Archivo todavía sin catalogar y declaración reciente → `DependencyNotReadyException` (reintento): `:36-40`.
  - Declaración que no encaja en `FileSourceCatalog` → se descarta y se audita `source.declare-rejected`, sin reintento: `:63-80`.
  - Solo guarda si algo cambió: `:82-84`.
- Dominio: `src/Services/CloudStorage/TaxVision.CloudStorage.Domain/Files/FileObject.cs:464-489`.
  - Una declaración más vieja que la aplicada no pisa: `:466-467`.
  - La misma declaración no se reescribe: `:471-480`.
  - Un archivo de cliente nunca cambia de cliente por una declaración: `:469-470`.
- Marca de "sin origen": `FileObject.SourceDeclaredAtUtc` (`FileObject.cs:90`), nula hasta la primera declaración.

### 2.3 Listas blancas

**[Código verificado]**
- `FileSourceCatalog` (`src/Services/CloudStorage/TaxVision.CloudStorage.Application/Files/FileSourceCatalog.cs:9-141`): entradas en `:19-107`; `Accepts` en `:127-134` exige recurso, papel, dueño (`OwnerType`) y carpeta (`FolderType`) a la vez.
- `LibrarySourceCatalog` (`LibrarySource.cs:61-149`): cada origen con permiso de lectura, módulo, si se recorta por asignación y si es **por fila** (`RowScoped`, `:33`). `Classify` (`:166-219`) es la misma clasificación que aplica el SQL de `LibraryReadRepository` (`:58-59`).
- Un tipo de archivo nuevo necesita entrada en las dos listas (y en el SQL) para salir en la biblioteca. Con solo `FileSourceCatalog` queda declarado pero no se lista.

### 2.4 Visibilidad

**[Código verificado]**

**(a) Por permiso, la decide CloudStorage.**
- `FileReadGate` (`src/Services/CloudStorage/TaxVision.CloudStorage.Application/Abstractions/FileReadGate.cs:18-65`): el PDF de una factura (`OwnerType.Invoice`) exige `invoicing.view`; un archivo de firma (`OwnerType.Signature`) exige un permiso de Signature (`:25-45`). Flag `CloudStorage:SourceReadGate:Enabled`, encendido por defecto (`CloudStorageSourceReadGateOptions.cs:6-8`).
- Orígenes de la biblioteca con permiso propio: Documents `cloudstorage.file.view`, Signature `signature.request.read`, Billing `invoicing.view`, Financial `financial.view` (`LibrarySource.cs:88-98`).

**(b) Por fila, la decide el servicio dueño.**
- Orígenes `RowScoped`: Tasks, Forms (Tasks), Mail (Correspondence), Chat y Recordings (Communication), Notes (`LibrarySource.cs:99-148`).
- Cliente: `src/Services/CloudStorage/TaxVision.CloudStorage.Infrastructure/Library/HttpFileOwnerVisibilityClient.cs:18-157`.
  - `POST {dueño}/files/visibility` con el **token de la petición entrante** (`:89-105`, `:150-156`); rutas en `:141-148`.
  - `GET communication/files/scope` con tope propio (`:70-87`).
  - Falla cerrado: sin URL, sin token, tiempo agotado o respuesta distinta de 200/403 = "no disponible"; 403 = esa persona no ve nada (`:13-17`, `:107-139`). El token no se escribe en logs.
- Registro: `src/Services/CloudStorage/TaxVision.CloudStorage.Infrastructure/DependencyInjection.cs:356-375` (claves `Services:Tasks|Correspondence|Communication|Notes:BaseUrl`); tope por llamada `OwnerTimeoutSeconds` en `:395-396`.
- Topes (`src/Services/CloudStorage/TaxVision.CloudStorage.Application/Library/LibraryOwnerOptions.cs:6-19`, sección `CloudStorage:Library`): 3 s por llamada al dueño, **2 s** para el ámbito de Communication, 6 s de presupuesto total, ventana de 100 ids.
- Respuesta de la biblioteca (`LibraryContracts.cs:69-76`): `unavailableSources` = orígenes cuyo dueño no respondió (sus filas se omiten); `partialSources` = orígenes cuyo ámbito se cortó por el tope (solo salen los de actividad más reciente).
- Endpoints de los dueños: Notes `src/Services/Notes/TaxVision.Notes.Api/Controllers/NoteFilesController.cs:20,31` (`notes.read`, staff); Tasks `TaskFilesController.cs:22,35`; Correspondence `CorrespondenceFilesController.cs:19,35`; Communication `src/Services/Communication/src/api/http/routes/files.route.ts:39` (scope) y `:49` (visibility).
- Contratos compartidos: `src/BuildingBlocks/Contracts/Files/FileVisibilityContracts.cs:4-75` (`VisibleFile` con `CanDownload` y `RestrictedTo` en `:14-23`; lote de 100 en `:27`; propósitos `list`/`download` en `:30-34`; marcas `only_you`, `participants`, `personal_mailbox`, `admin_view` en `:37-43`; ámbito de Communication con tope de 2 000 ids por lista en `:60-74`).
- Compose: `deploy/docker/docker-compose.yml:2141-2144` (bloque `cloudstorage-api`: `Services__Tasks__BaseUrl`, `Services__Correspondence__BaseUrl`, `Services__Communication__BaseUrl: http://communication-api:5350/`, `Services__Notes__BaseUrl`). Guarda: `deploy/tests/TaxVision.BuildingBlocks.Tests/Deploy/ComposeInternalBaseUrlWiringTests.cs:52-55`.
- El código de CloudStorage está cerrado por dueño: `FileOwnerService` (`IFileOwnerVisibilityClient.cs:6-12`), nombre de cliente y ruta en `HttpFileOwnerVisibilityClient`, `AddOwnerClient` en el DI, entrada en `LibrarySourceCatalog` y SQL en `LibraryReadRepository`. Un dueño por fila nuevo toca esos cinco lugares de CloudStorage.

### 2.5 Reconciliación automática de archivos viejos

**[Código verificado]**
- Job: `src/Services/CloudStorage/TaxVision.CloudStorage.Infrastructure/Storage/FileSourceClaimReconciliationJob.cs:20-148`.
  - Espera `ApplicationStarted` (`:31`), respeta `Enabled` (`:38-42`), corre al arrancar y cada `IntervalHours` con `ReconciliationRetry.NextDelay` (`:44-56`).
  - Agrupa por tenant y lote (máximo `MaxFileIds = 500`, `:79`, `:106`) y publica cada mensaje en su unidad con outbox (`TransactionalJobUnit`, `:130-147`).
  - Lo declarado sale solo de la consulta: el trabajo se apaga solo (`:15-19`).
- Opciones: `src/Services/CloudStorage/TaxVision.CloudStorage.Application/Configuration/FileSourceClaimOptions.cs:6-13` (sección `CloudStorage:FileSourceClaims`; `Enabled=true`, `IntervalHours=24`, `BatchSize=500`). Registro: `DependencyInjection.cs:88-89` y `:243`.
- Marca, sin tabla propia: `Files.SourceDeclaredAtUtc IS NULL` y estado `Available` (`src/Services/CloudStorage/TaxVision.CloudStorage.Infrastructure/Persistence/Repositories/CloudStorageRepositories.cs:150-174`, filtro en `:159`).
- Contrato: `CloudStorageIntegrationEvents.cs:413-420` (`cloudstorage.file_sources_claim_requested.v1`). Ruta al fanout: CloudStorage `Program.cs:302`.
- Handlers de los dueños (declaran solo los ids suyos de esa oficina, con los mismos valores que su reconciliación manual; los ajenos se ignoran):

| Dueño | Handler |
|---|---|
| Billing | `src/Services/Billing/TaxVision.Billing.Application/Invoices/IntegrationEvents/FileSourcesClaimRequestedConsumer.cs:12-43` |
| Notes | `src/Services/Notes/TaxVision.Notes.Application/Notes/Consumers/NoteFileSourcesClaimConsumer.cs:12-41` |
| Tasks | `src/Services/Tasks/TaxVision.Tasks.Application/Files/FileSourcesClaimConsumer.cs:14` |
| Correspondence | `src/Services/Correspondence/TaxVision.Correspondence.Application/Files/FileSourcesClaimConsumer.cs:15` |
| Signature | `src/Services/Signature/TaxVision.Signature.Application/Sealing/FileSourcesClaimRequestedConsumer.cs:15` |
| FinancialInsights | `src/Services/FinancialInsights/TaxVision.FinancialInsights.Application/Imports/Consumers/FileSourcesClaimRequestedConsumer.cs:15` |
| Communication (Node) | `src/Services/Communication/src/application/use-cases/claim-file-sources.ts:35-56`, registrado en `src/application/event-handlers/cloudstorage-consumers.ts:111-113` |

- Reconciliación manual (PlatformAdmin, `dryRun=true` por defecto):

| Dueño | Endpoint |
|---|---|
| Notes | `POST /notes/admin/redeclare-file-sources?tenantId=&dryRun=` (`NotesAdminController.cs:13-41`; actor en `:16`) |
| Tasks | `POST /tasks/admin/redeclare-file-sources` (`TasksAdminController.cs:20`) |
| Correspondence | `POST /correspondence/admin/redeclare-file-sources` (`CorrespondenceAdminController.cs:52`) |
| Signature | `POST /signature/admin/redeclare-file-sources` (`SignatureAdminController.cs:128`) |
| Billing | `.../redeclare-file-sources` (`BillingAdminController.cs:24`) |
| FinancialInsights | `.../redeclare-file-sources` (`FinancialAdminController.cs:25`) |
| Communication | `POST /communication/admin/files/redeclare-sources?tenantId=&kind=&dryRun=` (`admin-settings.route.ts:172-196`; permiso de plataforma en `:183`) |

- El orden del deploy no importa: si el dueño todavía no tiene el handler, la pasada siguiente repite la pregunta.

### 2.6 Descargas privadas mediadas por el dueño

**[Código verificado]**
- `FileReadGate.IsOwnerMediated` (`FileReadGate.cs:53-64`): Communication (salvo avatares), notas de voz, adjuntos de notas (`SourceResourceType == "note"`) y adjuntos entrantes de correo sin cliente. Para una persona esos archivos no existen en CloudStorage: no se listan (`CloudStorageRepositories.cs:86-92`) ni se abren por id.
- Flag `CloudStorage:OwnerMediatedGate:Enabled`, encendido por defecto (`CloudStorageOwnerMediatedGateOptions.cs:6-8`; registro en `DependencyInjection.cs:277-280`).
- Endpoints del dueño: Notes `POST /notes/{id}/attachments/{fileId}/download-url` (`NotesController.cs:481`) y portal (`PortalNotesController.cs:67`); Communication `POST /communication/conversations/:id/attachments/:fileId/download-url` (`conversations.route.ts:504`).
- Notes aplica su regla y después pide la URL a CloudStorage por M2M (`src/Services/Notes/TaxVision.Notes.Infrastructure/CloudStorage/NotesFileDownloadClient.cs:15-56`, ruta en `:39`). Por eso el cliente `notes-worker` tiene `cloudstorage.file.view` y `cloudstorage.file.download` (`deploy/docker/docker-compose.yml:655-660`, `ServiceAuth__Clients__20__Permissions__0/1`).

### 2.7 Borrado y retención

**[Código verificado]**
- Contrato: `CloudStorageIntegrationEvents.cs:444-456` (`SourceService`, `SourceReferenceId`, `Reason`, `Files` con dueño y carpeta esperados).
- Punto a punto, sin fanout: CloudStorage escucha `cloudstorage-file-deletions` (`src/Services/CloudStorage/TaxVision.CloudStorage.Api/Program.cs:305`). Consumidor: `FilesDeletionRequestedConsumer.cs:14-23` (verifica dueño, carpeta, tenant y legal hold antes de borrar).
- Publicadores: Tasks `Program.cs:227` (`FormRetentionService.cs:453`), Correspondence `Program.cs:161` (`NotAClientMailRetentionJob.cs:239`), Customer `Program.cs:211`, Documents `Program.cs:171`, Signature `Program.cs:304`, Auth `Program.cs:593`, Communication (`rabbit-file-deletion-requester.ts:8-9`).

### 2.8 API de la biblioteca y permiso

**[Código verificado]**
- `src/Services/CloudStorage/TaxVision.CloudStorage.Api/Controllers/LibraryController.cs`: ruta `storage/library` (`:24`), solo `TenantEmployee`/`TenantAdmin` y sin PlatformAdmin (`:26-28`).
  - `GET /storage/library/sources` (`:36-38`) y `GET /storage/library` (`:49-51`) con `cloudstorage.library.view`.
  - `GET /storage/library/documents` (`:58-60`) con `cloudstorage.file.view`.
  - `POST /storage/library/files/{fileId}/download-url` (`:67-70`) con `cloudstorage.library.view` y `cloudstorage.file.download`.
- Permiso: `src/BuildingBlocks/Authorization/CloudStoragePermissions.cs:11`; en Auth `src/Services/Auth/Domain/Roles/PermissionCatalog.cs:67`.

## 3. Problemas

1. **Dos listas blancas y un SQL que deben coincidir** (`FileSourceCatalog`, `LibrarySourceCatalog`, `LibraryReadRepository`). Una entrada solo en la primera deja el archivo declarado pero fuera de la biblioteca.
2. **CloudStorage cerrado por dueño**: un dueño por fila nuevo exige tocar cinco lugares de CloudStorage (§2.4).
3. **`invoicing.view` como literal** en `FileReadGate.cs:21` y `LibrarySource.cs:64` (Billing no publica sus constantes en BuildingBlocks).
4. **La ruta de reconciliación manual de Communication** (`/communication/admin/files/redeclare-sources` con `kind`) no sigue la forma de los .NET (`/<svc>/admin/redeclare-file-sources`).
5. **Topes cortos con el dueño** (3 s y 2 s): en local, un DNS lento o una URL `localhost` hacia un servicio que solo escucha IPv4 los vence (§9.4).

## 4. Buenas prácticas observadas

- El dueño declara; CloudStorage nunca adivina el origen.
- Idempotencia por estado: misma declaración no reescribe; una más vieja no pisa (`FileObject.cs:466-480`).
- Falla cerrado en la visibilidad por fila y aviso en la página (`unavailableSources`).
- El token del usuario viaja al dueño: la regla se aplica con la identidad real de quien mira.
- Reconciliación sin tabla nueva: la marca es la propia columna `SourceDeclaredAtUtc`.
- Mismos valores en la reconciliación automática y en la manual: el dueño reutiliza su redeclarador (`NoteFileSourcesClaimConsumer.cs:31-38`).

## 5. Recomendación

- **[Recomendación]** Un servicio nuevo que guarda archivos sigue los 7 puntos del [checklist](../Checklists/Archivos_FileLibrary.md) en el mismo cambio que crea la tabla del `FileId`.
- **[Recomendación]** Preferir visibilidad por permiso si todos los que tienen el permiso del módulo pueden ver todos los archivos; por fila solo si hay privacidad por registro (asignación, participantes, autor).
- **[Recomendación]** Publicar `invoicing.view` en BuildingBlocks para quitar los literales.
- **[Decisión pendiente]** Unificar la ruta de reconciliación manual de Communication con la de los .NET.

## 6. Riesgos

- Archivo de un recurso privado sin `IsOwnerMediated`: se abriría por id en CloudStorage para cualquiera con `cloudstorage.file.view`.
- Dueño por fila sin `Services__<Svc>__BaseUrl` en `cloudstorage-api`: en producción sus filas nunca aparecen (falla cerrado) y la página avisa siempre.
- Declaración con un par que no está en `FileSourceCatalog`: se descarta en silencio para el usuario (solo queda la auditoría `source.declare-rejected`).

## 7. Pruebas

### Existentes
- CloudStorage: `FileSourceCatalogTests` (+ `Chat`, `Notes`, `Recordings`), `DeclareFileSourceHandlerTests`, `FileSourceDeclarationTests`, `FileSourceClaimReconciliationJobTests`, `FileReadGateTests` (+ `Note`, `IncomingMail`, `OwnerMediated`), `LibrarySourceCatalogTests` (+ variantes), `SearchLibraryHandlerTests`, `IssueLibraryDownloadUrl*Tests`, `LibraryEndpointsTests` y `LibraryRowSourcesEndpointsTests` (`.Integration.`), `LibraryReadRepository*Tests` (`Persistence`).
- Dueños: `NoteFileSourcesClaimConsumerTests`, `NoteFileSourceClaimIntegrationTests`, `NoteFilesControllerTests`, `GetVisibleNoteFilesHandlerTests`, `RedeclareFileSourcesTests` (Notes, Tasks, Correspondence, Signature, Billing, FinancialInsights), `FileSourcesClaimConsumerTests` (Tasks, Correspondence), `FileSourcesClaimRequestedConsumerTests` (Billing, Signature, FinancialInsights), `TaskFilesControllerTests`, `CorrespondenceFilesControllerTests`; Communication `claim-file-sources.test.ts`, `file-visibility-chat.test.ts`, `file-scope-chat.test.ts`.
- Contratos y despliegue: `FileVisibilityContractsTests`, `ComposeInternalBaseUrlWiringTests`, `PermissionCatalogLibraryTests`.

### Las que debe tener un servicio nuevo
- Declaración: el evento lleva servicio, recurso, papel y cliente correctos; sin cliente cuando el recurso no es de un cliente.
- Catálogo: el par nuevo es aceptado con su dueño y carpeta, y rechazado con otra carpeta.
- Handler de `FileSourcesClaimRequested`: declara solo sus ids de esa oficina, ignora los ajenos, mismos valores que la reconciliación manual, sin tenant → no hace nada.
- Visibilidad por fila: devuelve solo lo que la persona vería en el módulo; lote > 100 → 400; `purpose` inválido → 400; otro tenant → nada.
- Descarga mediada: el endpoint del dueño aplica la regla antes de pedir la URL; CloudStorage responde 404 a la descarga directa por id.
- `.Integration.` del índice y de la consulta por `FileId` contra la base local.

## 8. Dependencias

CloudStorage (catálogos, compuertas, job, API), Auth (permiso `cloudstorage.library.view`, clientes M2M), BuildingBlocks (`CloudStorageIntegrationEvents`, `Contracts/Files`), compose (`cloudstorage-api`, `auth-api`), [Reconciliation](Reconciliation.md) (patrón de job y `ReconciliationRetry`), [M2M_ServiceAuth](M2M_ServiceAuth.md).

## 9. Material para la guía

### 9.1 Declarar desde el dueño

```csharp
// src/Services/Notes/TaxVision.Notes.Application/Notes/NoteFileSources.cs:17-35
public static FileSourceDeclaredIntegrationEvent Declare(
    Guid tenantId, Guid noteId, NoteTargetType targetType, Guid? targetId, Guid fileId, string correlationId) =>
    new()
    {
        TenantId = tenantId,
        CorrelationId = correlationId,
        FileId = fileId,
        SourceService = Service,
        SourceResourceType = ResourceType,
        SourceResourceId = noteId,
        SourceRole = Role,
        RelatedCustomerId = CustomerOf(targetType, targetId),
    };
```

### 9.2 Responder al reclamo de CloudStorage

```csharp
// src/Services/Notes/TaxVision.Notes.Application/Notes/Consumers/NoteFileSourcesClaimConsumer.cs:14-40
public static async Task Handle(
    FileSourcesClaimRequestedIntegrationEvent evt, INoteFileSourceRedeclarer redeclarer,
    IMessageBus bus, ICorrelationContext correlation, CancellationToken ct)
{
    if (evt.TenantId == Guid.Empty || evt.FileIds.Count == 0)
        return;
    using (correlation.Push(string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId))
    {
        var declarations = await redeclarer.DeclarationsForAsync(evt.TenantId, evt.FileIds, correlation.CorrelationId, ct);
        foreach (var declaration in declarations)
            await bus.PublishAsync(declaration);
    }
}
```

### 9.3 Compose de CloudStorage para un dueño por fila

```yaml
# deploy/docker/docker-compose.yml (cloudstorage-api), :2141-2144
Services__Tasks__BaseUrl: http://tasks-api:8080/
Services__Correspondence__BaseUrl: http://correspondence-api:8080/
Services__Communication__BaseUrl: http://communication-api:5350/
Services__Notes__BaseUrl: http://notes-api:8080/
```

### 9.4 Errores históricos

- En Windows, `http://localhost:5350` desde CloudStorage hacia Communication tardaba unos 2 s por intento: el nombre se prueba primero por IPv6 y Communication escucha solo en IPv4 (`COMMUNICATION_HTTP_HOST` por defecto `0.0.0.0`, `src/Services/Communication/src/infrastructure/config.ts:15`; `main.ts:536`). Vencía el tope de 2 s del ámbito y Chat y Recordings salían en `unavailableSources`. Arreglo vigente: `http://127.0.0.1:5350/` en `src/Services/CloudStorage/TaxVision.CloudStorage.Api/appsettings.Development.json:38`. **[Código verificado]** la URL y el bind; el tiempo de ~2 s sale de la verificación en vivo, no del código.
- **[Código verificado]** `Revisiones/restore_generated.ps1` (carpeta de la guía) recorre `git ls-files -m -- $Dir` (`:9`) y devuelve a los bytes de `HEAD` todo archivo modificado que no calce con `-Keep` (`:11`, `:17-37`). Un generado que ya estaba modificado a propósito antes de regenerar se pierde si no va en `-Keep`. Con `powershell -File` el arreglo `-Keep a,b` llega como un solo texto (mismo fenómeno documentado en `scripts/codegen/regenerate-wolverine-code.ps1:17`) y no calza con nada. Se llama con `&` y un arreglo: `& .\restore_generated.ps1 -Repo <repo> -Dir <carpeta> -Keep @('A.cs','B.cs')`.

### 9.5 Checklist

Ver [Checklists/Archivos_FileLibrary.md](../Checklists/Archivos_FileLibrary.md).
