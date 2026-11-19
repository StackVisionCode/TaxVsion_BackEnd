> Actualizacion 2026-11-19: se implemento la ampliacion backend de tracking descrita en [TRACKING_API.md](TRACKING_API.md), con migraciones aplicadas en DEV001. Este analisis conserva el diagnostico previo; para el estado verificado ver [VALIDATION_TRACKING.md](VALIDATION_TRACKING.md).

# Cobertura de TaxPro-Milage y funcionalidades pendientes en Mobile

Fecha: 2026-11-19. Analisis del checkout actual `D:/TaxPro-Milage` frente a este backend. Se revisaron rutas, servicios HTTP, modelos, GPS, dashboard, historial, detalle, perfil, ajustes, login y sincronizacion. Se contrasto el esquema real de `DEV001/TaxVision_Mobile` mediante consultas de solo lectura. Este documento es una propuesta; no implementa endpoints ni tablas nuevos.

## Resultado

El backend NO cubre toda la aplicacion. Mobile implementa exclusivamente mantenimiento de vehiculos y su infraestructura de seguridad. No hay persistencia ni endpoints de viajes, puntos GPS, seguimiento activo, resumen de millas ni configuracion de tarifas. El alcance original de la implementacion limitaba expresamente el trabajo a vehiculos y excluia tracking, calculos de millas e IRS; por eso la migracion inicial no los incluye.

Esquema confirmado en DEV001/TaxVision_Mobile:

- `Vehicles`.
- `UserPermissionsProjections`.
- `__EFMigrationsHistory`: `20261118202003_InitialCreate`.

No hay tabla Tracking, Trips ni TripPoints. Esto no es una migracion pendiente de tracking: el modelo y la migracion de tracking aun no existen. La busqueda en servicios y Gateway tampoco encontro un reemplazo para las rutas antiguas que llama la app. No se realizaron llamadas HTTP a una flota en ejecucion; la ausencia de cobertura es de codigo/rutas y esquema, no una prueba de respuesta de produccion.

## Cobertura por funcionalidad

| Funcionalidad de la app | Fuente | Backend actual | Trabajo pendiente |
| --- | --- | --- | --- |
| Login, seleccion de oficina y refresh | customer-auth.service.ts, customer-session-refresh.service.ts | Auth, /auth/mobile/login y /auth/mobile/refresh | Verificacion integrada; no duplicarlo en Mobile |
| Identidad y contacto/avatar del cliente | customer-auth.service.ts, profile.page.ts | Auth /auth/me y Customer /customers/me/contact, /avatar; Customer tambien tiene /phone | Mantener estos dueños; no crear otra tabla de clientes |
| CRUD, listado y favorito de vehiculos | tracking-api.service.ts, profile.page.ts | Mobile /mobile/vehicles | Implementado; probar flujo vivo con el resto del stack |
| Inicio de recorrido | gps-tracking.service.ts:261 | UUID y estado solo locales | Registrar sesion y vehiculo inicial en servidor |
| Sincronizacion GPS cada 30 segundos | gps-tracking.service.ts:107,711; tracking-api.service.ts:39 | Ruta antigua /client/tracking/create sin implementacion equivalente | Ingesta autenticada/idempotente de recorridos y puntos |
| Pausa, reanudacion y cancelacion | gps-tracking.service.ts:319-459 | Cambian estado local; pausa detiene la sincronizacion | Persistir transiciones y duraciones; cancelacion explicita |
| Finalizar recorrido | trip.service.ts:329; gps-tracking.service.ts:367 | Envio al endpoint antiguo | Completar viaje atomicamente y confirmar persistencia |
| Historial con filtros/paginacion | trip.service.ts:70; history.page.ts:253 | POST /client/tracking/getTrips sin implementacion equivalente | Consulta por fechas, proposito, estado, distancia, vehiculo y orden |
| Detalle y mapa de un viaje | trip.service.ts:124; trip-detail.page.ts:193 | POST /client/tracking/getTripById sin implementacion equivalente | Leer viaje y puntos GPS ordenados |
| Editar proposito y notas | trip-detail.page.ts:419; trip.service.ts:459 | PUT a ruta antigua sin ID interpolable | PATCH/PUT con ID y DTO limitado a campos editables |
| Eliminar viaje | trip-detail.page.ts:485; trip.service.ts:485 | DELETE a ruta antigua sin ID interpolable | Borrado/archivado, semantica acordada e impacto en estadisticas |
| Dashboard y resumen semanal/mensual/anual | trip.service.ts:517; dashboard.page.ts:302 | /client/tracking/getUserStatistics sin implementacion equivalente | Agregaciones de viajes persistidos, aisladas por cliente |
| Grafico de ultimos siete dias | trip.service.ts:561; dashboard.page.ts:318 | /client/tracking/getMilesLastSevenDays sin implementacion equivalente | Agrupar por fecha local y completar dias sin viajes con ceros |
| Tarifa/configuracion de kilometraje | trip.service.ts:505; dashboard.page.ts:180 | /client/tracking/getIRSMovilConfiguration sin implementacion equivalente | Lectura de configuracion efectiva y politica de administracion |
| Reintento sin conexion | trip.service.ts:785-853 | Cola en Preferences, sin invocacion encontrada del sincronizador | Disparadores de reintento, confirmacion y deduplicacion en backend |
| Preferencias del dispositivo | profile.page.ts y settings.page.ts | Preferences/localStorage | Pueden seguir locales; endpoint solo si se requiere sincronizacion entre dispositivos |
| Compartir resumen | trip-detail.page.ts:443 | Capacitor Share con texto | No requiere endpoint de enlace publico/PDF para el flujo actual |
| Rutas frecuentes | route-history.service.ts | Servicio local sin consumidores encontrados en codigo activo | No priorizar como dependencia de tracking; sincronizacion opcional futura |
| Foto del vehiculo desde camara/galeria | profile.page.ts | Mobile acepta referencia HTTPS, no subida de binarios | Si se requiere camara: flujo con CloudStorage y asociacion autorizada del archivo |

