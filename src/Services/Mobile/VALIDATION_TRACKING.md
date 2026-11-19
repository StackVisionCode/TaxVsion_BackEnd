# Validacion de tracking Mobile — 2026-11-19

Se conservaron las modificaciones locales existentes. Comandos ejecutados desde la raiz del backend, SDK instalado 10.0.303 (global.json 10.0.300 con roll-forward). No se ejecuto npm ni se modifico D:/TaxPro-Milage en esta ampliacion.

## Compilacion, generacion y pruebas

| Comando | Resultado observado |
| --- | --- |
| `dotnet build src/Services/Mobile/TaxVision.Mobile.Api/TaxVision.Mobile.Api.csproj --nologo` | Correcto; 0 errores y 0 advertencias en la compilacion inicial de esta ampliacion, despues de actualizar la firma generada de DeleteVehicle. |
| `powershell -ExecutionPolicy Bypass -File scripts/codegen/regenerate-wolverine-code.ps1 -Service Mobile` | Correcto; generacion estatica Wolverine y comprobacion de frescura aprobadas. |
| `dotnet test deploy/tests/TaxVision.Mobile.Tests/TaxVision.Mobile.Tests.csproj --no-restore --nologo -m:1` | Repeticion final: 58 aprobadas, 0 fallidas, 0 omitidas, 55 segundos. Incluye HTTP/Wolverine/SQL, actualizacion desde la migracion inicial preservando vehiculos y conflicto RowVersion; ejecutada despues de los ultimos cambios internos y de aplicar las migraciones de DEV001. |
| `dotnet ef migrations has-pending-model-changes --no-build --project src/Services/Mobile/TaxVision.Mobile.Infrastructure --startup-project src/Services/Mobile/TaxVision.Mobile.Api` | Correcto; no hay diferencias de modelo. |
| `dotnet ef migrations has-pending-model-changes --no-build --project src/Services/Auth/Infrastructure --startup-project src/Services/Auth/Api` | Correcto; no hay diferencias de modelo. |
| `dotnet csharpier format <archivos C# modificados>` | Aplicado a los archivos de la ampliacion, incluido el catalogo y migracion Auth. |
| `git diff --check` | Correcto; advertencias de normalizacion CRLF/LF de Git, sin errores de espacios. |

La prueba general se ejecuto con `dotnet test TaxVision.slnx --no-restore --nologo -m:1 --filter "FullyQualifiedName!~.Integration.&FullyQualifiedName!~.Persistence.&FullyQualifiedName!~ReminderRetentionJobTests"`. Este filtro excluye integraciones del resto del stack; no equivale a una validacion completa de infraestructura. Resultado final se registra debajo.

## Migraciones aplicadas realmente

Generacion de las dos migraciones con `dotnet ef migrations add AddTripTrackingAndMileageRates` (Mobile) y `dotnet ef migrations add AddMobileTripPermissions` (Auth), sus respectivos proyectos de Infrastructure y Api, y `--output-dir Persistence/Migrations`.

Aplicacion:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/migrate-local.ps1 -Apply -Yes -Service Auth,Mobile -SkipPrisma -AllowRemote
```

El wrapper uso las conexiones existentes de user-secrets, sin imprimir credenciales. Antes y despues se verifico con `SERVERPROPERTY('ServerName')` que ambas conexiones llegan a **DEV001**. La etiqueta localhost de Auth en el reporte corresponde al servidor real DEV001; no se uso la instancia anterior localhost\DEV.

| Base en DEV001 | Migracion aplicada | Historial despues | Pendientes |
| --- | --- | --- | --- |
| TaxVision_Auth | 20261118234048_AddMobileTripPermissions | 152 migraciones | 0 |
| TaxVision_Mobile | 20261118233935_AddTripTrackingAndMileageRates | 2 migraciones | 0 |

Verificacion SQL directa: existen MobileTrips, MobileTripPoints, MobileTripStateChanges y MobileMileageRates, ademas de Vehicles. Auth contiene mobile.trips.read, mobile.trips.manage, mobile.trips.sync, mobile.configuration.read y mobile.rates.manage.

Reconsulta ejecutada:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/migrate-local.ps1 -Service Auth,Mobile -SkipPrisma -AllowRemote
```

Resultado: ambas bases `up to date`; `Nothing to apply.` No se sembraron tarifas arbitrarias ni recorridos ficticios en TaxVision_Mobile. Las pruebas SQL usan bases temporales con nombre propio y las eliminan al finalizar.

