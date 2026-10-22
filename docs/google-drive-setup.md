# Google Drive en CloudStorage

## Configuración requerida por entorno

1. Habilitar Google Drive API en el proyecto de Google Cloud de TaxVision.
2. Configurar el consentimiento OAuth para la app web y registrar como redirect URI exacta:
   `https://<host-publico-api>/storage/providers/google-drive/callback`
3. Configurar estos secretos/valores en el gestor de secretos del servicio CloudStorage:

```text
CloudStorage__GoogleDrive__ClientId=<OAuth client ID>
CloudStorage__GoogleDrive__ClientSecret=<OAuth client secret>
CloudStorage__GoogleDrive__RedirectUri=https://<host-publico-api>/storage/providers/google-drive/callback
CloudStorage__GoogleDrive__FrontendReturnUri=https://<host-frontend>/storage
Encryption__MasterKey=<base64 de 32 bytes, clave estable del servicio>
```

La clave `Encryption__MasterKey` debe ser la misma entre réplicas y mantenerse estable durante la vida de los tokens cifrados. No se debe guardar en `appsettings.json` ni en Git. En el cliente OAuth se solicitan `openid`, `email` y el scope Drive `drive.file`; TaxVision no recibe ni almacena la contraseña de Google.

## Desarrollo local con User Secrets

Desde la raíz de `TaxVsion_BackEnd`, configura el cliente OAuth de tipo **Web application** en Google Cloud y añade exactamente `http://localhost:5047/storage/providers/google-drive/callback` como URI de redirección autorizada. El usuario que prueba OAuth también debe estar permitido por la pantalla de consentimiento si la app sigue en modo Testing.

```powershell
$api = '.\src\Services\CloudStorage\TaxVision.CloudStorage.Api\TaxVision.CloudStorage.Api.csproj'
dotnet user-secrets set 'CloudStorage:GoogleDrive:ClientId' '<OAuth client ID>' --project $api
dotnet user-secrets set 'CloudStorage:GoogleDrive:ClientSecret' '<OAuth client secret>' --project $api
dotnet user-secrets set 'CloudStorage:GoogleDrive:RedirectUri' 'http://localhost:5047/storage/providers/google-drive/callback' --project $api
dotnet user-secrets set 'CloudStorage:GoogleDrive:FrontendReturnUri' 'http://localhost:4200/storage' --project $api
```

Genera una clave AES aleatoria de 32 bytes una sola vez y guárdala como User Secret. Mantén el mismo valor entre reinicios y mientras haya refresh tokens cifrados:

```powershell
$keyBytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($keyBytes)
$masterKey = [Convert]::ToBase64String($keyBytes)
dotnet user-secrets set 'Encryption:MasterKey' $masterKey --project $api
Remove-Variable masterKey, keyBytes
```

Después reinicia CloudStorage para cargar User Secrets en el proceso Development. No pegues el Client Secret ni la clave maestra en logs o chats.

## Operación implementada

- Solo `TenantAdmin` y `PlatformAdmin`, con `cloudstorage.settings.manage`, pueden iniciar, consultar o desconectar una conexión.
- Estado OAuth aleatorio, de un solo uso y con vencimiento de 10 minutos. El tenant y el usuario salen del JWT al iniciar; no se reciben del navegador en el callback.
- Se cifra el refresh token por tenant. El estado y conexión son filas tenant-scoped.
- Los archivos nuevos se replican mediante carga reanudable de Google Drive después del escaneo antivirus exitoso. La copia interna MinIO continúa siendo la fuente de verdad y el flujo actual de cargas, descargas, permisos, papelera y borrado sigue aplicándose.
- La replicación usa checksum para idempotencia. No se hace backfill de archivos históricos y errores externos reintentan por la política Wolverine existente.
- El permiso OAuth `drive.file` limita el acceso a los archivos creados por la integración; este primer corte no enumera ni modifica archivos personales preexistentes de Google Drive.

Aplicar las migraciones de CloudStorage antes de habilitar el callback en el entorno.
