# FinancialInsights: registro seguro de lotes

Servicio .NET 10 con Domain, Application, Infrastructure y API. `POST /financial/import-batches`
registra entre 1 y 20 archivos previamente subidos a CloudStorage para un cliente seleccionado.
El cuerpo es `{ "customerId": "...", "fileIds": ["..."] }`.

El usuario debe ser staff, tener `financial.import` efectivo y pertenecer a un tenant con
`financial_insights` habilitado. El permiso se concede explícitamente a un rol; no entra en
los roles de sistema por defecto. La policy `[HasPermission]` lee la proyección local de
permisos efectivos (`perm_v`) y el gate consulta la proyección local de entitlements. Auth
publica los permisos después de aplicar los denies por usuario; los eventos de rol nunca
reconstruyen los permisos del usuario. Si la proyección del usuario aún no existe, el
servicio recupera un snapshot por M2M desde Auth. La API consulta además a Auth con el token
del usuario para confirmar el módulo y el permiso en este corte, verifica
su acceso a `GET /customers/{id}` y consulta `GET /storage/files/{id}` para cada archivo.
Por tanto también necesita `customers.view` y `cloudstorage.file.view` efectivos. Solo
acepta clientes activos del mismo tenant y archivos del mismo cliente con estado `Available`, tipo detectado PDF, PNG, JPEG,
CSV o XLSX y tamaño máximo 25 MiB. Una carga aún en escaneo devuelve 422; se reintenta
el registro cuando CloudStorage marque todos los archivos como `Available`. La unicidad
`(TenantId, FileId)` evita registrar un mismo archivo en varios lotes del tenant.

El servicio persiste metadatos del lote, no copia el archivo a otra ubicación. Los eventos
de CloudStorage se consumen con inbox durable y transiciones idempotentes. Un worker dentro
de la API toma documentos `Ready`, vuelve a comprobar que el cliente y el módulo sigan
activos y que CloudStorage marque el archivo `Available`, y lo descarga mediante una URL
firmada con host permitido. Lee CSV y XLSX localmente; en PDF usa `pdftotext` y aplica
Tesseract a las páginas sin texto suficiente. En PNG/JPEG aplica Tesseract. Envía a OpenAI
el texto extraído y únicamente adjunta imágenes de las páginas cuyo OCR local tenga baja
confianza. La API persiste los movimientos como sugerencias con `RequiresReview=true` y
los devuelve en `GET /financial/import-batches/{batchId}` tras verificar de nuevo los
permisos y el acceso al cliente. No calcula impuestos ni expone gráficos todavía. No se
debe presentar el resultado como asesoría fiscal ni declaración lista para presentar.

Las proyecciones propias incluyen usuario/permisos, rol/permisos, módulos del tenant,
estado del tenant y directorio mínimo de clientes. Los consumidores usan el tenant del
evento y descartan revisiones antiguas. La proyección de clientes se alimenta por eventos
y se completa para un cliente al registrar su primer lote; Customer sigue decidiendo la
visibilidad y el estado vigente antes de aceptar una importación.

La misma proyección de entitlements guarda `PlanCode`. El `POST` usa la política
`financial.i.import_batch_create` (categoría I): cuota base de 10 lotes por minuto por
usuario y 60 por minuto por tenant. Si `RateLimit:EnforceTierQuotas` está activo, el
lector local resuelve el plan y el catálogo global de `PlanRateLimits` en Subscription
aplica el multiplicador o límite configurado para ese plan/categoría. El contador es
compartido en Redis. Un cambio de entitlements invalida al instante tanto la caché del
plan como la de módulos. Este límite controla peticiones de registro. El worker reserva
como máximo 10 documentos por tenant y mes antes de llamar al modelo; cada documento
solo puede reservarse una vez para evitar reintentos automáticos con coste. También limita
el archivo a 25 MiB, 10 páginas, 3 imágenes de respaldo, 30 000 caracteres de texto y
4 000 tokens de salida por llamada. Guarda los tokens facturados cuando la respuesta llega.
Esta cuota es una protección inicial; no equivale a un presupuesto monetario configurable.

## Configuración y despliegue

