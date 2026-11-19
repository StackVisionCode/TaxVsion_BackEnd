# Mobile: recorridos, tracking GPS y tarifas

Implementacion del 2026-11-19. Amplia el CRUD de vehiculos con viajes, puntos GPS, estados, sincronizacion idempotente, historial y estadisticas. La app D:/TaxPro-Milage fue adaptada posteriormente a este contrato, con cola offline y manejo de confirmaciones. La actualizacion de contenedores y frontend se registra en [DEPLOYMENT_MOBILE_APP.md](DEPLOYMENT_MOBILE_APP.md).

## Autorizacion y contratos

Todos los endpoints van por Gateway bajo `/mobile`. Identidad y propiedad: JWT validado, actor CustomerPortal, `tenant_id`, `sub` y `customer_id`; los DTO no aceptan propietarios del cliente. Un vehiculo de otro cliente/oficina y un viaje inaccesible responden 404. Login, sesiones, clientes y sus avatares siguen perteneciendo a Auth/Customer. Se conservan la proyeccion de permisos, pull recovery, denylist, rate limiting y errores compartidos.

Permisos nuevos: `mobile.trips.read`, `mobile.trips.manage`, `mobile.trips.sync` y `mobile.configuration.read` para el rol del portal. `mobile.rates.manage` es exclusivo de TenantAdmin/PlatformAdmin; el admin tambien recibe lectura de configuracion. La migracion Auth inserta catalogo, no grants directos de roles. Auth sincroniza los roles de sistema al arrancar con los binarios actualizados.

Respuestas exitosas: DTO directo o PagedResult; no `{success,data}`. Estados JSON: InProgress, Paused, Completed, Cancelled. Fechas de entrada: ISO 8601 con offset; se normalizan a UTC. Fechas de salida: UTC con Z. Duracion: segundos; distancia de entrada GPS: coordenadas, no totales editables. El servidor calcula metros y devuelve millas/kilometros.

| Metodo | Ruta | Resultado | Permiso |
| --- | --- | --- | --- |
| POST | /mobile/trips | 201 nuevo; 200 reintento identico | trips.manage |
| GET | /mobile/trips | 200 PagedResult | trips.read |
| GET | /mobile/trips/active | 200 lista de hasta 100 viajes activos, mas recientes primero | trips.read |
| GET | /mobile/trips/{id} | 200 resumen/detalle sin todos los puntos | trips.read |
| GET | /mobile/trips/{id}/points?after=-1&size=100 | 200 pagina por cursor | trips.read |
| PUT | /mobile/trips/{id}/tracking | 200 viaje y confirmacion de secuencias | trips.sync |
| POST | /mobile/trips/{id}/pause | 200 viaje | trips.sync |
| POST | /mobile/trips/{id}/resume | 200 viaje | trips.sync |
| POST | /mobile/trips/{id}/complete | 200 viaje e importe estimado si hay tarifa | trips.sync |
| POST | /mobile/trips/{id}/cancel | 200 viaje cancelado | trips.sync |
| PATCH | /mobile/trips/{id} | 200 proposito/notas actualizados | trips.manage |
| DELETE | /mobile/trips/{id}?sequence=7&observedAt=<ISO8601> | 204 borrado logico de viaje terminal | trips.manage |
| GET | /mobile/statistics?timeZone=America/New_York | 200 estadisticas desde SQL | trips.read |
| GET | /mobile/statistics/daily?days=7&timeZone=America/New_York | 200 serie diaria | trips.read |
| GET | /mobile/mileage-configuration?purpose=business&date=2026-11-19 | 200 configuracion efectiva | configuration.read |
| GET | /mobile/mileage-configuration/rates?page=1&size=50 | 200 historial paginado; admin | rates.manage |
| POST | /mobile/mileage-configuration/rates | 201 version de tarifa; admin | rates.manage |

Todos los permisos de la tabla llevan el prefijo `mobile.`. Los 5 endpoints anteriores de vehiculos permanecen disponibles. No se publican rutas M2M nuevas.

## Inicio y sincronizacion

Crear viaje:

```json
{
  "clientTripId": "c98038ca-f4e7-4d71-a341-b96174b9961f",
  "vehicleId": "ec4fa886-ce22-4723-8470-42479a1628a2",
  "purpose": "business",
  "notes": "Client visit",
  "startedAt": "2026-11-19T10:00:00Z",
  "startPoint": {
    "sequence": 0,
    "observedAt": "2026-11-19T10:00:00Z",
    "latitude": 40.0,
    "longitude": -74.0,
    "accuracyMeters": 5
  }
}
```

`clientTripId` es el UUID durable generado por el dispositivo. El servidor asigna otro `id`, usado en todas las rutas siguientes. Conserva ambos IDs en la cola local. Repetir el inicio con el mismo clientTripId y contenido devuelve el mismo viaje con `replayed:true`; cambiar contenido con ese ID devuelve 409. La unicidad es por tenant/cliente; no se consulta a otro dueño para resolver el UUID.