## Inventario de rutas antiguas que hay que reemplazar

Definidas tanto en `src/environments/environment.ts:18` como en `environment.prod.ts:19`:

| Uso | Metodo actual | Ruta actual | Problema |
| --- | --- | --- | --- |
| Snapshots activos y finalizacion | POST | /client/tracking/create | No existe implementacion en el backend revisado |
| Lista de viajes | POST | /client/tracking/getTrips | No existe implementacion; createTrip reutiliza esta misma URL para otro contrato |
| Detalle | POST | /client/tracking/getTripById | No existe implementacion |
| Actualizacion | PUT | /client/tracking/getTripById | `.replace(':id',id)` no agrega ID porque la URL no contiene :id |
| Borrado | DELETE | /client/tracking/getTripById | Mismo error de ID y ruta |
| Rango de fechas | GET | /trips/range | No existe implementacion; puede unificarse con listado filtrado |
| Estadisticas | POST | /client/tracking/getUserStatistics | Dos metodos lo usan con cuerpos distintos: identidad vs TripStatisticsDto |
| Siete dias | POST | /client/tracking/getMilesLastSevenDays | No existe implementacion |
| Configuracion | POST | /client/tracking/getIRSMovilConfiguration | No existe implementacion |

No basta con crear una tabla Tracking ni con cambiar URLs. La app espera ApiResponse y PaginatedResponse para viajes, mientras Mobile devuelve DTOs y PagedResult para vehiculos. Debe fijarse un contrato y adaptar servicio y pantallas al mismo tiempo, preferiblemente siguiendo el contrato actual del backend.

## Endpoints propuestos para Mobile (NO implementados)

