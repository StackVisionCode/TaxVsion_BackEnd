# Guía: cómo conectar un microservicio nuevo a CloudStorage (MinIO)

> Para cualquier dev backend (.NET o Node) que necesite que su microservicio suba,
> baje o borre archivos. Cubre los **2 casos reales** que existen en este backend,
> con pasos concretos, código de referencia y una checklist al final de cada uno.

---

## 0. Resumen ejecutivo

CloudStorage es el **único** microservicio que le habla a MinIO para el camino
"de verdad" (bucket final, escaneo antivirus con ClamAV, cuotas por tenant,
recycle bin, sharing, etc.). Ningún otro servicio reimplementa esa lógica.

Pero un archivo tiene que *llegar* a MinIO de alguna manera, y ahí hay
**dos patrones distintos**, según de dónde vienen los bytes:

| | ¿Quién tiene los bytes primero? | Patrón | Ejemplos reales en este repo |
|---|---|---|---|
| **Caso A — Interno** | Tu propio backend (los genera él mismo, o los recibe en un endpoint propio) | Tu servicio sube **directo a MinIO** con credenciales propias + avisa por evento | Signature (PDF sellado), Notification (adjuntos IMAP), CommunicationTranscriptWorker (transcript), Customer (Excel/CSV de import) |
| **Caso B — Directo externo** | El navegador del usuario | El **frontend** le habla directo a CloudStorage (con el JWT del usuario), tu servicio ni toca los bytes | Communication (adjuntos de chat) |

Regla rápida para decidir cuál te toca:

- ¿El archivo lo genera o lo recibe **tu backend** (un PDF que armaste vos, un
  adjunto que bajaste de un IMAP, un archivo que el usuario te mandó a *tu*
  endpoint)? → **Caso A**.
- ¿El archivo lo tiene el usuario en su navegador y no hay ninguna razón de
  negocio para que pase por tu backend antes? → **Caso B** (más simple, menos
  código, cero carga extra en tu servicio).

Si tenés dudas, seguí leyendo — cada caso tiene su propia sección con pasos.

---

## 1. Por qué existen dos patrones (contexto rápido)

Antes existía un patrón único: tu servicio le pedía a CloudStorage por HTTP
"quiero subir un archivo", CloudStorage te daba una URL presignada, subías el
archivo, y le avisabas a CloudStorage "ya subí, confirmá". Tres llamadas HTTP
por archivo, siempre pasando por la red interna.

Se simplificó a lo que hay hoy (le decimos **Fase D** en el historial del
proyecto):

- Si los bytes ya están en tu backend, **no tiene sentido subirlos a
  CloudStorage por HTTP para que CloudStorage los vuelva a subir a MinIO** —
  es una posta doble. Ahora tu servicio sube **directo a MinIO** (con sus
  propias credenciales, acotadas a su propia carpeta) y solo le manda un
  **evento** a CloudStorage diciendo "ya subí este archivo a tal key, registralo
  y escaneálo". CloudStorage lo escanea (antivirus + política de contenido) de
  forma asíncrona y lo mueve a su lugar final.
- Si los bytes están en el navegador del usuario, tiene incluso menos sentido
  que pasen por tu backend — que se suban directo del navegador a MinIO, con
  las credenciales normales de usuario que ya emite CloudStorage.

En los dos casos, **la lógica de escaneo, cuotas, recycle bin, sharing, etc.
vive solo en CloudStorage** — vos nunca la reimplementás.

---

## 2. Caso A — Interno (tu backend sube/baja bytes)

### 2.1 Diagrama de flujo

