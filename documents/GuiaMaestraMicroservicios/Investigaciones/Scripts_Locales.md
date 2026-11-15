# Scripts PowerShell de la flota local

> Investigación de solo lectura, 2026-11-15. Rutas relativas a `C:\Users\devcacg\Desktop\Proyectos\TaxVsion_BackEnd`. Etiquetas: **[Código verificado]**, **[Doc. histórica]**, **[Recomendación]**, **[Decisión pendiente]**. No se ejecutó ningún script.

## 1. Estado actual

En local los servicios .NET **no corren en Docker**: corren como procesos `dotnet <dll>` desde `bin\Release\net10.0`, con `ASPNETCORE_ENVIRONMENT=Development`. Docker se usa solo para la infraestructura (RabbitMQ, Redis, MinIO, ClamAV, content-scanner). SQL Server es la instancia local de la máquina. La configuración de cada servicio sale de sus **user-secrets**, no del `.env`. **[Código verificado]** + **[Doc. histórica]** `memory/feedback_dev_local_connection_string_viene_de_user_secrets.md`

| Script | Qué hace |
|---|---|
| `scripts/start-fleet.ps1` (129 líneas) | Arranca content-scanner (Docker), Auth primero, el resto escalonado, Gateway al final, y los 3 Node |
| `scripts/stop-fleet.ps1` (63) | Mata por puerto y por línea de comando bajo el repo, con árbol de procesos |
| `scripts/restart-down.ps1` (113) | Relanza solo los que no escuchan |
| `scripts/migrate-local.ps1` (366) | Detecta y aplica migraciones EF y Prisma pendientes en local |
| `scripts/codegen/regenerate-wolverine-code.ps1` (42) | Regenera el código pregenerado de Wolverine |
| `scripts/generate-dev-keys.ps1` / `.sh` | Par JWT RS256 de desarrollo en `dev-keys/` |
| `deploy/docker/compose.ps1` (57) | Wrapper de compose con el `.env` de la raíz (modos full/local/infra) |
| `scripts/sg-sim.ps1`, `scripts/verify-payment-provider-config.ps1` | Herramientas puntuales (no leídas en detalle) |

## 2. Evidencia

### 2.1 `start-fleet.ps1` **[Código verificado]**

- Raíz del repo = padre de `scripts/` (4): sirve en cualquier máquina.
- Fija `Wolverine__CodeGeneration__TypeLoadMode = "Static"` (6-7), igual que producción. Doble guion bajo.
- Tabla `$fleet` (8-40): nombre `n`, carpeta `d`, puerto `p`, espera opcional `w`, variables extra `e`.
- `Start-One` (45-65): busca `TaxVision.<n>.Api.dll` o `TaxVision.<n>.dll` en `bin\Release\net10.0`; si no hay dll imprime `SKIP` (no compila). Fija `ASPNETCORE_ENVIRONMENT=Development` y `ASPNETCORE_URLS=http://localhost:<p>`, redirige a `logs\<n>.log` y `logs\<n>.err.log`, ventana oculta. Las variables `e` se limpian después del lanzamiento.
- Paso 0 (76-86): `docker compose -f deploy\docker\docker-compose.local-infra.yml --env-file .env up -d content-scanner`.
- Paso 1 (88-91): Auth solo y espera su puerto 45 s ("aplica migraciones y calienta SQL sin competencia").
- Paso 2 (93-101): el resto cada 3 s; Notification espera a que escuche Scribe (`w = 5380`, 19-20).
- Paso 3 (103-128): Node (`npm ci` si falta `node_modules`, `prisma:generate` si hay schema, `npm run dev`).

Puertos locales (fuente de verdad, la lee `DevClientPortDefaultsTests`):