El inicio tiene revision 1 y punto 0. Siguiente lote:

```json
{
  "sequence": 2,
  "observedAt": "2026-11-19T10:00:30Z",
  "points": [
    {"sequence":1,"observedAt":"2026-11-19T10:00:15Z","latitude":40.001,"longitude":-74.0,"accuracyMeters":5},
    {"sequence":2,"observedAt":"2026-11-19T10:00:30Z","latitude":40.002,"longitude":-74.0,"accuracyMeters":5}
  ]
}
```

Hay dos secuencias independientes:

- `sequence` del comando: exactamente revision anterior + 1. La comparten tracking, pausa, reanudacion, cierre, cancelacion, edicion y borrado.
- `sequence` del punto: continua desde el ultimo punto aceptado + 1. Se envian solo puntos nuevos, no el snapshot completo repetido.

Se devuelve `revision`, `lastAcceptedPointSequence` y `replayed`. La respuesta es el estado actual del viaje; un reintento viejo puede devolver una revision posterior a la de la operacion reenviada. No sustituir estado local mas nuevo por uno mas viejo.

Un reintento debe conservar EXACTAMENTE la secuencia, instantes y contenido originales. Se compara una huella SHA-256 normalizada; la misma secuencia con distinto contenido responde `Trip.ReplayConflict` (409). Una secuencia futura sin sus predecesoras responde `Trip.SequenceConflict` (409). No se aceptan huecos ni mensajes fuera de orden; el cliente reintenta su cola en orden. RowVersion y constraints SQL resuelven carreras: ante 409 por concurrencia, recargar y reintentar el mismo payload, nunca inventar un ID nuevo.

El lote se confirma como una unidad SQL junto a puntos, revision y registro de operacion. Una validacion fallida no guarda parte de la ruta. La cola del dispositivo solo debe eliminar una operacion tras el acuse o despues de asegurar su persistencia durable local. El backend habilita los reintentos; no puede corregir por si solo el borrado prematuro de Preferences en el frontend.

## Estados, tiempo y distancia

- InProgress -> Paused -> InProgress; desde InProgress/Paused se puede completar o cancelar.
- Completed y Cancelled no se reactivan por snapshots posteriores. Proposito/notas se pueden editar con una nueva revision. Borrado solo en estos estados terminales.
- Pausa, reanudacion y cancelacion reciben `{ "sequence":3, "observedAt":"2026-11-19T10:01:00Z" }`.
- Complete recibe el mismo contrato que tracking; puede incluir los ultimos puntos. Si esta pausado, primero cerrar con puntos vacios o reanudar antes de enviar nuevos puntos.
- El tiempo activo acumula intervalos del estado InProgress. El tiempo pausado no suma. Los instantes de tracking/estado deben ser monotonicos, posteriores al inicio y no mas de cinco minutos en el futuro del servidor. El orden de puntos debe respetar secuencia y timestamps; un punto atrasado anterior al ultimo intervalo confirmado se rechaza.
- Distancia: suma Haversine de puntos GPS aceptados durante tramos activos, con radio terrestre medio 6371008.8 m. Tras reanudar no se suma un salto desde la posicion previa a la pausa. No hay map matching ni deteccion de fraude GPS; la precision reportada sigue dependiendo del dispositivo. No se aceptan distancias/velocidades agregadas ni importes arbitrarios del telefono como resumen canonico.
- Cada punto puede incluir speedMetersPerSecond, altitudeMeters y heading. La velocidad maxima procede de muestras de velocidad proporcionadas; si no existen, no se infiere de saltos GPS. La velocidad media usa distancia calculada/duracion activa.

## Consultas, limites y errores

`GET /mobile/trips`: page>=1, size entre 1 y 100; filtros startDate/endDate ISO8601 inclusivos sobre StartedAtUtc, purpose, status, vehicleId, minDistanceMiles/maxDistanceMiles. sortBy=date|distance|duration; sortOrder=asc|desc. Siempre hay desempate por ID. La ruta reemplaza tambien `/trips/range` con los filtros correspondientes. No se descargan todos los puntos de cada resultado.

Puntos: size entre 1 y 512, after=-1 para iniciar. Respuesta `{items:[...],nextAfter:123,hasMore:true}`. Usar nextAfter como cursor siguiente hasta hasMore=false. Las coordenadas permanecen ordenadas por secuencia.

Validaciones: proposito business|medical|moving|personal|charity; notas maximo 2000; latitud -90..90, longitud -180..180, accuracy 0..10000 m, numeros finitos; velocidad opcional 0..500 m/s, heading 0..<360. Son limites de entrada, no una afirmacion de calidad de medicion. Maximo 512 puntos por lote, cuerpo de tracking/complete de 256 KiB en API y Gateway. Las otras rutas conservan 16 KiB en Gateway. Lectura 300/min; operaciones normales 60/min; sync/transiciones 120/min por tenant/usuario con overlays de tenant 3000/600/1200.