```
┌──────────────┐   1. Sube directo a MinIO      ┌───────┐
│ Tu servicio  │ ───────────────────────────────▶│ MinIO │
│ (backend)    │   (credenciales propias,        └───────┘
│              │    scoped a tu carpeta)
│              │   2. Publica SaveFileRequestedIntegrationEvent
│              │ ───────────────────────────────▶┌─────────────┐
│              │                                  │ CloudStorage│
│              │   3. (si necesitás leer de       │ (escanea,   │
│              │◀──vuelta) esperás el evento──────│  mueve al   │
│              │    FileAvailable/Infected/        │  bucket     │
│              │    BlockedByPolicy                │  final)     │
│              │                                  └─────────────┘
│              │   4. Descarga/borra vía HTTP+M2M
│              │ ───────────────────────────────▶ CloudStorage API
└──────────────┘
```

Dos variantes de Caso A, según si necesitás **leer el archivo de vuelta**:

- **A1 — Fire-and-forget**: subís el archivo y nunca más lo necesitás leer
  (ej. Signature sube el PDF ya sellado como artefacto final). No hace falta
  consumer de eventos — el `FileId` ya lo generaste vos, lo guardás en tu
  propio aggregate, y listo.
- **A2 — Subís y después necesitás bajarlo**: subís un archivo para procesarlo
  después (ej. Customer sube el Excel/CSV para parsearlo). Acá **sí** hace
  falta esperar a que CloudStorage confirme que pasó el escaneo antes de
  descargarlo — si lo intentás descargar antes de que exista en el bucket
  final, vas a fallar. Ver paso 8.

### 2.2 Paso a paso completo

#### Paso 1 — Cuenta de servicio scoped en MinIO

Cada servicio tiene su **propia** cuenta de MinIO, con permiso *solo* para
subir (`s3:PutObject`) dentro de su propia carpeta en `taxvision-temp/`.
**Nunca** usar las credenciales root de MinIO (`MINIO_ROOT_USER`/`PASSWORD`).

