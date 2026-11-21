# Checklist — Archivos y File Library

> Propósito: que todo archivo que un servicio guarda en CloudStorage llegue a la File Library con su origen, lo vea solo quien debe verlo, se repare solo si quedó sin origen y se borre por el contrato común.
> El servicio de negocio guarda solo el `FileId` (Guid); el objeto y su fila `Files` los guarda CloudStorage. El origen lo declara siempre el dueño, nunca CloudStorage.

**Cuándo aplica:** el servicio guarda un `FileId` de CloudStorage en su base (adjuntos, PDF generados, grabaciones, estados de cuenta, documentos firmados…).

**Formato:** *Verificación* = comando, prueba o archivo; *Origen* = investigación. Condicionales entre corchetes.

---

## 1. Guardar el FileId

- [ ] **Columna del `FileId` con índice** que la encuentre sin pasar por el recurso padre: `(TenantId, FileId)` si la tabla tiene `TenantId`; solo `FileId` en una hija sin `TenantId`; con filtro `IS NOT NULL` si es opcional. Comentario corto del porqué en la configuración EF.
  - Verificación: migración aplicada en local (`scripts/migrate-local.ps1` sin pendientes); patrón `AddDraftAttachmentFileIndex`, `AddNoteAttachmentFileIndex`, `AddFormRevisionPdfFileIndex`.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.1.
- [ ] **El `FileId` es del tenant del recurso**: toda consulta por `FileId` filtra también por oficina (`IgnoreQueryFilters()` + `TenantId` explícito en consumers y jobs).
  - Verificación: prueba "id de otra oficina → no se declara ni se muestra".
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.5; [Guardrails](../Referencias/Guardrails.md) #8.

## 2. Declarar el origen

- [ ] **Al guardar, declarar el origen** con `FileSourceDeclaredIntegrationEvent` (`cloudstorage.file_source_declared.v1`): `SourceService`, `SourceResourceType`, `SourceResourceId`, `SourceRole` y `RelatedCustomerId` (solo si el recurso es de un cliente). Una clase estática por recurso con las constantes (patrón `NoteFileSources`).
  - Verificación: T-APP del handler que adjunta: publica la declaración con esos valores.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.2.
- [ ] [si CloudStorage guarda por el servicio] **Campos de origen en `SaveFileRequestedIntegrationEvent`** (`SourceResourceType`, `SourceResourceId`, `SourceRole`, `RelatedCustomerId`) en lugar del evento aparte.
  - Verificación: patrón `SignatureCloudStorageClient.cs:111-114`.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.2.
- [ ] **Ruta explícita** `options.PublishMessage<FileSourceDeclaredIntegrationEvent>().ToRabbitExchange("taxvision-events")` y guardar antes de publicar.
  - Verificación: `EventRoutingContractTests`; prueba de rutas en runtime.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.2; [Wolverine_RabbitMQ](../Investigaciones/Wolverine_RabbitMQ.md) §6.
- [ ] **Re-declarar al cambiar el papel** del archivo (reemplazado, quitado): CloudStorage no reescribe la misma declaración y una más vieja no pisa a la nueva.
  - Verificación: prueba del dueño; `FileObject.DeclareSource` (`FileObject.cs:464-489`).
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.2.

## 3. Listas blancas de CloudStorage

- [ ] **Par (servicio, recurso, papeles, dueños, carpeta) en `FileSourceCatalog`**. Lo que no encaja se descarta y queda auditado como `source.declare-rejected`.
  - Verificación: caso en `FileSourceCatalogTests` (aceptado con su dueño y carpeta; rechazado con otra carpeta).
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.3.
- [ ] [si el archivo debe salir en la biblioteca] **Entrada en `LibrarySourceCatalog`** (permiso, módulo, `RowScoped`) y la misma clasificación en el SQL de `LibraryReadRepository`.
  - Verificación: `LibrarySourceCatalogTests`; `LibraryReadRepository*Tests`.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.3.

## 4. Visibilidad

- [ ] **Modelo elegido y escrito**: (a) por permiso, si todo el personal con el permiso del módulo ve todos los archivos (decide CloudStorage con `FileReadGate` y permisos); (b) por fila, si hay privacidad por registro (decide el dueño).
  - Verificación: decisión en el PR. · Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.4 y §5.
- [ ] [si (a) y el archivo exige el permiso de su módulo] **Superficie en `FileReadGate`** (`SurfaceOf` + `PermissionsOf`).
  - Verificación: `FileReadGateTests`.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.4.
- [ ] [si (b)] **`POST /<svc>/files/visibility`** con `FileVisibilityRequest`/`FileVisibilityResponse` de `BuildingBlocks/Contracts/Files`: staff, permiso de lectura del módulo, `[RateLimit]`; devuelve solo lo que esa persona vería en el módulo; `CanDownload` y `RestrictedTo` cuando corresponda; lote > 100 o `purpose` distinto de `list`/`download` → 400.
  - Verificación: patrón `NoteFilesControllerTests`, `GetVisibleNoteFilesHandlerTests`, `NoteFileVisibilityIntegrationTests`.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.4.