| Servicio | Puerto | Servicio | Puerto |
|---|---|---|---|
| Auth | 5124 | Correspondence | 5400 |
| Tenant | 5217 | Growth | 5410 |
| Customer | 5263 | PaymentClient | 5420 |
| Wallet | 5270 | PaymentApp | 5430 |
| FinancialInsights | 5276 | Notes | 5440 |
| Notification | 5320 | Documents | 5450 |
| CloudStorage | 5330 | Billing | 5460 |
| Signature | 5340 | Sms | 5470 |
| Communication (Node) | 5350 | Catalog | 5480 |
| Subscription | 5360 | Inventory | 5490 |
| CommunicationRecorder (Node) | 5365 | Reminder | 5500 |
| Postmaster | 5370 | Tasks | 5510 |
| Scribe | 5380 | Calendar | 5520 |
| Connectors | 5390 | Campaigns | 5530 |
| Connectors.MailboxListener | 5395 | content-scanner (Docker) | 5540 |
| Gateway | 5047 | TranscriptWorker (Node) | sin puerto |

Los `launchSettings.json` coinciden en los casos revisados (Notes 5440, Wallet 5270, Calendar 5520, Gateway 5047). **[Código verificado]**

### 2.2 `stop-fleet.ps1` **[Código verificado]**

- Lista de puertos (12-15) igual a la de start-fleet; **excluye 5540** porque lo atiende el proxy de Docker Desktop (10-11).
- Además mata procesos cuya línea de comando contiene `bin\Debug|Release\net10.0\TaxVision.*.dll` o `\src\Services\Communication*` bajo el repo (25-37), sin tocar `dotnet build/test`.
- `taskkill /PID /T /F` (44-49) y verifica puertos libres (53-62).

### 2.3 `restart-down.ps1` **[Código verificado]**

- Mismo patrón que start-fleet (Release, Development, logs), pero relanza solo puertos que no escuchan (48-70) y los Node sin proceso (81-107). Al final imprime `flota completa` o `abajo: ...` (109-113).
- Motivo (1-3): el arranque en frío pierde servicios por timeout de SQL (`Error Number:-2`) al migrar las tablas de Wolverine.

### 2.4 `migrate-local.ps1` **[Código verificado]**

- Resuelve la cadena como ASP.NET en Development: user-secrets del Api (`UserSecretsId` del csproj) → `appsettings.Development.json` → `appsettings.json` (82-96). Nunca imprime contraseñas (11).
- Solo toca servidores locales salvo `-AllowRemote` (99-106).
- Descubre servicios solos: busca `*ModelSnapshot.cs` bajo `src\Services\*` y el csproj de Infrastructure más cercano (196-219). No hay lista que mantener.
- Detecta pendientes comparando los `[Migration]` del código con `__EFMigrationsHistory` (sin compilar); `-UseEfToDetect` usa `dotnet ef migrations list`.
- Aplica con `dotnet ef database update --project <Infra> --startup-project <Infra> --context <Ctx>` (348) y Prisma con `npx --no-install prisma migrate deploy` (355-356).
- Uso (13-18): sin parámetros solo informa; `-Apply` pregunta; `-Apply -Yes -Service Calendar,Auth`; `-SkipPrisma`.

### 2.5 `regenerate-wolverine-code.ps1` **[Código verificado]**

- Lista fija `$all` de 26 servicios (12-16).
- Pone `TAXVISION_CODEGEN_WRITE=1` y `TypeLoadMode=Dynamic` (21-24) y corre, por servicio, `dotnet test deploy\tests\TaxVision.<s>.Tests\... --filter "FullyQualifiedName~PreGeneratedHandlerCodeTests.Pre_generated_code_matches_the_current_handlers"` (26-31). La prueba de frescura reescribe `Internal/Generated` del Api.

### 2.6 Release vs Debug