## Limites de la verificacion

No se reinicio ni desplego la flota. Para usar estos binarios hay que desplegar/reiniciar Auth, Mobile y Gateway; Auth sincroniza los roles del sistema y puede requerirse renovar sesion. No se verificaron en dispositivo fisico GPS, deteccion de conduccion o segundo plano. Las pruebas HTTP sustituyen transporte RabbitMQ/Redis y autenticacion criptografica por dobles; si verifican autorizacion, aislamiento, contratos, SQL y Wolverine real. La aplicacion movil necesita adaptar contratos, secuencias y reintentos como explica TRACKING_API.md. No hay tasas fiscales precargadas; sin tarifa administrada, la estimacion es null.

## Incidencia de Signature en la suite general

Tres pruebas de arquitectura de Signature fallaron al construir su host porque su configuracion apunta al archivo local ausente `C:/Users/Administrator/AppData/Local/TaxVision/Secrets/signature-signer-jwt.pem`:

- SignatureFailurePolicyTests.NotReadyException_UsesTheDependencyNotReadyPolicy (SealingLockBusyException).
- SignatureFailurePolicyTests.NotReadyException_UsesTheDependencyNotReadyPolicy (SignatureImageNotReadyException).
- LocalQueueDurabilityTests.LocallyRoutedMessages_UseDurableQueues.

El error observado es InvalidOperationException de RsaSigningKeyProvider.LoadFromFile, antes de las aserciones de dichas pruebas. No se modificaron las claves ni la configuracion de Signature como parte de Mobile. Por tanto, la suite general no se declara aprobada. La ejecucion separada final de Mobile si termino con 58/58 aprobadas.

## Resultado final de la suite general

Comando finalizado con codigo **1**: 14478 aprobadas, 3 fallidas y 1 omitida(s) en 31 proyectos de pruebas. Las tres fallidas corresponden a Signature y su archivo PEM ausente, descrito arriba. La prueba omitida requiere Redis real. No hubo errores de compilacion en esta ejecucion; si hubo advertencias del repositorio.

| Proyecto | Aprobadas | Fallidas | Omitidas |
| --- | --- | --- | --- |
| TaxVision.Auth.Tests | 1507 | 0 | 0 |
| TaxVision.Billing.Tests | 258 | 0 | 0 |
| TaxVision.BuildingBlocks.Tests | 1088 | 0 | 1 |
| TaxVision.Calendar.Tests | 483 | 0 | 0 |
| TaxVision.Campaigns.Tests | 237 | 0 | 0 |
| TaxVision.Catalog.Tests | 76 | 0 | 0 |
| TaxVision.CloudStorage.Tests | 766 | 0 | 0 |
| TaxVision.Connectors.Tests | 635 | 0 | 0 |
| TaxVision.ContentScanner.Tests | 20 | 0 | 0 |
| TaxVision.Correspondence.Tests | 618 | 0 | 0 |
| TaxVision.Customer.Tests | 572 | 0 | 0 |
| TaxVision.DataCollection.Engine.Tests | 378 | 0 | 0 |
| TaxVision.Documents.Tests | 178 | 0 | 0 |
| TaxVision.FinancialInsights.Tests | 121 | 0 | 0 |
| TaxVision.Gateway.Tests | 219 | 0 | 0 |
| TaxVision.Growth.Tests | 374 | 0 | 0 |
| TaxVision.Inventory.Tests | 59 | 0 | 0 |
| TaxVision.Mobile.Tests | 51 | 0 | 0 |
| TaxVision.Notes.Tests | 192 | 0 | 0 |
| TaxVision.Notification.Tests | 662 | 0 | 0 |
| TaxVision.PaymentApp.Tests | 594 | 0 | 0 |
| TaxVision.PaymentClient.Tests | 500 | 0 | 0 |
| TaxVision.Postmaster.Tests | 544 | 0 | 0 |
| TaxVision.Reminder.Tests | 115 | 0 | 0 |
| TaxVision.Scribe.Tests | 634 | 0 | 0 |
| TaxVision.Signature.Tests | 849 | 3 | 0 |
| TaxVision.Sms.Tests | 279 | 0 | 0 |
| TaxVision.Subscription.Tests | 948 | 0 | 0 |
| TaxVision.Tasks.Tests | 1047 | 0 | 0 |
| TaxVision.Tenant.Tests | 385 | 0 | 0 |
| TaxVision.Wallet.Tests | 89 | 0 | 0 |