400: payload, fecha, coordenadas o paginacion invalidos. 401: JWT/identidad invalida. 403: actor/permiso incorrecto. 404: recurso ausente o inaccesible. 409: secuencia, reintento alterado, estado incompatible, RowVersion o constraint unico. 413: cuerpo excesivo. 429: rate limit. Los errores de negocio siguen `{code,message}`; autorizacion/model binding siguen las convenciones de BuildingBlocks/MVC. No se registran coordenadas/notas en el ToString de los comandos.

Borrado logico: excluye viaje de listados, puntos, detalle y estadisticas. Conserva el historial y el identificador para impedir resurreccion por reintento. No equivale a purga fisica; la politica de retencion y purga requiere una definicion separada. Un vehiculo con viajes (incluso borrados logicamente) devuelve Vehicle.InUse/409 al intentar eliminarlo; las claves foraneas mantienen esa garantia ante carreras.

## Estadisticas y configuracion

Solo incluyen Completed no borrados. totalTrips, totalMiles, totalKilometers, totalDurationSeconds, promedios, semana actual (lunes), mes y año civiles en la zona pedida. Agrupacion de importe por moneda en `estimatedAmounts`; nunca se suman monedas diferentes. `tripsWithoutRate` indica viajes sin estimacion. `milesByPurpose` es una lista de pares purpose/miles. Daily devuelve dias consecutivos con ceros incluidos; days=1..366 y endDate opcional. Zona por parametro IANA, en su defecto claim zoneinfo y finalmente Etc/UTC. El viaje se imputa al dia de inicio, no se reparte entre dias al cruzar medianoche. Para una recuperacion exhaustiva de activos (mas de 100), usar listado filtrado por estado y paginas.

Tarifas: versiones inmutables por oficina/proposito/fecha UTC. POST exclusivo del admin, `{purpose,effectiveFrom,ratePerMile,currency}`. Fecha desde el dia UTC actual, sin backdating; ratePerMile 0..1000 con hasta 4 decimales; moneda 3 letras mayusculas. Una sola version por oficina/proposito/fecha; duplicados 409. Se puede programar una nueva version futura. No se permite modificar la tarifa global desde el token de un cliente.

GET de configuracion toma la version mas reciente con effectiveFrom<=date; si no existe, isConfigured=false y mileageRate/currency/rateId=null. No se crean tasas de IRS ni se infiere elegibilidad fiscal. Son configuraciones de estimacion administradas por la oficina.

Al completar se guarda el ID de tarifa, valor, moneda e importe redondeado a 2 decimales (AwayFromZero), usando la fecha UTC de inicio. Cambiar una tarifa futura no recalcula viajes completados. Editar solo notas conserva ese snapshot; reclasificar proposito vuelve a buscar la tarifa efectiva del nuevo proposito y deja su nueva estimacion. Sin tarifa el importe es null. Para preferencias de idioma/auto-stop y rutas frecuentes no se añadieron tablas: siguen siendo funciones locales/opcionales, no dependencias del tracking. Fotos binarias siguen el flujo de CloudStorage cuando se implemente su integracion cliente; Mobile mantiene referencias HTTPS.

## Esquema, pruebas y despliegue

Nueva migracion Mobile: AddTripTrackingAndMileageRates; crea MobileTrips, MobileTripPoints, MobileTripStateChanges y MobileMileageRates. Añade clave alternativa a Vehicles para FK compuesta (TenantId,CustomerId,VehicleId). No modifica la migracion inicial ya aplicada. Las constraints e indices aseguran owner, ClientTripId, secuencias y version de tarifa unicos. Nueva migracion Auth: AddMobileTripPermissions, solo catalogo.

La coleccion Postman TaxVision_Mobile incluye operaciones de tracking y tarifas con variables vacias para tokens/configuracion. El codigo Wolverine se regenera con el script existente y se verifica en modo Static. El wiring de Docker/migraciones/flota del servicio ya existia y cubre estas tablas sin un nuevo contenedor.

Antes de uso: aplicar migraciones Auth y Mobile en DEV001, compilar binarios actualizados, reiniciar Auth (sincronizacion de roles), Mobile y Gateway (rutas/limites). Refrescar login/token si Auth incrementa la version de permisos. Los checks SQL automatizados crean bases temporales separadas; no insertan recorridos de prueba en TaxVision_Mobile.

Ver VALIDATION_TRACKING.md para comandos realmente ejecutados y el estado de las migraciones. No se afirma prueba de dispositivo fisico, GPS real, Redis real ni de la flota completa por haber pasado pruebas SQL con dobles de esas dependencias.