- La flota arranca desde **Release** (`start-fleet.ps1:47-50`, `restart-down.ps1:52`): Debug puede estar viejo y además se bloquea (MSB3027) al compilar con el proceso vivo. **[Código verificado]**
- Con la flota arriba, al tocar un servicio: detenerlo por puerto, `dotnet build -c Release`, relanzarlo como start-fleet y comprobar `/health/ready`; un 502 del gateway suele ser un puerto caído. **[Doc. histórica]** `memory/feedback_reiniciar_servicio_tocado_release.md`

### 2.7 User-secrets locales

- 27 proyectos Api/Gateway declaran `<UserSecretsId>`. **[Código verificado]** (grep sobre `*.Api.csproj` y `TaxVision.Gateway.csproj`)
- Claves típicas: `ConnectionStrings:Default`, `ConnectionStrings:Redis`, `RabbitMq:Uri`, `Jwt:*`, `Encryption:MasterKey`, `ServiceAuthClient:*`, `<Svc>:Minio:*`. **[Doc. histórica]** `Implementaciones/DEVS/Guia_Creacion_Microservicio.md` §10.1; `README.md:565` ("User Secrets").
- El Gateway en Development lee la clave pública de `../../../dev-keys/jwt-public.pem` y Tenant en `http://localhost:5217` (`src/Gateway/TaxVision.Gateway/appsettings.Development.json:8-13`). **[Código verificado]**

## 3. Problemas

1. **`restart-down.ps1` no fija `Wolverine__CodeGeneration__TypeLoadMode=Static`** (start-fleet sí, línea 7). Un servicio relanzado por restart-down queda en Dynamic (el default de Development) y no reproduce producción. **[Código verificado]**
2. **`restart-down.ps1` no espera a Scribe antes de Notification** (sin `w`, 13), al revés que start-fleet (19-20). **[Código verificado]**
3. **Tres listas de puertos duplicadas** (start, stop, restart) más la de codegen: un servicio nuevo hay que agregarlo en 4 sitios. Solo `start-fleet.ps1` está protegido por prueba (`DevClientPortDefaultsTests`). **[Código verificado]**
4. **start-fleet toca Docker** (paso 0). Las reglas de agentes prohíben tocar Docker; la memoria indica usar una copia sin ese paso si el 5540 ya escucha. **[Doc. histórica]** `memory/feedback_codegen_static_variable_doble_guion.md`
5. **El script no compila**: si falta el dll en Release imprime `SKIP` y sigue. **[Código verificado]**
6. **Codificación**: `start-fleet.ps1` está en cp1252 y conviene escribir solo ASCII. **[Doc. histórica]** misma memoria.
7. `DevClientPortDefaultsTests.cs:22-23` dice que Communication está "fuera de start-fleet.ps1", pero el script sí lo arranca (103-108). Comentario desfasado. **[Código verificado]**

## 4. Buenas prácticas ya vigentes

- Arranque escalonado (evita la avalancha de SQL) y Auth primero.
- Logs por servicio en `logs\<n>.log` / `.err.log`.
- `migrate-local` descubre servicios y no imprime secretos; solo local por defecto.
- Static en la flota local, igual que producción.

## 5. Recomendación

- **[Recomendación]** La guía debe listar los 4 archivos a tocar al dar de alta un servicio (start, stop, restart, codegen) y el orden: compilar Release antes de arrancar.
- **[Decisión pendiente]** Igualar `restart-down.ps1` con start-fleet (Static y espera de Scribe). Es un cambio de script, fuera de esta investigación.
- **[Decisión pendiente]** Una sola tabla de flota compartida por los tres scripts.

## 6. Riesgos

- Probar contra binarios viejos (Debug o Release sin recompilar) muestra comportamiento antiguo.
- Arranque en frío: varios servicios caen por timeout SQL `-2`; se relanzan con `restart-down.ps1`, no es un bug del cambio. **[Doc. histórica]**
- Migración sin aplicar: el `Invalid column` tarda 21-190 s, infla el p99 del gateway y dispara load shedding (503 en toda la flota). **[Doc. histórica]** `memory/feedback_migration_drift_causes_gateway_load_shedding_cascade.md`
- Leer la cadena del `.env` en vez de user-secrets apunta a otra base (casi se borraron datos reales). **[Doc. histórica]**

