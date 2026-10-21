# Dropbox en CloudStorage

## Crear y configurar la aplicación de Dropbox

1. En la Dropbox App Console crea una aplicación **Scoped access** con acceso **App folder**. No elijas **Full Dropbox**.
2. En la pestaña **Permissions**, habilita `account_info.read` y `files.content.write` y guarda los cambios.
3. En **Settings**, registra este OAuth redirect URI exacto para desarrollo local:
   `http://localhost:5047/storage/providers/dropbox/callback`
4. Copia **App key** y **App secret**. TaxVision solicita acceso sin conexión para renovar el token; guarda el refresh token cifrado por tenant.

Los archivos nuevos y los respaldos se escriben en la carpeta de aplicación aislada de Dropbox. Conectar no migra archivos existentes. Para limitar el acceso a esa carpeta, la app debe ser de tipo **App folder**.

## Desarrollo local con User Secrets

Desde la raíz de `TaxVsion_BackEnd`:

```powershell
$api = '.\src\Services\CloudStorage\TaxVision.CloudStorage.Api\TaxVision.CloudStorage.Api.csproj'
dotnet user-secrets set 'CloudStorage:Dropbox:ClientId' '<App key>' --project $api
dotnet user-secrets set 'CloudStorage:Dropbox:ClientSecret' '<App secret>' --project $api
dotnet user-secrets set 'CloudStorage:Dropbox:RedirectUri' 'http://localhost:5047/storage/providers/dropbox/callback' --project $api
dotnet user-secrets set 'CloudStorage:Dropbox:FrontendReturnUri' 'http://localhost:4200/storage' --project $api
```

Reinicia CloudStorage después de guardar los secretos. No guardes el App secret en `appsettings.json` ni en Git.

Para Docker, configura `DROPBOX_CLIENT_ID`, `DROPBOX_CLIENT_SECRET`, `DROPBOX_REDIRECT_URI` y `DROPBOX_FRONTEND_RETURN_URI` en el `.env` del backend. El `docker-compose.yml` los pasa a CloudStorage.

## Comportamiento

- Solo TenantAdmin y PlatformAdmin con `cloudstorage.settings.manage` pueden conectar o desconectar la cuenta.
- OAuth solicita `account_info.read` para mostrar la cuenta y `files.content.write` para guardar copias. TaxVision nunca recibe la contraseña de Dropbox.
- La interfaz comparte el selector genérico de uso: CRM, proveedor para archivos nuevos, ambas copias o respaldos programados.
- Las copias se crean después del análisis antivirus. MinIO sigue siendo la fuente interna y conectar Dropbox no hace backfill.
- Dropbox recibe archivos de hasta 150 MiB mediante una carga directa y los archivos mayores mediante sesiones de carga en bloques. El ID remoto y checksum se guardan para evitar copias repetidas.
- Desconectar elimina el token cifrado de TaxVision, pero no borra archivos de Dropbox ni revoca el consentimiento de la cuenta.
