# Revisión del microservicio Mobile contra la Guía Maestra

**Fecha:** 2026-11-19
**Servicio:** `src/Services/Mobile` (viajes con GPS, millaje y vehículos para la app móvil)
**Commits revisados:** `4aefbbf9` y `75e8924f` (rama `MobileServices`, PR #184), ya integrados en `Develop` (`6f3f8835`)
**Para:** el senior que hará las correcciones
**Estado del código:** sin cambios. Esta revisión fue de solo lectura.

---

## 1. Cómo se revisó

1. **Merge.** Se comparó lo que trajo el merge contra el último commit del dueño (`13a6744b..6f3f8835`).
   - Resultado: fuera del servicio Mobile, todo son líneas agregadas. No se perdió nada de lo que ya existía.
   - Se buscaron Guid de permisos repetidos: no hay.
   - Se confirmó que Auth no tiene cambios de modelo pendientes (`dotnet ef migrations has-pending-model-changes`).
2. **Gate del CI.** Se corrió completo después del merge: **31 proyectos, 14 981 pruebas, 0 fallos**.
   - `VALIDATION_TRACKING.md` menciona 3 fallos de Signature por un PEM local, pero en esta máquina no se reproducen.
3. **Guía Maestra.** Se recorrió ítem por ítem `Checklists/Nuevo_Microservicio.md` (fases 1 a 15).
   - También los checklists a los que remite: `Seguridad.md`, `Testing.md`, `Produccion.md` y `Modulos_Planes.md`.
   - Cuando un ítem no era explícito, se comparó con Notes y FinancialInsights, que ya siguen la guía.
4. **Evidencia.** Cada hallazgo de abajo trae la ruta y la línea, y se verificó en el código. Las rutas son relativas a `TaxVsion_BackEnd`.
5. **Local.** Ya quedaron configurados los user-secrets de Mobile y el cliente `mobile-worker` en Auth. Se aplicaron las 4 migraciones (Auth +2 y la base `TaxVision_Mobile` con 2), y el reporte del script confirma que están al día.

## 2. Veredicto

**Sí siguió la Guía Maestra.** Copió el patrón de Notes y FinancialInsights:
- proyección de permisos con recuperación pull;
- codegen Static con prueba de frescura;
- filtro de tenant fail-closed;
- rate limit en cada acción;
- offboarding;
- Gateway, compose, `deploy.yml` y `apply-migrations.sh`;
- pruebas `.Integration.` separadas.

**Se puede desplegar el backend** si antes se cargan los 3 secrets (ver §6). **No conviene dar la app a clientes reales** hasta resolver R1 (datos GPS) y O2 (revisión del login móvil).

### Resumen de hallazgos

| ID | Gravedad | Tema | Ítem de la guía |
|---|---|---|---|
| R1 | 🔴 Bloquea el lanzamiento a clientes | Los datos GPS no tienen retención y el borrado no los elimina | Sin ítem explícito (ver §7); relacionado con `Produccion.md` §11 |
| O1 | 🟠 Importante | Un secret de Mobile sin cargar tumba todo el deploy, Auth incluido | `Nuevo_Microservicio.md` Fase 12; `Produccion.md:25` |
| O2 | ✅ Resuelto (2026-11-19) | Login y refresh móviles anónimos en Auth: piden revisión de seguridad | `Seguridad.md:26` y `:116` |
| O3 | 🟠 Importante | Comercialización sin decidir contra el módulo `miles` existente | `Nuevo_Microservicio.md` Fase 9; `Modulos_Planes.md:14-15` |
| O4 | 🟠 Importante | Faltan pruebas de seguridad mínimas | `Testing.md:59`, `:128`, `:129`, `:135` |
| O5 | 🟠 Importante | No hay runbook del servicio | `Produccion.md:110-112` |
| M1–M14 | 🟡 Menores | Dominio, zona horaria, configuración, limpieza | Ver §5 |

## 3. Lo que está bien (conservarlo)

- **Aislamiento del cliente.** El cliente del portal solo ve lo suyo: el tenant y el `customer_id` salen del JWT, y lo ajeno responde 404.
  - Código: `TaxVision.Mobile.Infrastructure/Persistence/Repositories/TripRepository.cs:11-16`.
  - Prueba: `TripApiIntegrationTests`.
- **Sin acceso del staff a trayectos.** No existe ningún endpoint para que el staff lea los trayectos de un cliente.
- **Idempotencia.** Funciona por hash de payload y secuencia, con `RowVersion` e índices únicos.
- **Logs.** Los comandos no escriben coordenadas en el log.
- **Tamaño de cuerpo.** Está limitado en la API y en el Gateway (256 KiB en tracking y complete).
- **Permisos.** Tienen Guid propio, sin choques. La guía solo exige un "GUID fijo" (`Checklists/Seguridad.md:32`).
  - Usó la serie `bf063f60-…`, distinta de `a1000000-…-2xx`, pero eso es solo cosmético.
  - **No cambiarlos:** ya están aplicados en bases existentes y moverlos exigiría otra migración.

---

## 4. Hallazgos importantes

### R1 🔴 Los datos GPS no tienen retención y "borrar" un viaje no borra su trayecto

**Cómo se encontró**
- La guía no tiene un ítem de retención para datos personales (ver §7). Salió al revisar la seguridad de los datos de ubicación.
- El propio autor lo dejó pendiente en `src/Services/Mobile/TRACKING_API.md:102`: "la política de retención y purga requiere una definición separada".

**Evidencia**
- Se guarda la ubicación precisa de cada punto: latitud, longitud, precisión, velocidad, altitud y rumbo (`TaxVision.Mobile.Domain/Trips/TripPoint.cs:13-18`).
- El viaje guarda además la última posición (`TaxVision.Mobile.Domain/Trips/Trip.cs:26-27`).
- `Trip.Delete` solo marca `DeletedAtUtc` (`Trip.cs:181-188`).
- La cascada que borraría los puntos (`TripPointConfiguration.cs:46`) nunca se dispara, porque el viaje no se elimina físicamente.
- No existe ningún job de purga.

**Por qué importa**
- Es la ubicación exacta de los clientes finales de cada oficina, y queda guardada para siempre aunque el usuario "borre" el viaje.
- Si un cliente pide que se eliminen sus datos, hoy no se puede cumplir.
- La tabla crece sin límite: un viaje genera cientos de puntos.

**Cómo arreglarlo**
1. **Decisión del dueño** sobre el plazo, por ejemplo: los puntos GPS se conservan N meses; el resumen del viaje (distancia, fecha, propósito, vehículo) se conserva para el reporte de millaje.
2. **Al borrar un viaje**, eliminar físicamente sus `TripPoint` (o anular las coordenadas) en la misma unidad de trabajo que marca `DeletedAtUtc`.
   - Se conserva la fila del viaje y su identificador, que es lo que impide que un reintento lo "resucite".
3. **Job de purga por lotes** con el patrón de `ReminderRetentionJob` / `TaskRetentionJob`:
   - lote acotado;
   - una unidad por transacción;
   - lock distribuido en Redis;
   - `ReconciliationRetry` ante fallo.
   - Purga los puntos de viajes completados más viejos que el plazo y también `LastLatitude`/`LastLongitude`.
4. **Minimizar.** Valorar no guardar `AltitudeMeters` ni `Heading` si ningún cálculo los usa.
5. **Documentar** el plazo en `TRACKING_API.md` y en el runbook (O5).

**Cómo verificar**
- Una prueba de dominio o aplicación: borrar un viaje elimina sus puntos.
- Una prueba `.Integration.` del job: solo purga lo vencido, respeta el lote y es idempotente.
- Una consulta de conteo antes y después en la base local.

---

### O1 🟠 Si falta `MOBILE_SERVICE_CLIENT_SECRET`, se cae el deploy completo (Auth incluido)

**Cómo se encontró**
- Fase 12 de `Nuevo_Microservicio.md` (Docker, compose y secrets) y `Produccion.md:25`.
- Al comparar el bloque `auth-api` del compose con los demás clientes M2M.

**Evidencia** (`deploy/docker/docker-compose.yml`)
- En el bloque `auth-api`:
  - El cliente de Mobile usa `${MOBILE_SERVICE_CLIENT_SECRET:?…}` (`:712`).
  - Los demás clientes usan `:-` (`:701`, `:706`, `:709`, `:714`).
- `mobile-api` también exige el secret con `:?` (`:1648`).
- Los secrets se leen del entorno `production-extra` (`.github/workflows/deploy.yml:597-599`).

**Por qué importa**
- Con `:?`, si la variable no existe, `docker compose` falla al interpolar el archivo **entero**, no solo `mobile-api`.
- Si el push a `main` llega antes de cargar los secrets, no se despliega nada.
- `:?` en `MOBILE_DB_CONNECTION` sí es lo que pide la guía (`Produccion.md:25`). Lo que se aparta del patrón es el secret del cliente M2M dentro del bloque de Auth.

**Cómo arreglarlo**
1. **Antes de cualquier push a `main`**, cargar los 3 secrets de §6 en `production-extra`.
2. Para que un olvido futuro no tumbe Auth, cambiar `:712` a `:-`, igual que los demás clientes. Antes, confirmar en `ServiceAuth` de Auth cómo trata un cliente con secret vacío (que lo ignore o lo rechace, nunca que lo acepte sin secret).
   - Dejar `:?` en `mobile-api` y en `migrations`, que son de Mobile.

**Cómo verificar**
- `ProductionExtraSecretsDeployTests` y `ServiceClientSecretsDeployWiringTests` en verde.
- `docker compose config` con un `.env` sin el secret debe fallar solo por las entradas de Mobile.

---

### O2 🟠 El login y el refresh móviles de Auth son anónimos y cross-tenant: revisión de seguridad aparte

**Cómo se encontró**
- `Seguridad.md:26`: cada acción declara actor, permiso y rate limit, o `[RateLimitExempt("razón")]`.
- `Seguridad.md:116`: un endpoint anónimo con riesgo de abuso necesita regla pre-auth por IP en el Gateway y `MaxRequestBodySize`.
- El commit del servicio también tocó Auth.

**Evidencia**
- `src/Services/Auth/Api/Controllers/MobileAuthController.cs:95-99`: `POST login` con `[AllowAnonymous]` y `[RateLimitExempt]`. Se apoya en `ILoginThrottler`.
- `MobileAuthController.cs:172-183`: `POST refresh` con `[AllowAnonymous]`, `[RateLimitExempt]` y `ResolvedTenantId: null`. A propósito, no valida el host de la oficina.
- `src/Services/Auth/Application/CentralLogin/Commands/DiscoverLogin.cs`: nuevo `ForceSelection`, que obliga a elegir oficina aunque solo haya una.

**Por qué importa**
- Es una puerta de autenticación nueva, anónima y que cruza oficinas.
- Un error aquí permite probar contraseñas o reutilizar refresh tokens entre tenants.
- No está mal hecho a simple vista, pero merece una revisión dedicada y no una lectura de paso.

**Cómo arreglarlo (revisar y dejar constancia)**
1. **Login.** Confirmar que tiene regla en `GatewayRateLimiting:PreAuthByIp:Rules` y ruta con `MaxRequestBodySize` propio, igual que el login web.
2. **Throttling.** Confirmar que `ILoginThrottler` limita por IP **y** por email, y que el bloqueo es el mismo que el del login web.
3. **Refresh.** Comprobar que el refresh token está atado a usuario y tenant, rota en cada uso, detecta reutilización (revoca la familia) y respeta la denylist de sesión y el offboarding.
4. **Selección de oficina.** Confirmar que la referencia efímera de `DiscoverLogin` expira pronto, es de un solo uso y solo lista oficinas donde la contraseña ya fue validada.
5. **Pruebas.** Agregar pruebas de abuso: N intentos → bloqueo; refresh reutilizado → 401 y familia revocada; refresh de un usuario offboarded → 401.

**Resolución (2026-11-19)**

La revisión encontró, además, un hueco mayor: `/auth/mobile/refresh` aceptaba cualquier token `Workspace` sin binding de host, así que anulaba la defensa de la Fase 18 también para el CRM y el portal web.

| Punto | Estado | Qué se hizo |
|---|---|---|
| Superficie propia | ✅ | `SessionSurface.Mobile`. El refresh móvil solo rota tokens Mobile y el web rechaza los Mobile (`RefreshAccessTokenHandlerTests`, `MobileAuthControllerTests`). |
| Sesión única | ✅ | Por canal, decisión del dueño: una web y una móvil conviven (`UserSession.Channel` y `CompetesWith`, migración `AddSessionChannel`). El takeover solo revoca el canal propio; hay takeover móvil en `POST /auth/mobile/session/takeover`. |
| 1. Gateway | ✅ | Ruta `auth-mobile` con tope de 16 KiB y la regla `PreAuthByIp` que ya existía (`GatewayPublicBookingRouteTests`). |
| 2. Throttling | ✅ | IP + email por tipo de cuenta, 10 fallos con espera de 5 min (decisión del dueño; ventana corta porque la app no tiene otra puerta si alguien bloquea un email; probado contra Redis en `LoginThrottlerEmailIntegrationTests`). Solo cuenta cuando la clave no coincide en ninguna oficina: el mismo email con otra clave en otra oficina no suma. Aplica también al login central web. |
| 3. Refresh | ✅ | Rotación, reuso y revalidación ya existían. Ahora no puede cruzar superficies. |
| 4. Selección de oficina | ✅ | Ya cumplía: 60 s, un solo uso, oficina dentro del set. Además, la app solo acepta oficinas de cuentas del portal. |
| MFA | ✅ | El modo "solo enrolar MFA" ahora también aplica a la app (`MfaEnrollmentSessionTests`). |
| Controller con lógica (libro §6.2) | ✅ | Caso de uso `MobileSignIn` en Application; el controller entrega un solo mensaje. Los frenos y la autenticación se comparten con el login web (`CentralLoginAuthentication`). |
| 5. Pruebas | ✅ | Pruebas de abuso y de flujo en `CentralLoginHandlersTests`, `SessionTakeoverTests`, `UserSessionTests` y `RefreshAccessTokenHandlerTests`. |
| Documentación | ✅ | Decisión C-59 y nuevo ítem en `Checklists/Seguridad.md` §7. |

**Contrato con la app:** login y refresh no cambian de ruta ni de JSON. El takeover de la app pasa a `POST /auth/mobile/session/takeover`; con `/auth/session/takeover` ahora responde `Auth.TakeoverInvalid`. Las sesiones móviles anteriores a este cambio son `Workspace`, así que la app tiene que volver a iniciar sesión una vez.

---

### O3 🟠 La comercialización no se decidió contra el módulo `miles` que ya existe

**Cómo se encontró**
- Fase 9 de `Nuevo_Microservicio.md`.
- `Modulos_Planes.md:14-15`: clasificar el servicio según la tabla "Cuándo NO comercializar" y dejar escrita la decisión.

**Evidencia**
- Mobile no tiene entrada en `PermissionModuleMap` ni gate de módulo. Es transversal por omisión (`src/Services/Mobile/README.md:21`).
- Ya existen el permiso `portal.miles.use` (`src/BuildingBlocks/Authorization/PortalPermissions.cs:10`) y un módulo de frontend `miles`, omitido a propósito en `src/BuildingBlocks/Authorization/PermissionModuleMap.cs:27`.

**Por qué importa**
- Hoy el millaje queda gratis para todas las oficinas y no hay palanca para activarlo por plan o por cliente.
- Además existen dos conceptos de "millaje" que no se hablan: `portal.miles.use` y los permisos `mobile.*`.

**Cómo arreglarlo**
1. Decisión del dueño: ¿se vende (módulo `miles`) o es transversal?
2. **Si se vende**, hacerlo todo a la vez, como pide `Modulos_Planes.md`:
   - mapear el prefijo `mobile.` al módulo en `PermissionModuleMap`;
   - entrada en el catálogo de planes;
   - gate de módulo en Mobile;
   - frontends.
3. **Si es transversal**, dejar escrita la fila de "Cuándo NO comercializar" y decidir qué pasa con `portal.miles.use`: unificarlo con `mobile.trips.read` o retirarlo.

---

### O4 🟠 Faltan pruebas de seguridad mínimas

**Cómo se encontró:** `Testing.md`, matriz de seguridad mínima y ítem de arquitectura.

| Falta | Ítem | Qué copiar |
|---|---|---|
| Filtro global de tenant: A no ve lo de B | `Testing.md:128` | Patrón de Notes (persistencia InMemory con dos tenants) |
| `sid` en la denylist → 401 `Auth.SessionRevoked` con Redis real | `Testing.md:129` | Hoy es un doble (`deploy/tests/TaxVision.Mobile.Tests/…/VehicleHttpTests.cs`) |
| Exceder la cuota → 429 con `Retry-After` | `Testing.md:135` | `.Integration.` de Notes o FinancialInsights |
| Regla "los controllers no dependen de Infrastructure" | `Testing.md:59` | `NotesArchitectureTests` (hoy `MobileArchitectureTests` solo revisa atributos) |
| Evento de integración con `TenantId` vacío | Matriz de seguridad | Consumer de proyección de Notes |

**Por qué importa:** estas pruebas son las que impiden que un refactor futuro abra los datos de una oficina a otra sin que nadie lo note.

---

### O5 🟠 No hay runbook del servicio

**Cómo se encontró:** `Produccion.md:110-112` (Fase 15 de `Nuevo_Microservicio.md`).

**Qué falta:** un documento corto con:
- cómo verificar que el servicio está sano después del deploy;
- qué reiniciar;
- la reconciliación de permisos y su intervalo;
- el job de retención de R1 y su plazo;
- qué hacer si la app móvil reporta errores de sincronización (conflictos de secuencia o de replay).

**Dónde:** junto a los demás runbooks puntuales (por ejemplo `DEVS/Runbook_Resincronizar_Buzones.md`). Los 7 `.md` que hoy están en la carpeta del servicio pueden alimentarlo (ver M13).

---

## 5. Hallazgos menores 🟡

| ID | Qué pasa | Ítem de la guía | Evidencia | Cómo arreglarlo |
|---|---|---|---|---|
| M1 | El agregado no controla a sus hijos: el handler crea `TripPoint` y `TripOperation` y los agrega por fuera de `Trip` | Fase 3: agregado (Guardrail #5) | `Domain/Trips/TripPoint.cs:20`, `TripOperation.cs:17`; `Application/Trips/Commands/ChangeTripCommand.cs:80,85`, `StartTripCommand.cs:40-41` | Que `Trip` cree y valide sus puntos y operaciones (métodos de negocio en el agregado); las fábricas, `internal` |
| M2 | Las transiciones del viaje no emiten eventos, y `UseTaxVisionDomainEvents()` queda registrado sin uso | Fase 3: "una transición = su evento" (Guardrail #2) | `TaxVision.Mobile.Api/Program.cs:81` | Si nadie los consume hoy, dejarlo escrito en el README. Si se van a consumir (millaje en Billing o reportes), emitirlos como eventos de dominio |
| M3 | Solo hay un value object (`VehicleDetails`). Propósito y moneda son strings con validador estático | Fase 3: value objects en `Domain/ValueObjects/` (Guardrails #3, #6) | `Domain/Trips/TripPurpose.cs` | Llevarlos a value objects, uno por archivo |
| M4 | El acquirer M2M devuelve `null` en silencio si falta configuración | `Seguridad.md:104-105` | `Infrastructure/Permissions/MobileServiceTokenAcquirer.cs:19-22` | Log de warning cuando falta `ClientId` o `ClientSecret`, más la prueba "sin config → null + warning" |
| M5 | Aritmética de zona horaria a mano y `AT TIME ZONE` con fallback al id IANA, que SQL Server no entiende (500) | Fase 10: tipos de `BuildingBlocks/TimeZones` | `Infrastructure/Persistence/Repositories/TripRepository.cs:151-157`, `:231-232` | Usar `IanaTimeZone`/`LocalDay` y no caer al id IANA: si no hay mapeo a Windows, calcular los límites en .NET |
| M6 | Tarifas de millaje: `PlatformAdmin` puede crearlas (quedan en el tenant de plataforma, sin sentido de negocio). La fecha por defecto es la UTC, no la local. No se puede crear una tarifa con fecha pasada, así que los viajes anteriores a la primera tarifa nunca tienen estimación | Fase 3 y Fase 10 | `Api/Controllers/MileageConfigurationController.cs:38,46,64`; `Domain/…/MileageRate.cs:29` | Quitar `PlatformAdmin` de crear/listar tarifas, usar la fecha local de la oficina y permitir fecha efectiva pasada (decisión del dueño) |
| M7 | Configuración muerta: `RateLimit:EnforceTierQuotas` no la lee nadie | Fase 12 | `Api/appsettings.json:6`; `docker-compose.yml:1649` | Quitarla de los dos lados |
| M8 | `appsettings.json` versiona `RabbitMq:Uri` con las credenciales por defecto `guest` | Fase 12 / secrets | `Api/appsettings.json:3` | Dejarla vacía, como Notes; local va por user-secrets y prod por compose |
| M9 | Falta `appsettings.Development.json` con `Jwt:PublicKeyPath`, así que la flota local no arranca sin un user-secret extra | Fase 12: scripts locales | Comparar con `Notes.Api/appsettings.Development.json` | Crear el archivo igual al de Notes (en local ya se puso por user-secret mientras tanto) |
| M10 | `MobileDbContextFactory` prioriza las variables de entorno `MOBILE_DB_CONNECTION` y `ConnectionStrings__Default` sobre los user-secrets. Con el `.env` de producción cargado en la shell, `dotnet ef` apuntaría a producción | Fase 4: persistencia y factory | `Infrastructure/Persistence/MobileDbContextFactory.cs:13-14` | Usar el mismo orden que la factory de Notes (user-secrets primero) |
| M11 | Comentarios mal ubicados: el de FinancialInsights quedó encima de las líneas de Mobile | Guardrail #36 | `docker-compose.yml:710`; `.github/workflows/deploy.yml:594` | Mover cada comentario a su bloque y agregar uno corto para Mobile |
| M12 | Índice redundante `(TenantId, CustomerId, Id)` que duplica la clave alterna; la tabla `Vehicles` no lleva el prefijo `Mobile` como las demás | Fase 4 | `Infrastructure/Persistence/Configurations/VehicleConfiguration.cs:25-30` | Quitar el índice en una migración nueva; el nombre de la tabla se puede dejar (renombrar cuesta más de lo que aporta) |
| M13 | 7 `.md` en la carpeta del servicio con rutas locales (`D:/TaxPro-Milage`), nombre del servidor y fechas, mezclando inglés y español | Guardrail #36; `Produccion.md` §11 | `src/Services/Mobile/*.md` | Dejar un `README.md` corto; mover lo útil al runbook (O5) y borrar lo de proceso (`FILES_CHANGED.md`, `VALIDATION*.md`) |
| M14 | `IgnoreQueryFilters()` sin el comentario que diga cuál de las condiciones seguras aplica | `Seguridad.md:65` (G8) | `Infrastructure/Persistence/Repositories/TripRepository.cs:11-16` | Comentario de una línea en cada uso |

---

## 6. Antes de subir a producción

Cargar en **GitHub → Settings → Environments → `production-extra`** (lo exige `deploy.yml:597-599`):

| Secret | Valor |
|---|---|
| `MOBILE_DB_CONNECTION` | Cadena de SQL Server de producción, como `NOTES_DB_CONNECTION` pero con `Database=TaxVision_Mobile` |
| `MOBILE_SERVICE_CLIENT_ID` | `mobile-worker` |
| `MOBILE_SERVICE_CLIENT_SECRET` | Valor nuevo al azar. Auth y Mobile usan el mismo |

Para generar el secret:

```powershell
$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

`RABBITMQ_CONNECTION` y la clave pública JWT ya existen y se reutilizan. No hace falta nada más en compose ni en `deploy.yml`: la imagen se construye sola desde el compose, y `apply-migrations.sh` ya tiene el bloque de Mobile.

**Orden recomendado:**
1. Cargar los 3 secrets.
2. Push a `main`.
3. Verificar `/health/ready` de mobile-api.
4. Antes de dar la app a clientes reales: R1 y O2.

## 7. Propuesta para la Guía Maestra

La guía no tiene un ítem sobre **retención de datos personales** (ubicación, documentos, mensajes). R1 salió por la revisión de datos de ubicación, no por un ítem del checklist.

Propuesta para `Checklists/Produccion.md` (o `Seguridad.md`):

> - [ ] [si guarda datos personales sensibles: ubicación, salud, identificadores] **Plazo de retención decidido por el dueño, borrado físico al eliminar y job de purga por lotes** (patrón `ReminderRetentionJob`). Verificación: prueba de que eliminar borra los datos sensibles y `.Integration.` del job.