| Metodo | Ruta propuesta | Responsabilidad |
| --- | --- | --- |
| POST | /mobile/trips | Crear/iniciar viaje; ClientTripId UUID, VehicleId, proposito, instante y punto inicial |
| PUT | /mobile/trips/{tripId}/tracking | Sincronizar snapshot/lote GPS con version/sequence y confirmacion del servidor |
| POST | /mobile/trips/{tripId}/pause | Pausar, registrar instante y cerrar intervalo activo |
| POST | /mobile/trips/{tripId}/resume | Reanudar sin contabilizar tiempo pausado |
| POST | /mobile/trips/{tripId}/complete | Persistir cierre y ultimos puntos; devolver resumen canonico |
| POST | /mobile/trips/{tripId}/cancel | Cancelar sesion activa y evitar que un snapshot viejo la reactive |
| GET | /mobile/trips | Listar con filtros y paginacion real; sin descargar puntos de todos los viajes |
| GET | /mobile/trips/{tripId} | Obtener detalle/resumen y permisos del recurso |
| GET | /mobile/trips/{tripId}/points | Leer puntos ordenados con paginacion/limite para mapas extensos |
| PATCH | /mobile/trips/{tripId} | Cambiar proposito/notas mediante DTO restringido |
| DELETE | /mobile/trips/{tripId} | Eliminar/archivar viaje segun politica de conservacion |
| GET | /mobile/statistics | Totales por periodo, estado y proposito, desde datos persistidos |
| GET | /mobile/statistics/daily?days=7 | Serie diaria y conteo de viajes; zona horaria explicita |
| GET | /mobile/mileage-configuration | Configuracion/tarifa efectiva, moneda, unidades y vigencia |

Los puntos pueden venir dentro del detalle para recorridos pequeños; separarlos permite controlar tamaño y paginar. Pausa/reanudacion/cancelacion tambien pueden viajar como transiciones versionadas en sync, pero no deben quedar solo en el telefono. No hacen falta dos APIs competidoras con contratos distintos para el mismo tracking.

Opcionales, segun requerimiento: GET /mobile/trips/active (recuperacion entre dispositivos), GET/PUT /mobile/preferences (preferencias sincronizadas), CRUD /mobile/saved-routes (rutas frecuentes). Un GET /mobile/bootstrap podria agrupar vehiculos, configuracion y viaje activo. Los endpoints administrativos de tarifas dependen de quien deba administrarlas; el cliente no debe editar una tarifa fiscal global.

## Persistencia propuesta (NO creada)

1. **MobileTrips**: Id servidor, ClientTripId, TenantId, CustomerId, VehicleId, Status, Purpose, Notes, StartedAtUtc, EndedAtUtc, LastObservedAtUtc, LastAcceptedSequence, DistanceMeters, ActiveDurationSeconds, MaxSpeed, CreatedAtUtc, UpdatedAtUtc y RowVersion. Indice unico por tenant/cliente/ClientTripId. Registrar criterio de distancia/unidades y procedencia. Un viaje referencia el vehiculo seleccionado al iniciar; no se vuelve a resolver el favorito al terminar.
2. **MobileTripPoints**: TripId, TenantId, CustomerId, PointSequence, ObservedAtUtc, Latitude, Longitude, AccuracyMeters y opcionales SpeedMetersPerSecond/Altitude/Heading. Unico por viaje y secuencia, orden estable e indice para lectura por paginas. Los DTO actuales descartan velocidad/altitud/rumbo: ampliarlos si se necesitan maximos/historial precisos. No usar solo timestamp como identidad: puede repetirse.
3. **MobileTripStateChanges** (o intervalos equivalentes): viaje, tipo de transicion, instante, version/id de operacion. Necesario si se quiere trazabilidad y duracion activa fiable, incluyendo pausa y cancelacion. No hace falta registrar cada segundo como un cambio de estado.
4. **MobileMileageRates/MobileMileageConfigurations**: solo despues de definir propiedad y reglas de tarifas. Incluir vigencia, proposito, unidades y moneda; overrides tenant/cliente si se autorizan. Guardar en viaje la tarifa/version aplicada al completarse para que un cambio futuro no reescriba silenciosamente los importes historicos.

No hace falta crear tablas de estadisticas diarias/mensuales en la primera entrega: pueden calcularse desde viajes finalizados y sus indices. No copiar tablas de clientes/usuarios de otros servicios. Las nuevas migraciones irian a TaxVision_Mobile en DEV001 y permisos nuevos a Auth; la migracion inicial ya aplicada no debe editarse.