## 7. Pruebas

- `DevClientPortDefaultsTests` compara los `BaseUrl` por defecto de los clientes M2M con los puertos de `start-fleet.ps1`. **[Código verificado]** `deploy/tests/TaxVision.BuildingBlocks.Tests/Deploy/DevClientPortDefaultsTests.cs:27`
- La regeneración de codegen usa `PreGeneratedHandlerCodeTests` (ver `Testing_EDA.md`).

## 8. Dependencias

- Infra Docker local (`docker-compose.local-infra.yml`), SQL Server local, user-secrets por servicio, `dev-keys/`.
- Gateway en 5047 con clusters `http://localhost:<puerto>` (`appsettings.json` de Gateway).

## 9. Material para la guía

### 9.1 Alta de un servicio en la flota local (pasos reales)

1. Elegir un puerto libre de la tabla de `start-fleet.ps1` (múltiplos de 10 entre 5400 y 5540 están casi todos tomados; revisar 5540 = content-scanner).
2. Fijarlo en `src/Services/<Svc>/TaxVision.<Svc>.Api/Properties/launchSettings.json`.
3. Agregar la línea en `scripts/start-fleet.ps1` (antes de Gateway):
   ```powershell
   @{ n = "<Svc>"; d = "src\Services\<Svc>\TaxVision.<Svc>.Api"; p = <puerto> },
   ```
   Si depende de otro al arrancar: `w = <puerto del otro>`. Si necesita variables: `e = @{ "Clave__Sub" = "valor" }`.
4. Misma línea en `scripts/restart-down.ps1` y el puerto en `$ports` de `scripts/stop-fleet.ps1`.
5. Agregar `"<Svc>"` a `$all` de `scripts/codegen/regenerate-wolverine-code.ps1`.
6. Cluster del gateway en `src/Gateway/TaxVision.Gateway/appsettings.json` con `http://localhost:<puerto>/` (ver `Gateway_YARP.md`).
7. User-secrets del Api:
   ```powershell
   dotnet user-secrets set "ConnectionStrings:Default" "<cadena local>" --project src\Services\<Svc>\TaxVision.<Svc>.Api
   dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project ...
   dotnet user-secrets set "RabbitMq:Uri" "amqp://<user>:<password>@localhost:5672" --project ...
   ```
   Y el cliente M2M en Auth (`ServiceAuth:Clients:<N>:*`) con su contraparte `ServiceAuthClient:*` en el servicio.
8. `dotnet build -c Release` del Api y `./scripts/migrate-local.ps1 -Apply -Service <Svc>`.
9. Correr el gate (`DevClientPortDefaultsTests` valida los puertos).

### 9.2 Comandos de uso diario

```powershell
powershell -ExecutionPolicy Bypass -File scripts\start-fleet.ps1
powershell -ExecutionPolicy Bypass -File scripts\restart-down.ps1
powershell -ExecutionPolicy Bypass -File scripts\stop-fleet.ps1
./scripts/migrate-local.ps1                    # solo informa
./scripts/migrate-local.ps1 -Apply -Service <Svc>
powershell -ExecutionPolicy Bypass -File scripts\codegen\regenerate-wolverine-code.ps1 -Service <Svc>
Get-Content logs\<Svc>.err.log -Tail 50
```

### 9.3 Variable Static

`$env:Wolverine__CodeGeneration__TypeLoadMode = "Static"` (doble guion bajo; la clave es `Wolverine:CodeGeneration:TypeLoadMode`, `src/BuildingBlocks/BuildingBlocks.Web/Hosting/WolverineCodeGenerationExtensions.cs:18,40-42`). Con un solo guion bajo no se lee y Development sigue en Dynamic.