1. Creá el archivo de policy en `deploy/docker/minio/policies/<tu-servicio>-source.json`:

   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       {
         "Effect": "Allow",
         "Action": ["s3:PutObject"],
         "Resource": ["arn:aws:s3:::taxvision-temp/<tu-servicio>/*"]
       }
     ]
   }
   ```

2. Agregá una línea en `deploy/docker/minio/provision-service-accounts.sh`:

   ```sh
   provision "<tu-servicio>" "<tu-servicio>-worker" "${TU_SERVICIO_MINIO_SECRET:?Set TU_SERVICIO_MINIO_SECRET}" \
     "$(dirname "$0")/policies/<tu-servicio>-source.json"
   ```

3. Generá un secreto nuevo (32 bytes random, cualquier formato) y agregalo a
   **los 3 lugares** (sí, los 3 — es fácil olvidarse de uno):
   - `.env` local: `TU_SERVICIO_MINIO_SECRET=...`
   - `.github/workflows/deploy.yml` (sección "Write production .env"):
     `TU_SERVICIO_MINIO_SECRET=${{ secrets.TU_SERVICIO_MINIO_SECRET }}`
   - El servicio `minio-provision` en `docker-compose.yml` (bloque
     `environment`): `TU_SERVICIO_MINIO_SECRET: ${TU_SERVICIO_MINIO_SECRET:?...}`
   - Y creá el **GitHub Secret** con ese mismo nombre en el repo (Settings →
     Secrets), si vas a desplegar.

4. Provisioná la cuenta contra MinIO (local, con Docker corriendo):

   ```bash
   docker compose -f deploy/docker/docker-compose.yml --env-file .env \
     --profile tools run --rm minio-provision
   ```

   Es idempotente — correrlo de nuevo no rompe nada, solo actualiza la
   password si el user ya existe.

#### Paso 2 — Cliente M2M en Auth (para descargar/borrar)

Si tu servicio necesita **leer o borrar** el archivo después (Caso A2, o
cualquier borrado), necesita un token de servicio. Se pide un cliente M2M
nuevo en Auth — **esto es config, no código nuevo** (Auth ya tiene toda la
maquinaria de client-credentials armada):

1. En `docker-compose.yml`, en el bloque `environment` de `auth-api`, agregá
   un índice nuevo (mirá los que ya existen, `ServiceAuth__Clients__0/1/2/3`,
   y agregá el siguiente):

   ```yaml
   ServiceAuth__Clients__N__ClientId: ${TU_SERVICIO_SERVICE_CLIENT_ID:-<tu-servicio>-worker}
   ServiceAuth__Clients__N__Secret: ${TU_SERVICIO_SERVICE_CLIENT_SECRET:-}
   ServiceAuth__Clients__N__Permissions__0: cloudstorage.file.download
   ServiceAuth__Clients__N__Permissions__1: cloudstorage.file.view
   ServiceAuth__Clients__N__Permissions__2: cloudstorage.file.delete   # solo si vas a borrar
   ```

   Dale **solo los permisos que realmente necesitás** (principio de menor
   privilegio) — si tu servicio nunca borra archivos, no le des
   `cloudstorage.file.delete`.

2. Para desarrollo local (`dotnet run`, sin Docker), das de alta el mismo
   cliente directo en los user-secrets de Auth:

   ```powershell
   dotnet user-secrets set "ServiceAuth:Clients:N:ClientId" "<tu-servicio>-worker" `
     --project src\Services\Auth\Api
   dotnet user-secrets set "ServiceAuth:Clients:N:Secret" "<mismo-secreto>" `
     --project src\Services\Auth\Api
   dotnet user-secrets set "ServiceAuth:Clients:N:Permissions:0" "cloudstorage.file.download" `
     --project src\Services\Auth\Api
   dotnet user-secrets set "ServiceAuth:Clients:N:Permissions:1" "cloudstorage.file.view" `
     --project src\Services\Auth\Api
   ```

3. Agregá `TU_SERVICIO_SERVICE_CLIENT_ID`/`_SECRET` a `.env`,
   `docker-compose.yml` (bloque de `auth-api` de arriba) y `deploy.yml` — los
   3 lugares, igual que en el paso 1.

#### Paso 3 — Configuración de tu servicio

**Si tu servicio es .NET**, seguí exactamente el patrón que ya usan
Signature/Notification/Customer (buscá `SignatureMinioOptions.cs`,
`NotificationMinioOptions.cs` o `CustomerMinioOptions.cs` como referencia):

```csharp
// TuServicioMinioOptions.cs
public sealed class TuServicioMinioOptions
{
    public const string SectionName = "TuServicio:Minio";
    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public string TempBucket { get; set; } = "taxvision-temp";
    public string SourcePrefix { get; set; } = "<tu-servicio>";
}