## Correcciones necesarias en la app

### Riesgos de guardado y sincronizacion

- `tracking-api.service.ts:94`: `buildTripPayloadDto` inicia una consulta async del vehiculo con subscribe y devuelve el payload inmediatamente. El primer envio puede salir con VehicleId vacio o uno residual. Resolver/guardar VehicleId al iniciar el viaje y encadenar llamadas.
- `gps-tracking.service.ts:411-424`: stopTracking se suscribe a finishTrip y limpia el estado antes de esperar la respuesta. Devuelve savedTrip potencialmente nulo/anterior. `tracking.page.ts:554` muestra guardado exitoso sin acuse real. Esperar confirmacion o guardar primero una cola durable y mostrar pendiente.
- Snapshot cada 30 s y cierre reenvian los puntos completos. Sin idempotencia, secuencias y version de estado, se duplican viajes/puntos o una respuesta atrasada puede reabrir un viaje completado. La cola debe tolerar reintentos y mensajes fuera de orden.
- `trip.service.ts:816`: syncPendingTrips existe, pero no se encontro ninguna llamada desde el resto de la app. Necesita ejecutarse tras recuperar conexion/login/reabrir app, con un unico sincronizador.
- `gps-tracking.service.ts:880` guarda `pending_trip_<timestamp>`; TripService usa una sola lista `pending_trips`. El sincronizador no procesa las claves del otro mecanismo.
- `customer-auth.service.ts:225-229`: logout borra Preferences, localStorage e IndexedDB. Esto puede eliminar recorridos pendientes. Ademas stopTracking se invoca sin esperar durante logout. Definir conservacion segura de pendientes por oficina/cliente y evitar que un usuario distinto reciba datos del anterior.
- Las claves `trips_cache`, `statistics_cache`, `pending_trips`, temporal y rutas no incluyen tenant/cliente. Deben particionarse por la identidad autenticada y limpiarse/cargarse al cambiar de oficina. No confiar solo en el borrado global de logout.

### Contrato y presentacion

- `gps-tracking.service.ts:691` incrementa elapsedTime en segundos. El envio periodico lo transmite sin convertir; finishTrip lo multiplica por 1000. El mismo endpoint recibiria dos unidades distintas. Adoptar durationSeconds explicito y convertir una sola vez.
- El cliente genera startTime/lastUpdate en epoch milisegundos y Trip expone fechas ISO. Normalizar ese contrato; mantener UTC en servidor y aplicar zona de oficina/lector a agrupaciones diarias.
- `trip.service.ts:459,485`: corregir rutas con ID. Limitar actualizacion a proposito/notas; no aceptar Partial<Trip> como permiso para alterar owner/distancia arbitrariamente.
- `trip.service.ts:491` convierte error de borrado en false; `trip-detail.page.ts:494` ignora el booleano y muestra exito. Propagar fallo o comprobar resultado.
- `history.page.ts:253` solicita solo la primera pagina; los filtros se aplican al subconjunto local. `loadMore:346` es un setTimeout sin peticion. Implementar filtros/paginacion remotos y conservar metadatos.
- Eliminar y compartir en history.page.ts son placeholders de consola. El detalle si usa Share nativo y llama a borrar; hay que cablear ambas pantallas coherentemente.
- getTrips/getTripById/getStatistics ocultan errores HTTP devolviendo cache. Se debe mostrar si los datos son locales/desactualizados o si no se pudieron cargar.
- `TripStatus` usa InProgress/Completed/Paused/Cancelled, pero createTrip manda in_progress. Unificar enum y transiciones; el inicio GPS actual no llama a createTrip.
- TripPurpose enum contiene business/medical/moving/personal; el comentario del DTO agrega charity. Fijar el catalogo funcional sin asumir tratamiento fiscal de cada categoria.
- Dashboard calcula deduccion usando irsConfiguration, historial/detalle usan environment.app.mileageRate y ajustes guardan otra tarifa en Preferences. Unificar fuente y calculo por proposito/fecha. No todas las millas se deben multiplicar indiscriminadamente por una unica tarifa.
- `sendTripStatistics` comparte URL con getStatistics, sin identificador del viaje; no se encontro consumidor activo. Evitar endpoint de estadisticas editables que acepte totales arbitrarios para sustituir registros reales.
- Hay dos servicios llamados TrackingApiService en archivos distintos (tracking-api.service.ts y tracking-api-service.ts), mas un DTO sin TripId/VehicleId. No se encontro llamada activa al autosave del segundo. Consolidar el camino de tracking antes de conectar nuevos endpoints.
- RouteHistoryService importa SavedRoute, que no se encontro definido en interfaces.ts, y no tiene consumidores activos encontrados. Es codigo incompleto/latente, no evidencia de una funcionalidad ya integrada.