- [ ] [si (b)] **Alta del dueño en CloudStorage**: `FileOwnerService`, nombre de cliente y ruta en `HttpFileOwnerVisibilityClient`, `AddOwnerClient(..., "Services:<Svc>:BaseUrl")`.
  - Verificación: `SearchLibraryHandlerTests`, `LibraryRowSourcesEndpointsTests`.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.4.
- [ ] [si (b)] **`Services__<Svc>__BaseUrl: http://<svc>-api:8080/` en el bloque `cloudstorage-api`** del compose y su fila en `ComposeInternalBaseUrlWiringTests`; URL local en `appsettings.Development.json` de CloudStorage (`http://127.0.0.1:<puerto>/` si el dueño es Node).
  - Verificación: `ComposeInternalBaseUrlWiringTests`; biblioteca local sin el origen en `unavailableSources`.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.4 y §9.4.
- [ ] **Falla cerrado aceptada**: si el dueño no responde, sus filas no se muestran y la página avisa (`unavailableSources`); `partialSources` solo indica un ámbito recortado por tope.
  - Verificación: prueba de la página/cliente con el dueño caído.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.4.

## 5. Archivos viejos sin origen

- [ ] **Handler de `FileSourcesClaimRequestedIntegrationEvent`** (`cloudstorage.file_sources_claim_requested.v1`): busca esos ids en sus tablas de esa oficina, declara solo los suyos y con los mismos valores que su reconciliación manual; ignora los ajenos; sin tenant o sin ids no hace nada.
  - Verificación: patrón `NoteFileSourcesClaimConsumerTests`, `NoteFileSourceClaimIntegrationTests`.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.5; [Reconciliation](../Investigaciones/Reconciliation.md).
- [ ] **Reconciliación manual** `POST /<svc>/admin/redeclare-file-sources?tenantId=&dryRun=`: solo PlatformAdmin, `dryRun=true` por defecto, idempotente, `[RateLimitExempt]` con razón.
  - Verificación: patrón `RedeclareFileSourcesTests` (Notes, Tasks).
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.5.
- [ ] **El orden del deploy no importa**: la pasada siguiente del job de CloudStorage (al arrancar y cada 24 h) repite la pregunta. No hace falta tabla ni marca propia.
  - Verificación: revisión. · Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.5.

## 6. Descargas privadas

- [ ] [si los archivos son privados del recurso] **Endpoint propio de descarga** (patrón `POST /notes/{id}/attachments/{fileId}/download-url`): aplica la regla del dueño y después pide la URL a CloudStorage por M2M.
  - Verificación: prueba "sin acceso al recurso → 404"; "con acceso → URL".
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.6.
- [ ] [si los archivos son privados del recurso] **Origen mediado en `FileReadGate.IsOwnerMediated`**: CloudStorage no los lista ni los abre por id para una persona (flag `CloudStorage:OwnerMediatedGate:Enabled`, encendido por defecto).
  - Verificación: patrón `FileReadGateNoteTests`, `ListFilesNoteOwnerMediatedTests`, `IssueDownloadUrlOwnerMediatedTests`.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.6.
- [ ] [si el servicio pide URLs de descarga por M2M] **`cloudstorage.file.download`** (y `cloudstorage.file.view` si consulta el estado) en su cliente de `ServiceAuth` del bloque `auth-api` (patrón `ServiceAuth__Clients__20__Permissions__1` de `notes-worker`) y en los user-secrets locales de Auth.
  - Verificación: compose `auth-api`; descarga real en local.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.6; [M2M_ServiceAuth](../Investigaciones/M2M_ServiceAuth.md).

## 7. Borrado y retención

- [ ] [si borra o retiene archivos] **`FilesDeletionRequestedIntegrationEvent`** punto a punto a la cola `cloudstorage-file-deletions` (`PublishMessage<...>().ToRabbitQueue("cloudstorage-file-deletions")`), con el dueño y la carpeta esperados de cada archivo.
  - Verificación: prueba del job de retención; `FileDeletionSkippedIntegrationEvent` manejado (reintento con el dueño nuevo).
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.7.

## 8. Local y regeneración

- [ ] **Regenerar codegen** de CloudStorage y del dueño al agregar handlers o clientes; si se usa `Revisiones/restore_generated.ps1`, llamarlo con `&` y arreglo, e incluir en `-Keep` también los generados que ya estaban modificados antes de regenerar.
  - Verificación: `PreGeneratedHandlerCodeTests` de ambos servicios.
  - Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §9.4; [Roslyn_Produccion](../Investigaciones/Roslyn_Produccion.md) §10.
- [ ] **Verificación en vivo**: el archivo aparece en `/storage/library` con su origen; un usuario sin acceso al recurso no lo ve; la descarga funciona por la ruta que corresponde.
  - Verificación: flota local con el usuario de pruebas. · Origen: [Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §2.8.

## Decisiones pendientes

- Unificar la ruta de reconciliación manual de Communication (`/communication/admin/files/redeclare-sources`) con la de los .NET ([Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §5).
- Publicar `invoicing.view` en BuildingBlocks para quitar los literales de CloudStorage ([Archivos_FileLibrary](../Investigaciones/Archivos_FileLibrary.md) §3).