Aplicar las migraciones de Auth (`AddFinancialInsightsPermissions`), Subscription
(`AddFinancialInsightsModule`) y FinancialInsights (`InitialFinancialImports`,
`AddFinancialProjections`, `AddPermissionSnapshotState`, `AddFinancialProcessing`) en sus bases respectivas. La API
requiere `ConnectionStrings__Default`, `ConnectionStrings__Redis`, `RabbitMq__Uri`,
`Jwt__PublicKeyPath` y `Services__Auth__BaseUrl`, `Services__Customer__BaseUrl`,
`Services__CloudStorage__BaseUrl`, `SubscriptionClient__BaseUrl` y
`RateLimit__EnforceTierQuotas`. La recuperación inicial de permisos y la lectura del
catálogo de cuotas requieren el mismo
`FINANCIAL_INSIGHTS_SERVICE_CLIENT_SECRET` en `auth-api` y `financial-insights-api`; si falta,
los usuarios sin proyección local son denegados. El worker lee `OpenAI:ApiKey` de User
Secrets en desarrollo o de `FINANCIAL_INSIGHTS_OPENAI_API_KEY` mediante Compose. El modelo
configurado inicialmente es `gpt-5.4-mini`; se puede cambiar en `OpenAI:Model`.
El runner de migraciones aplica las tablas de FinancialInsights solo cuando
`FINANCIAL_INSIGHTS_DB_CONNECTION` tiene valor; el perfil `financial` sigue desactivado
en el despliegue general hasta configurar esa base y la credencial M2M.

### Desarrollo local y User Secrets

El proyecto API usa `UserSecretsId=taxvision-financialinsights-development`. En esta
máquina se configuró `ConnectionStrings:Default` reutilizando servidor y credenciales
de desarrollo de Connectors, pero apuntando a la base propia `TaxVision_FinancialInsights`.
También se copiaron las conexiones locales de Redis y RabbitMQ. `appsettings.Development.json`
aporta URLs internas y la ruta de la clave pública JWT; el perfil de lanzamiento activa
`Development`. La fábrica de EF lee `ConnectionStrings:Default` desde los mismos User
Secrets si no se proporcionan variables de entorno. Las cuatro migraciones de FinancialInsights
se aplicaron a esta base local antes de alinear la conexión con Connectors; SQL Server debe
estar en marcha para volver a verificar ese estado con la configuración actual.

La clave del proyecto OpenAI se guarda como `OpenAI:ApiKey` en estos User Secrets.
Sin la clave, el worker permanece inactivo; no se debe pegar en `appsettings.json`
ni en Angular. User Secrets funciona para la ejecución local en `Development`; los
contenedores usan variables o un gestor de secretos separado. Desde la raíz del repo,
este comando de PowerShell pide la clave sin mostrarla y la entrega al almacén por stdin:

```powershell
$key = Read-Host 'Pega la API key de OpenAI' -AsSecureString; @{ 'OpenAI:ApiKey' = [System.Net.NetworkCredential]::new('', $key).Password } | ConvertTo-Json -Compress | dotnet user-secrets set --project 'src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj'; Remove-Variable key
```

El servicio está en el perfil Compose `financial`; activar con una base aislada
`FINANCIAL_INSIGHTS_DB_CONNECTION`. El gateway expone `/financial/{**catch-all}`. El módulo
es comercial sin precio ni inclusión por defecto en planes: el operador debe habilitarlo
para el tenant antes de usar la API.

```powershell
.\deploy\docker\compose.ps1 local --profile financial up -d --build financial-insights-api
```

## Verificación

```powershell
dotnet build src/Services/FinancialInsights/TaxVision.FinancialInsights.Api/TaxVision.FinancialInsights.Api.csproj
dotnet test deploy/tests/TaxVision.FinancialInsights.Tests/TaxVision.FinancialInsights.Tests.csproj
```

El build y las pruebas no sustituyen una prueba completa con Auth, Customer, CloudStorage,
SQL Server, RabbitMQ y la API de OpenAI en ejecución. Las cuatro migraciones de
FinancialInsights se aplicaron a la base local `TaxVision_FinancialInsights` antes del
ajuste de User Secrets. Para OCR de
imágenes en la máquina local se necesita Tesseract; la imagen Docker lo instala junto con
Poppler. Sin una API key real no se ejecutó una llamada al modelo.