## Reglas de backend para la siguiente implementacion

- Actor CustomerPortal y TenantId/CustomerId exclusivamente del JWT; no del customerId/companyId que hoy manda la app. Validar que VehicleId pertenece al mismo tenant/cliente. Consultas y constraints deben mantener ese aislamiento.
- Nuevos permisos de lectura/gestion/sincronizacion en catalogo, bundle portal y migracion Auth solo con InsertData; mantener proyeccion/pull recovery, denylist, UTC, Result/Error y pipeline existentes.
- Transiciones validas: InProgress <-> Paused; ambos pueden completar/cancelar; Completed/Cancelled no se reactivan por snapshots viejos. Decidir borrado logico/fisico antes de publicar el contrato.
- Validar coordenadas, timestamps/orden, accuracy, tamaños de notas y lotes. Limitar puntos por request y por pagina, aplicar rate limits y actualizar limite de cuerpo del Gateway: /mobile actualmente permite 16 KiB, insuficiente para snapshots de rutas grandes. Preferir lotes acotados a ampliar sin limites.
- No confiar en distancia/importe del telefono como dato fiscal canonico sin una politica explicita: validar/recalcular o guardar valor reportado y calculado por separado. Los calculos de distancia y la tarifa no existen hoy en Mobile.
- Persistencia atomica por lote/transicion y confirmacion con lastAcceptedSequence/version. Un cierre repetido debe devolver el mismo viaje, no crear otro. Retries no deben duplicar la cola local ni los puntos.
- Seguridad de localizacion: respuestas no-store, no registrar payloads GPS completos en logs, retencion/exportacion/borrado definidos y revision de acceso. No crear enlaces publicos de tracking para resolver el Share local.
- Si se conserva historial, eliminar un vehiculo no debe destruir viajes existentes; definir archivado, bloqueo o snapshot de datos del vehiculo. El CRUD actual elimina vehiculos fisicamente porque aun no hay referencias desde viajes.
- Pruebas: dominio/transiciones, tenant+customer IDOR, traduccion SQL, migraciones nuevas, idempotencia, lotes fuera de orden, rollback, pausa/duracion, deduccion por configuracion, rutas Gateway y flujo real dispositivo -> Gateway -> Mobile -> SQL.

## Orden de implementacion recomendado

1. Contrato canonico, identidades, unidades y estados; MobileTrips/MobileTripPoints, migracion, inicio/sync/finalizacion y lectura de detalle.
2. Cola durable en app, vehiculo fijo por viaje, confirmacion real, reintentos y orden/idempotencia; pausa/reanudacion/cancelacion.
3. Historial filtrado/paginado, detalle/mapa, edicion y borrado; indices y manejo de vehiculos historicos.
4. Estadisticas/dias desde viajes, configuracion de tarifas con vigencia y dueño definido; unificar deducciones de las pantallas.
5. Solo si se solicita: preferencias entre dispositivos, rutas frecuentes, subida de fotos, exportaciones o enlaces compartidos.

Este analisis no ejecuta migraciones ni modifica codigo funcional. No se corrieron builds/tests nuevos porque el trabajo fue lectura de codigo y esquema. Los resultados de validacion anteriores no prueban funcionalidades de tracking que aun no existen.
