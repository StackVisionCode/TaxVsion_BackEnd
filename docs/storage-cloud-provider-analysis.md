# Análisis de almacenamiento externo por Tenant

## Alcance

Este análisis aplica el contexto del proyecto al checkout `ProyectoTax`. El documento adjunto expresa requisitos del producto y fases de implementación; las notas de conexión y credenciales incluidas en él no se copian al repositorio.

## Hallazgos de arquitectura

- El backend sí tiene un microservicio propietario de archivos: `TaxVision.CloudStorage`.
- El servicio ya usa Clean Architecture en proyectos `Domain`, `Application`, `Infrastructure` y `Api`, EF Core con SQL Server y migraciones versionadas, Wolverine/RabbitMQ para trabajo y eventos, autorización por actor/permisos, y filtros globales EF que fallan cerrados sin Tenant.
- El almacenamiento físico actual es MinIO compatible con S3. `IObjectStorage` y `IMultipartUploadStorage` ya aíslan las operaciones del SDK; `MinioObjectStorage` y `S3MultipartUploadStorage` son sus adaptadores actuales. Los handlers, descargas, enlaces firmados, purgas y cargas multipart dependen de estos puertos. Este es el almacenamiento interno de TaxVision y debe conservar su comportamiento.
- Los archivos y carpetas se asocian a `TenantId`; la clasificación existente es `FolderType` (Documents, Receipts, Invoices, EmailIncoming, EmailOutgoing, Tasks, Signatures, Avatars, Imports, Recordings, Transcripts, Backups, Templates, Branding, Other, VoiceNotes y SaaSReceipts). “Exports” no aparece como tipo persistido independiente en el enum; se debe mapear al caso funcional existente al diseñar políticas, no inventar una categoría de archivo sin confirmar su dueño.
- El front principal tiene un feature `storage` (uso, archivos y compartidos) y un área `settings`. El landing es un proyecto distinto.
- `TaxVision.Connectors` ya contiene infraestructura OAuth y proveedores Google/Microsoft para cuentas de correo. No encontré adaptadores de archivos Google Drive, OneDrive ni Dropbox. Sus permisos/scopes y contratos de correo no deben reutilizarse como si autorizaran archivos.
- El backend y frontend están en repositorios Git independientes. No hay un Git raíz común para cambios coordinados.

## Riesgos y decisiones de diseño

1. No sustituir MinIO como destino predeterminado. Un proveedor conectado no debe alterar destinos hasta que exista política explícita del Tenant.
2. Las políticas de archivos deben vivir en CloudStorage, que posee sus metadatos y operaciones. Deben tener filtro/índice tenant-scoped y resolver ausencia de fila a CRM interno.
3. Distinguir almacenamiento operacional, temporal de procesamiento, exportaciones y backups. `FolderType` es clasificación documental; no debe convertirse automáticamente en selector de destino.
4. Reusar el patrón de autorización por permisos y actor del servicio. La selección `X-Tenant`/Tenant del token debe seguir siendo autoridad; nunca aceptar un TenantId arbitrario del body para cambiar de Tenant.
5. Reusar `ISecretProtector`/`IRotatingSecretProtector` del building block de seguridad para tokens OAuth, con claves proporcionadas por el entorno seguro. No guardar secretos en texto plano ni devolverlos en DTOs.
6. Integrar un proveedor de archivos como nuevo adaptador y registrar sus capacidades. No agregar ramas por proveedor a los handlers existentes.
7. La descarga/carga directa con URLs firmadas, multipart, antivirus y auditoría actuales tiene contratos específicos de S3. La primera integración externa debe definir si replica asíncronamente desde MinIO o persiste el archivo directamente en el adaptador; mover el destino primario requiere cambios transversales y no es un reemplazo local del SDK.

## Fases propuestas para este checkout

1. **Análisis (completado):** confirmar propietario, puertos, categorías, multi-tenancy, auth, mensajería y feature Angular.
2. **Diseño:** acordar política por propósito, comportamiento de fallback, autorización OAuth y rotación/secretos antes de persistir conexiones.
3. **Almacenamiento interno:** mantener `IObjectStorage`/`IMultipartUploadStorage` como puertos internos MinIO y establecerlo como valor por defecto explícito en la política.
4. **Primer proveedor externo:** agregar un adaptador aislado para Google Drive API v3, condicionado a registrar la aplicación OAuth, seleccionar scopes/folder de mínimo privilegio y definir sincronización. La investigación de APIs y sus restricciones está en `storage-provider-api-research.md`.
5. **Segundo proveedor y demás:** validar la abstracción con otro adaptador antes de expandir proveedores.
6. **Híbrido, backups y exports:** ejecutar copias resilientes con outbox/jobs, idempotencia, auditoría y destino independiente por propósito.
7. **Frontend:** añadir configuración al feature existente y respetar sus permisos, rutas, servicios y patrones visuales.
8. **Verificación:** cobertura de defaults, aislamiento por tenant, cambios de política, OAuth, subida/descarga, resiliencia y backups.

## Criterio de aceptación base

Un Tenant sin política o que solo conectó un proveedor continúa leyendo y escribiendo en el MinIO interno actual. Solo una política persistida para ese Tenant puede habilitar una copia o un destino externo. Los fallos de la nube nunca borran ni ocultan la copia interna por defecto.

## Próximo corte de implementación

El siguiente cambio funcional debe añadir política tenant-scoped con defaults CRM-only y lectura/escritura protegida por permisos en CloudStorage, junto con su migración y UI de settings. Después se integra Google Drive como copia posterior al escaneo `Available`; la escritura directa y primaria en proveedores externos se reserva hasta que los puertos soporten de forma segura las diferencias de URLs firmadas, multipart, descarga y escaneo.