// ServiceAuthClientOptions.cs + CloudStorageClientOptions.cs (solo si descargás/borrás)
public sealed class ServiceAuthClientOptions
{
    public const string SectionName = "ServiceAuthClient";
    public string AuthBaseUrl { get; set; } = "http://localhost:5124";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class CloudStorageClientOptions
{
    public const string SectionName = "CloudStorageClient";
    public string BaseUrl { get; set; } = "http://localhost:5330";
}
```

**Si tu servicio es Node/TS**, mirá `CommunicationTranscriptWorker` como
referencia (`src/Services/CommunicationTranscriptWorker/src/config.ts` +
`src/cloudstorage/minio-uploader.ts`) — mismo esquema, variables de entorno en
vez de `IOptions<T>`.

#### Paso 4 — El cliente (Upload / Download / Delete)

Este es el corazón del patrón. Copiá la estructura de
`SignatureCloudStorageClient.cs` (Signature) o
`CustomerImportCloudStorageClient.cs` (Customer) — son casi idénticas:

```csharp
public async Task<Result> UploadAsync(
    Guid tenantId, Guid fileId, byte[] content,
    string fileName, string contentType, Guid actorId,
    CancellationToken ct = default)
{
    var options = minioOptions.Value;
    var sourceObjectKey = $"{options.SourcePrefix}/{fileId:N}/{fileName}";

    // 1. Subida directa a MinIO con TUS credenciales
    using var stream = new MemoryStream(content);
    await minioClient.PutObjectAsync(
        new PutObjectArgs()
            .WithBucket(options.TempBucket)
            .WithObject(sourceObjectKey)
            .WithStreamData(stream)
            .WithObjectSize(content.LongLength)
            .WithContentType(contentType),
        ct);

    // 2. Avisale a CloudStorage que lo registre y escanee
    await bus.PublishAsync(new SaveFileRequestedIntegrationEvent
    {
        TenantId = tenantId,
        FileId = fileId,                    // el FileId LO GENERÁS VOS (ver nota abajo)
        RequestingService = "<tu-servicio>", // solo para logs/auditoría
        SourceBucket = options.TempBucket,
        SourceObjectKey = sourceObjectKey,
        ActorId = actorId,
        OwnerType = "Tenant",                // ver tabla de enums, sec 5
        OwnerId = null,
        FolderType = "Documents",            // ver tabla de enums, sec 5
        TaxYear = null,
        OriginalName = fileName,
        ContentType = contentType,
        SizeBytes = content.LongLength,
        CorrelationId = correlation.CorrelationId,
    });

    return Result.Success();
}
```

> **Nota clave sobre el `FileId`**: lo generás **vos**, no CloudStorage. Esto
> es a propósito — así podés guardar esa referencia en tu propio aggregate
> *antes* de que termine el escaneo asíncrono. También sirve de idempotencia:
> un reintento con el mismo `FileId` no duplica nada (CloudStorage lo trata
> como no-op si ya existe).
>
> **Truco útil (Caso A2)**: si tu aggregate ya tiene su propio Id único (por
> ejemplo `MiJobAttempt.Id`), **reusalo como `FileId`** en vez de generar un
> campo nuevo — así correlacionás el evento de vuelta sin persistir nada
> extra. Es lo que hace Customer (`CustomerImportAttempt.Id` == `FileId`).

Para **descargar** (HTTP+M2M):

```csharp
public async Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct)
{
    var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);       // POST auth/service-token
    var urlResponse = await httpClient.PostAsync($"storage/files/{fileId}/download-url", ...); // con Bearer token
    var presignedUrl = /* parsear DownloadUrl del response */;
    var bytes = await httpClient.GetByteArrayAsync(presignedUrl, ct); // GET directo a MinIO
    return Result.Success(bytes);
}
```

Para **borrar**:

```csharp
public async Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct)
{
    var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
    var response = await httpClient.DeleteAsync($"storage/files/{fileId}", /* Bearer token */);
    // 404 tambien cuenta como éxito (ya no existe = objetivo cumplido)
    return (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        ? Result.Success()
        : Result.Failure(...);
}
```

#### Paso 5 — Registrar todo en DI

```csharp
services.AddOptions<ServiceAuthClientOptions>().Bind(config.GetSection(ServiceAuthClientOptions.SectionName));
services.AddOptions<CloudStorageClientOptions>().Bind(config.GetSection(CloudStorageClientOptions.SectionName));
services.AddOptions<TuServicioMinioOptions>().Bind(config.GetSection(TuServicioMinioOptions.SectionName));

services.AddHttpClient<IServiceTokenAcquirer, ServiceTokenAcquirer>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptions<ServiceAuthClientOptions>>().Value;
    http.BaseAddress = new Uri(NormalizeBaseUrl(opt.AuthBaseUrl));
});

services.AddSingleton<IMinioClient>(sp =>
{
    var opt = sp.GetRequiredService<IOptions<TuServicioMinioOptions>>().Value;
    var builder = new MinioClient().WithEndpoint(opt.Endpoint).WithCredentials(opt.AccessKey, opt.SecretKey);
    if (opt.UseTls) builder = builder.WithSSL();
    return builder.Build();
});

