# OneDrive en CloudStorage

## Registrar la aplicación Microsoft

1. En Microsoft Entra admin center, crea un registro de aplicación compatible con cuentas organizativas y cuentas Microsoft personales.
2. Añade una plataforma **Web** y registra el redirect URI exacto. Para desarrollo local:
   `http://localhost:5047/storage/providers/onedrive/callback`
3. Crea un secreto de cliente y copia su valor en el gestor de secretos de CloudStorage.
4. La conexión solicita permisos delegados `User.Read` y `Files.ReadWrite`, además de `openid`, `profile`, `email` y `offline_access`.

Microsoft Graph no ofrece `Files.ReadWrite.AppFolder` para OneDrive for Business. Por eso el consentimiento delegado `Files.ReadWrite` puede permitir a la aplicación acceder a más archivos del usuario. El código de TaxVision solo escribe archivos creados por TaxVision en la carpeta de aplicación (`Apps/<nombre de la aplicación>`); no enumera, migra ni modifica los archivos preexistentes. Revisa y aprueba el permiso con el administrador de Microsoft 365 de tu organización.

## Desarrollo local con User Secrets

Desde la raíz de `TaxVsion_BackEnd`:

```powershell
$api = '.\src\Services\CloudStorage\TaxVision.CloudStorage.Api\TaxVision.CloudStorage.Api.csproj'
dotnet user-secrets set 'CloudStorage:OneDrive:ClientId' '<Application (client) ID>' --project $api
dotnet user-secrets set 'CloudStorage:OneDrive:ClientSecret' '<client secret value>' --project $api
dotnet user-secrets set 'CloudStorage:OneDrive:RedirectUri' 'http://localhost:5047/storage/providers/onedrive/callback' --project $api
dotnet user-secrets set 'CloudStorage:OneDrive:FrontendReturnUri' 'http://localhost:4200/storage' --project $api
```

Reinicia CloudStorage después de configurar los secretos. No guardes el secreto de cliente ni la clave maestra de cifrado en `appsettings.json` o Git. En Docker, configura `ONEDRIVE_CLIENT_ID`, `ONEDRIVE_CLIENT_SECRET`, `ONEDRIVE_REDIRECT_URI` y `ONEDRIVE_FRONTEND_RETURN_URI` en el `.env` local.

## Comportamiento

- El acceso a conectar, consultar y desconectar está limitado a TenantAdmin/PlatformAdmin con `cloudstorage.settings.manage`.
- OAuth usa estado aleatorio de un solo uso con caducidad y guarda cifrado el refresh token por tenant. TaxVision no recibe ni almacena la contraseña de Microsoft.
- “Usar ambos” y “Solo respaldos” reutilizan la política y el scheduler genéricos de proveedores. La copia se realiza después de que CloudStorage marca el archivo como disponible tras el análisis antivirus; la copia interna MinIO sigue siendo la fuente de verdad y no se hace backfill al conectar.
- Los uploads de Graph se envían en bloques reanudables. Se conserva el identificador remoto y el checksum para evitar copias repetidas; los refresh tokens rotados por Microsoft se guardan cifrados.
- Desconectar elimina el token guardado por TaxVision. Para revocar también el consentimiento OAuth, el usuario o administrador debe retirar el acceso de la aplicación desde Microsoft Entra.