services.AddHttpClient<ITuServicioCloudStorageClient, TuServicioCloudStorageClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptions<CloudStorageClientOptions>>().Value;
    http.BaseAddress = new Uri(NormalizeBaseUrl(opt.BaseUrl));
});
```

No te olvides del paquete NuGet: `Minio` (v7.0.0) y `Microsoft.Extensions.Http`
en el `.csproj` de Infrastructure.

#### Paso 6 — docker-compose.yml

Agregá al bloque `environment` de tu servicio (mirá `customer-api` como
plantilla completa):

```yaml
CloudStorageClient__BaseUrl: http://cloudstorage-api:8080
ServiceAuthClient__AuthBaseUrl: http://auth-api:8080
ServiceAuthClient__ClientId: ${TU_SERVICIO_SERVICE_CLIENT_ID:-<tu-servicio>-worker}
ServiceAuthClient__ClientSecret: ${TU_SERVICIO_SERVICE_CLIENT_SECRET:-}
TuServicio__Minio__Endpoint: minio:9000
TuServicio__Minio__UseTls: "false"
TuServicio__Minio__AccessKey: <tu-servicio>-worker
TuServicio__Minio__SecretKey: ${TU_SERVICIO_MINIO_SECRET:?Set TU_SERVICIO_MINIO_SECRET in .env}
TuServicio__Minio__TempBucket: taxvision-temp
TuServicio__Minio__SourcePrefix: <tu-servicio>
```

Y agregá `minio` como dependencia (`depends_on: minio: condition: service_healthy`)
si tu servicio no lo tenía ya.

#### Paso 7 — .env local + GitHub Actions

Ya lo mencionamos en los pasos 1 y 2, pero repetimos porque **es el error más
común**: agregar el secreto en `.env` y `docker-compose.yml` y **olvidarse**
de `.github/workflows/deploy.yml`. Si eso pasa, todo funciona en tu laptop
pero el deploy a producción rompe porque `docker-compose` exige la variable
(`:?Set ... in .env`) y el workflow nunca la escribió. Chequealo así:

```bash
grep -n "TU_SERVICIO_MINIO_SECRET\|TU_SERVICIO_SERVICE_CLIENT" .env deploy/docker/docker-compose.yml .github/workflows/deploy.yml
```

Las 3 búsquedas tienen que dar resultado.

#### Paso 8 — (Solo Caso A2) Esperar el escaneo antes de leer

Si necesitás **leer el archivo de vuelta** después de subirlo, no podés
descargarlo inmediatamente — CloudStorage todavía lo está escaneando
(ClamAV + política de contenido). Necesitás un **consumer** que reaccione
cuando el escaneo termine:

1. En `Program.cs`, agregá una cola propia bindeada al fanout compartido:

   ```csharp
   options.ListenToRabbitQueue("<tu-servicio>-events", queue =>
   {
       queue.BindExchange("taxvision-events", string.Empty);
   }).UseDurableInbox();
   ```

2. Escribí un handler estático (Wolverine lo descubre solo por convención de
   nombre/firma):

   ```csharp
   public static class ImportFileScanResultConsumer // o el nombre que corresponda
   {
       public static async Task Handle(
           FileAvailableIntegrationEvent msg,
           ITuRepository repo, IMessageBus bus, CancellationToken ct)
       {
           var miEntidad = await repo.GetByIdAsync(msg.FileId, ct); // FileId == tu propio Id, ver Paso 4
           if (miEntidad is null || miEntidad.TenantId != msg.TenantId) return; // no es tuyo, ignorar
           // acá disparás lo que tengas que hacer (encolar tu propio worker, etc.)
       }

       public static async Task Handle(
           FileInfectedDetectedIntegrationEvent msg,
           ITuRepository repo, IUnitOfWork uow, CancellationToken ct)
       {
           // marcá tu entidad como fallida — el archivo no pasó el antivirus
       }

       public static async Task Handle(
           FileBlockedByPolicyIntegrationEvent msg,
           ITuRepository repo, IUnitOfWork uow, CancellationToken ct)
       {
           // marcá tu entidad como fallida — bloqueado por política de contenido
       }
   }
   ```

   Este mismo evento fluye por la cola compartida para **todos** los archivos
   de **todos** los servicios — por eso el primer chequeo (`miEntidad is null`)
   es obligatorio: si no matchea nada tuyo, simplemente no es tu archivo.

### 2.3 Checklist final — Caso A

- [ ] Policy JSON en `deploy/docker/minio/policies/<servicio>-source.json`
- [ ] Línea `provision "..."` en `provision-service-accounts.sh`
- [ ] Secreto MinIO en `.env` **+** `docker-compose.yml` **+** `deploy.yml` **+** GitHub Secret
- [ ] Cliente M2M en `ServiceAuth:Clients` (Auth) — docker-compose **y** user-secrets locales
- [ ] Permisos M2M acotados a lo que realmente usás (menor privilegio)
- [ ] `TuServicioMinioOptions` + `ServiceAuthClientOptions` + `CloudStorageClientOptions`
- [ ] Cliente Upload/Download/Delete registrado en DI (`AddHttpClient`, `IMinioClient` singleton)
- [ ] Paquetes NuGet `Minio` + `Microsoft.Extensions.Http`
- [ ] docker-compose.yml: env vars del servicio + `depends_on: minio`
- [ ] (Si Caso A2) Consumer de `FileAvailable`/`FileInfectedDetected`/`FileBlockedByPolicy`
- [ ] `dotnet build` + tests verdes antes de dar por terminado

### 2.4 Errores comunes

- **Usar las credenciales root de MinIO** en vez de una cuenta scoped —
  cualquier bug en tu servicio podría leer/borrar archivos de *todos* los
  tenants. Siempre cuenta propia con policy de un solo prefijo.
- **Olvidarse de `deploy.yml`** — funciona local, rompe en producción (ver
  Paso 7).
- **Descargar inmediatamente después de subir** (sin esperar el evento) en
  Caso A2 — vas a fallar porque el archivo todavía no está en el bucket
  final.
- **Content-Type/extensión no permitidos**: cada `FolderType` tiene una
  whitelist de extensiones/tamaño (`CloudStorageOptions.FolderTypePolicies`)
  — si tu archivo no encaja (ej. mandás un `.exe`), el escaneo lo va a
  bloquear igual aunque la subida a MinIO haya sido exitosa. Revisá la
  policy del `FolderType` que uses antes de asumir que "ya está".
- **Darle de más permisos al cliente M2M** — si tu servicio nunca borra,
  no le des `cloudstorage.file.delete`.

---

## 3. Caso B — Directo externo (frontend habla directo con CloudStorage)

### 3.1 Cuándo usar este patrón

Cuando el archivo lo tiene el usuario en su navegador y **no hay ninguna
razón de negocio** para que tu backend lo toque primero. Ejemplo real:
adjuntos de chat en Communication — el usuario adjunta un archivo al mensaje,
el navegador se lo manda directo a CloudStorage, y Communication solo se
entera después (vía evento) para actualizar el estado visual del adjunto.

Ventajas: cero código de subida en tu servicio, cero carga de red extra en tu
backend, el usuario sube más rápido (un salto menos).

### 3.2 Diagrama de flujo

```
┌───────────┐  1. POST /storage/files/uploads         ┌─────────────┐
│ Frontend  │ ────────────────────────────────────────▶│ CloudStorage│
│ (JWT del  │  (con su propio JWT, NO M2M)              │             │
│  usuario) │◀── URL presignada + fileId ───────────────│             │
│           │                                            └─────────────┘
│           │  2. PUT directo a la URL presignada       ┌───────┐
│           │ ──────────────────────────────────────────▶│ MinIO │
│           │                                            └───────┘
│           │  3. POST /storage/files/{id}/complete     ┌─────────────┐
│           │ ────────────────────────────────────────▶│ CloudStorage│
└───────────┘                                            └──────┬──────┘
                                                                 │ escanea,
┌──────────────┐  4. Consume FileAvailable/Infected/...         │ publica evento
│ Tu servicio  │◀────────────────────────────────────────────────┘
│ (solo        │  (actualiza estado de TU entidad, ej. el mensaje de chat)
│  escucha)    │
└──────────────┘
```

### 3.3 Paso a paso

1. **El frontend, con el JWT normal del usuario logueado** (no M2M — el
   usuario ya está autenticado), llama directo a los endpoints públicos de
   CloudStorage:
   - `POST {{UrlBase}}/storage/files/uploads` (o
     `.../uploads/initiate-multipart` si el archivo es grande) — permiso
     `cloudstorage.file.upload`. Devuelve `fileId` + URL(s) presignada(s).
   - El navegador hace el `PUT` directo a la(s) URL(s) de MinIO.
   - `POST {{UrlBase}}/storage/files/{fileId}/complete` (o
     `.../complete-multipart`) — confirma la subida.
2. **Tu backend no interviene en nada de lo anterior.** Solo necesita:
   - Saber qué `fileId` le corresponde a qué entidad tuya — normalmente el
     frontend te lo manda en el mismo request donde crea "el mensaje"/"el
     adjunto"/lo que sea (ej. `POST /chat/messages { text, attachmentFileId }`).
   - Un **consumer** igual al del Paso 8 del Caso A2 (`FileAvailable`,
     `FileInfectedDetected`, `FileBlockedByPolicy`) para reaccionar cuando
     CloudStorage confirma o rechaza el archivo — por ejemplo, marcar el
     adjunto como "disponible" o "bloqueado" en tu propia tabla.
3. **No hace falta**: cuenta de MinIO propia, cliente M2M, `IMinioClient`,
   `ServiceAuthClient`, nada de eso — tu servicio nunca le habla a MinIO ni a
   CloudStorage por HTTP en el sentido de subir/bajar. Solo escucha eventos.

### 3.4 Checklist — Caso B

- [ ] El frontend usa el JWT normal del usuario (no hace falta nada M2M)
- [ ] Tu backend guarda la relación `fileId ↔ tu entidad` en el mismo request
      donde el frontend te informa que ya subió
- [ ] Consumer de `FileAvailableIntegrationEvent` /
      `FileInfectedDetectedIntegrationEvent` /
      `FileBlockedByPolicyIntegrationEvent` (misma cola/patrón que Caso A2)
- [ ] Cero credenciales de MinIO, cero cliente M2M — si te encontrás
      escribiendo eso para este caso, probablemente sea el patrón equivocado

---

## 4. Tabla comparativa

| | Caso A (Interno) | Caso B (Directo externo) |
|---|---|---|
| ¿Quién sube los bytes? | Tu backend | El navegador del usuario |
| ¿Necesitás cuenta MinIO propia? | Sí | No |
| ¿Necesitás cliente M2M? | Sí (si leés/borrás) | No |
| ¿Necesitás consumer de eventos? | Solo si leés de vuelta (A2) | Sí, siempre |
| Carga en tu servicio | Sube/baja los bytes él mismo | Cero — solo escucha eventos |
| Ejemplo real | Signature, Notification, Customer, CommunicationTranscriptWorker | Communication (chat) |

---

## 5. Referencia rápida

### Enums que tenés que usar (definidos en `CloudStorage.Domain`)

```csharp
public enum OwnerType { Tenant, Customer, User, Signature, Invoice, Communication }

public enum FolderType
{
    Documents, Receipts, Invoices, EmailIncoming, EmailOutgoing, Tasks,
    Signatures, Avatars, Imports, Recordings, Backups, Other
}
```

Cada `FolderType` tiene su propia whitelist de extensiones/tamaño máximo en
`CloudStorageOptions.FolderTypePolicies` — revisala antes de asumir que tu
tipo de archivo va a pasar el escaneo.

### Endpoints de CloudStorage relevantes

| Endpoint | Quién lo llama | Permiso |
|---|---|---|
| `POST storage/files/uploads` | Frontend (Caso B) | `cloudstorage.file.upload` |
| `POST storage/files/uploads/initiate-multipart` | Frontend (Caso B, archivos grandes) | `cloudstorage.file.upload` |
| `POST storage/files/{id}/complete` / `complete-multipart` | Frontend (Caso B) | `cloudstorage.file.upload` |
| `POST storage/files/{id}/download-url` | Tu backend (Caso A, M2M) | `cloudstorage.file.download` |
| `DELETE storage/files/{id}` | Tu backend (Caso A, M2M) | `cloudstorage.file.delete` |

### Grant M2M (client-credentials)

```
POST {{AuthBaseUrl}}/auth/service-token
{ "clientId": "...", "clientSecret": "...", "tenantId": "..." }

→ { "accessToken": "...", "expiresInSeconds": 600, "tokenType": "Bearer" }
```

Token de vida corta (~10 min por default) — cacheá con margen (ej. renovar
30s antes de expirar), no pidas uno nuevo en cada llamada.

### Eventos de CloudStorage que podés consumir

- `FileAvailableIntegrationEvent` — el archivo pasó el escaneo, ya está listo.
- `FileInfectedDetectedIntegrationEvent` — antivirus lo marcó infectado.
- `FileBlockedByPolicyIntegrationEvent` — bloqueado por política de contenido.
- `FilePendingReviewIntegrationEvent` — verdict incierto, requiere revisión
  humana (no disparado hoy, el content scanner es un no-op).
- `FileDeletedIntegrationEvent` / `FileRestoredIntegrationEvent` — soft-delete
  / restauración desde la papelera.

Todos viajan por el mismo exchange fanout `taxvision-events` — tu cola
recibe **todo**, filtrás por si te corresponde mirando el `FileId`/`TenantId`.

---

## 6. Archivos de referencia en el repo (para copiar-pegar)

**Caso A** (elegí el que más se parezca a tu situación):

- `src/Services/Signature/TaxVision.Signature.Infrastructure/Sealing/HttpClients/SignatureCloudStorageClient.cs` — A1 (fire-and-forget, sube el PDF sellado).
- `src/Services/Customer/TaxVision.Customer.Infrastructure/Imports/CustomerImportCloudStorageClient.cs` — A2 (sube y después lee de vuelta para parsear filas).
- `src/Services/Customer/TaxVision.Customer.Application/Imports/Consumers/ImportFileScanResultConsumer.cs` — el consumer del Paso 8.
- `src/Services/CommunicationTranscriptWorker/src/cloudstorage/minio-uploader.ts` — mismo patrón A1 pero en Node/TS.

> **Nota (18 de julio de 2026)**: el ejemplo que citaba acá
> `InboundAttachmentStorageWriter.cs` de Notification se eliminó — era parte
> del módulo de conexión/sync de mailbox propio de Notification, retirado
> por duplicar lo que `Connectors` (conexión OAuth + sync) y
> `Correspondence` (inbox filtrado por customer) ya hacen mejor. Si
> necesitás un ejemplo A1 de adjuntos entrantes hoy, mirá cómo
> `Correspondence` descarga y sube attachments de mensajes recibidos
> (`DownloadAttachmentHandler.cs` en `TaxVision.Correspondence.Application`).

**Caso B**:

- `src/Services/Communication/src/application/event-handlers/cloudstorage-consumers.ts` — consumer de los 3 eventos, sin ningún cliente MinIO propio.

**Infraestructura**:

- `deploy/docker/minio/provision-service-accounts.sh` + `deploy/docker/minio/policies/*.json`
- `deploy/docker/docker-compose.yml` (bloques `signature-api`, `notification-api`, `customer-api`, `minio-provision`, `auth-api`)
- `src/BuildingBlocks/Messaging/CloudStorageIntegrationEvents/CloudStorageIntegrationEvents.cs` — contratos de todos los eventos.

---

*Última actualización: 14 de julio de 2026, tras migrar el import de Customer a este patrón.*
