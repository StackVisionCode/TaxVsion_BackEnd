# communication-recorder

Grabación **server-side** de reuniones y llamadas de Communication (fase F7 del MASTER de Communication).

- **Captura**: un ffmpeg por pista recibe el RTP que manda mediasoup (`PlainTransport` → SDP) y lo escribe **sin recodificar** (`-c copy`) en `{spool}/{sessionId}/{trackId}.webm`.
- **Composición offline**: cuando Communication envía `stop` con el timeline ya resuelto, arma una grabación 720p (configurable) con rejilla o pantalla compartida + tira, slates, insignias, borde de hablante, avisos y la mezcla de todo el audio.
- **Subida y eventos**: sube las pistas de audio (carpeta `RecordingTracks`) y el compuesto (`Recordings`) a MinIO temp + `SaveFileRequested`, y publica `communication.recording.composed.v1` o `communication.recording.composition_failed.v1`.

No conoce reglas de negocio: Communication decide qué se graba, cuándo para y quién es quién; el recorder solo ejecuta.

## Decisión T-31: Node + ffmpeg

PoC del 2026-10-30 (i9-14900HX, ffmpeg 8.1, mediasoup 3.21):

| Medida | Resultado |
|---|---|
| Captura `-c copy` por pista (720p VP8 1 Mbps / Opus) | **0,33–0,46 % de un núcleo**, **25–40 MB RSS** por pista |
| Composición 4 tiles, 42 s, 2 hilos | 3,3 s (≈ 12× tiempo real), pico 270 MB |
| Composición 9 tiles, 42 s, 2 hilos | 4,5 s (≈ 9× tiempo real), pico 435 MB |
| Tamaño del compuesto | ~1,6 Mbps (~720 MB/h) |

mediasoup-recorder (Rust + GStreamer) no se pudo evaluar sin Docker y añadía dos dependencias nuevas; ffmpeg ya está en el stack (imagen del transcript worker). Pendiente: medición en la VM (P-022) antes de F11.

Prueba de integración de este servicio (Windows, ffmpeg 8.1.2, 2 hilos): 3 participantes, 6 pistas (cámara, cámara con cambio de resolución a mitad, pantalla compartida, solo audio), 30 s → **6 segmentos, 3,1–3,3 s de pared (≈ 9–10× tiempo real), MP4 H.264 1280×720 750 frames + AAC, 2,87 MB**.

## API HTTP v1

Todas las rutas `/v1/*` exigen `Authorization: Bearer <JWT de servicio de Auth>`: firma contra el JWKS de Auth, `iss`, `aud` (`TaxVision.Services`), algoritmo RS256/PS256/ES256, `actor_type = Service` y `client_id` incluido en `RECORDER_ALLOWED_CLIENT_IDS`. Errores siempre `{code, message}`.

| Método y ruta | Cuerpo | Respuesta |
|---|---|---|
| `GET /health` (sin auth) | — | `200 {status:'ok', activeSessions, activeTracks, queueDepth}` |
| `GET /metrics` (sin auth) | — | Prometheus |
| `POST /v1/sessions` | `{sessionId, tenantId, scope:'Meeting'\|'Call', scopeId, startedAtUtc, maxDurationSeconds, toleranceSeconds, tracksRetentionDays, captureVideo, composition:{width,height,fps,videoKbps,audioKbps,format:'mp4'\|'webm'}}` | `201 {sessionId}`; repetido `200`; `503 Recording.CapacityExceeded` |
| `POST /v1/sessions/:id/tracks` | `{trackId, producerId, userId, kind:'audio'\|'video', source:'camera'\|'screen', codec:{mimeType, payloadType, clockRate, channels?}, ssrc}` | `201 {ip, port, rtcpPort}` (repetido con la pista abierta `200` con los mismos puertos); `404 Recording.SessionNotFound`; `409 Recording.SessionClosed`; `409 Recording.VideoNotCaptured`; `409 Recording.TrackClosed`; `503 Recording.PortsExhausted` / `Recording.CaptureFailed` |
| `DELETE /v1/sessions/:id/tracks/:trackId` | — | `204` (idempotente) |
| `POST /v1/sessions/:id/stop` | `{stoppedAtUtc, reason, timeline}` | `202 {jobId}` (idempotente: mismo jobId); `404`; `409 Recording.SessionClosed` si la sesión ya falló/abortó |
| `POST /v1/sessions/:id/recompose` | `{timeline, composition}` | `202 {jobId}`; `409 Recording.TracksPurged` (sin spool); `409 Recording.SessionActive`; `409 Recording.CompositionInProgress` |
| `DELETE /v1/sessions/:id` | — | `204` (aborta y descarta; idempotente) |

Validación con zod; cuerpo inválido → `400 Recording.InvalidRequest`. `sessionId`/`trackId` solo `[A-Za-z0-9_-]{1,100}` (son nombres de carpeta/archivo). Códecs aceptados: `audio/opus`, `video/VP8`, `video/VP9` (los que WebM admite con `-c copy`). `ip` de la respuesta = `RECORDER_RTP_ANNOUNCED_IP` o `null` (Communication resuelve el host del recorder).

`timeline = {participants:[{userId, displayName, role, actorType}], events:[{seq, at, type, actorUserId, targetUserId, payload}]}`. Incluye las filas del `MeetingEventLog` **anteriores** al inicio: solo fijan el estado en t=0 (quién está, cámara/micro, mano, pantalla, host); únicamente lo ocurrido dentro de la ventana genera avisos. En llamadas los eventos pueden venir vacíos: las tiles salen de las pistas.

### Eventos publicados (fanout `RECORDER_RABBITMQ_EXCHANGE`, AMQP `type` = eventType, `messageId` = eventId, canal con confirmación)

- `communication.recording.composed.v1`: `{eventId, eventType, tenantId, correlationId, occurredOnUtc, sessionId, scope, scopeId, composedFileId, composedSizeBytes, format, durationSeconds, layout:'auto', audioTracks:[{trackId, userId, fileId, source, startOffsetMs, durationMs}], videoTrackCount, composedAtUtc}`.
- `communication.recording.composition_failed.v1`: `{…sobre, sessionId, scope, scopeId, reason:'NoTracks'|'ComposeError'|'UploadFailed'|'RecorderRestarted'|'Aborted', errorMessage}`.
- `SaveFileRequested` (cola `RECORDER_CLOUDSTORAGE_EXTERNAL_QUEUE`, PascalCase, `RequestingService='communication-recorder'`, `OwnerType='Communication'`, `OwnerId=scopeId`): pistas de audio (`RecordingTracks`, `audio/webm`, `Track_{userId}_{trackId}.webm`) y luego el compuesto (`Recordings`, `video/mp4|video/webm`, `Meeting_Recording_YYYY-MM-DD.mp4` / `Call_Recording_…`). Objeto en `{bucket}/{prefix}/{tenantId}/{sessionId}/{fileId}.{ext}`.

`correlationId` = cabecera `x-correlation-id` del `POST /v1/sessions` (o el sessionId).

## Composición

1. **Ventana**: de `startedAtUtc` a `stoppedAtUtc` (sin stop: fin de la última pista), siempre acotada por el corte duro (`maxDurationSeconds + toleranceSeconds`). Se alinea a frames.
2. **Segmentos de layout constante**: se corta solo cuando cambia quién ocupa las tiles (entradas/salidas) o la pantalla compartida. Todo lo demás cambia dentro del segmento con `enable='between(t,…)'`, así una reunión con mucha actividad no genera un segmento por evento.
   - Rejilla 16:9 centrada de hasta 9 tiles; con más, 8 + tile "+N". Orden de tiles = orden de entrada.
   - Pantalla compartida: pantalla al 78 % del ancho a la izquierda + tira vertical de participantes con "+N"; gana la pantalla iniciada más tarde; `screen.stopped` corta la pista.
   - Por tile: slate (iniciales + nombre) como base; el video se superpone solo mientras la cámara se ve (pista abierta y sin `media.changed videoEnabled=false`); etiqueta de nombre junto al video; "Muted" (`audioEnabled=false`, o sin pista de micro y sin estado conocido); "Hand raised" (`hand.raised` → `hand.lowered*`/salida); borde verde entre `speaker.started/stopped` (huecos < 750 ms se funden).
   - Lienzo: avisos arriba durante 4 s ("Ana joined", "Ana left", "Ana is now the host"), apilados si coinciden; insignia "N reactions" por minuto desde `reaction.summary`.
3. **Video por segmento** con el códec final (`libx264 veryfast crf 23` + `maxrate = videoKbps`, o `libvpx` en WebM); cada pista se alinea con `-ss` (si empezó antes) o `-itsoffset` (si empezó después) y `-reinit_filter 0` (los cambios de resolución de simulcast no reinician el grafo).
4. **Concat** de segmentos sin recodificar + **audio una sola vez** para toda la ventana: `aresample=48000:async=1` (rellena huecos de DTX/pausa), `adelay` por `startOffsetMs`, `amix normalize=0`. MP4 H.264 + AAC con `+faststart`, o WebM VP8 + Opus. `captureVideo=false` → solo audio (AAC en MP4 / Opus en WebM).
5. Grafo en `-filter_complex_script` (sin límite de línea de comandos). Texto con escapado de dos niveles y `expansion=none` (nombres con `'`, `:`, `,`, `[`, `%` probados). `-filter_complex_threads` y `-threads` = `RECORDER_COMPOSE_THREADS`; en Linux se envuelve con `nice -n RECORDER_COMPOSE_NICE`.

Los builders (`timeline-model`, `layout`, `filter-graph`) son puros y deterministas; las pruebas comparan el grafo exacto.

## Configuración

| Variable | Default | Uso |
|---|---|---|
| `RECORDER_PORT` | `5360` | API HTTP (+ `/metrics`) |
| `RECORDER_RABBITMQ_URI` | — (requerida) | RabbitMQ |
| `RECORDER_RABBITMQ_EXCHANGE` | `taxvision-events` | fanout de eventos |
| `RECORDER_CLOUDSTORAGE_EXTERNAL_QUEUE` | `cloudstorage-external-uploads` | cola de `SaveFileRequested` |
| `RECORDER_REDIS_URI` | — (requerida) | cola de composición |
| `RECORDER_REDIS_KEY_PREFIX` | `recorder` | claves `{prefix}:compose:queue` / `:processing` |
| `RECORDER_MINIO_ENDPOINT` / `_PORT` / `_USE_SSL` | — / `9000` / `false` | MinIO (`USE_SSL` compara el texto `"true"`) |
| `RECORDER_MINIO_ACCESS_KEY` / `_SECRET_KEY` | — (requeridas) | cuenta scoped del recorder |
| `RECORDER_MINIO_TEMP_BUCKET` | `taxvision-temp` | bucket temporal |
| `RECORDER_MINIO_SOURCE_PREFIX` | `recording` | prefijo de objetos |
| `RECORDER_AUTH_JWKS_URI` | — (requerida) | JWKS de Auth |
| `RECORDER_AUTH_ISSUER` / `_AUDIENCE` | `TaxVision.Auth` / `TaxVision.Services` | validación del JWT |
| `RECORDER_ALLOWED_CLIENT_IDS` | `communication-worker` | lista separada por comas |
| `RECORDER_RTP_LISTEN_IP` | `127.0.0.1` | IP del SDP (en contenedor `0.0.0.0`) |
| `RECORDER_RTP_ANNOUNCED_IP` | vacío | `ip` devuelta al abrir pista (vacío = `null`) |
| `RECORDER_RTP_PORT_MIN` / `_MAX` | `44000` / `44999` | pares RTP (par) / RTCP (+1) |
| `RECORDER_SPOOL_DIR` | `/var/spool/communication-recorder` | pistas + manifest |
| `RECORDER_FFMPEG_BIN_PATH` / `RECORDER_FFPROBE_BIN_PATH` | `ffmpeg` / `ffprobe` | binarios |
| `RECORDER_FONT_FILE` | `/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf` | fuente de drawtext |
| `RECORDER_CLOUDSTORAGE_BASE_URL` | `` (vacío) | base de `GET storage/avatars/{fileId}` para la foto del slate; vacío = iniciales |
| `RECORDER_AVATAR_TIMEOUT_MS` | `3000` | tope por descarga de foto |
| `RECORDER_AVATAR_MAX_BYTES` | `2097152` | tamaño máximo aceptado de una foto |
| `RECORDER_MAX_ACTIVE_SESSIONS` | `4` | sesiones capturando o esperando stop |
| `RECORDER_COMPOSE_THREADS` | `2` | hilos de ffmpeg al componer |
| `RECORDER_COMPOSE_NICE` | `10` | `nice` en Linux (0 = sin nice) |
| `RECORDER_ORPHAN_TIMEOUT_MINUTES` | `30` | tras el corte duro, espera del stop antes de componer igual |
| `RECORDER_CAPTURE_READY_DELAY_MS` | `400` | margen para que ffmpeg abra el socket antes de responder |
| `RECORDER_CAPTURE_STOP_TIMEOUT_MS` | `5000` | cierre ordenado antes del kill |
| `RECORDER_COMPOSE_TIMEOUT_FACTOR` / `_MIN_MINUTES` | `3` / `10` | tope de pared = max(mínimo, duración × factor) |
| `RECORDER_JANITOR_INTERVAL_MINUTES` | `60` | barrido de spools vencidos |
| `RECORDER_RETRY_MAX_ATTEMPTS` / `RECORDER_RETRY_BACKOFF_MS` | `4` / `1000,5000,30000` | reintentos de subida/publicación |
| `RECORDER_HTTP_BODY_LIMIT_BYTES` | `16777216` | el timeline de una reunión larga supera 1 MB |
| `RECORDER_SHUTDOWN_DRAIN_MS` | `60000` | espera del trabajo en curso al parar |

Métricas (`/metrics`, prom-client): `recorder_active_sessions`, `recorder_active_tracks`, `recorder_compose_queue_depth`, `recorder_compose_duration_seconds{format}`, `recorder_compose_realtime_factor`, `recorder_compositions_total{format}`, `recorder_composition_failures_total{reason}` + métricas por defecto del proceso.

## Ciclo de vida y garantías

- **Manifest** (`manifest.json`, escritura atómica): por pista `trackId, producerId, userId, kind, source, startOffsetMs` (apertura − `startedAtUtc`), `endedOffsetMs` (cierre), y tras subir `fileId`/`durationMs`. Estados: `capturing → limit_reached → queued → composing → composed | failed`, `aborted`.
- **Cierre de una captura**: RTCP compuesto RR + BYE al puerto RTCP del ffmpeg (el demuxer RTP da EOF y escribe el trailer aunque no lleguen paquetes; funciona en Windows y Linux) + `q` por stdin + SIGINT en POSIX; kill al vencer el plazo. Con `-flush_packets 1`, un kill solo pierde el último cluster.
- **Corte duro** a `maxDurationSeconds + toleranceSeconds`: cierra las capturas y sigue esperando el `stop`; sin `stop` en `RECORDER_ORPHAN_TIMEOUT_MINUTES`, compone con timeline de respaldo ("Participant N").
- **Cola de composición** en Redis (BLMOVE a "en curso", concurrencia 1, ack tras publicar). Al arrancar: lo que quedó en curso vuelve a la cola; sesiones que estaban capturando → `composition_failed {reason:'RecorderRestarted'}` (sus pistas quedan para `recompose`); sesiones encoladas sin trabajo en Redis → se reencolan.
- **Idempotencia**: si la publicación final falla, el trabajo se reencola y el siguiente intento no recompone ni resube (usa el `result` del manifest). Al recomponer se reutilizan los `fileId` de las pistas de audio ya subidas.
- **Spool**: el compuesto local se borra tras subirlo; las pistas + manifest quedan hasta `tracksRetentionDays` desde la última composición (o el fallo). El janitor (cada hora) borra lo vencido. Recomponer sin spool → `409 Recording.TracksPurged`.
- `DELETE /v1/sessions/:id` sobre una sesión ya encolada publica `composition_failed {reason:'Aborted'}`; sobre una que capturaba no publica nada (Communication ya lo sabe).

## Límites conocidos

- **Una sola réplica**: el spool y los puertos RTP son locales al contenedor; la cola de Redis no reparte trabajo entre instancias.
- **Keyframes**: cada segmento busca (`-ss`) en las pistas de video; Communication debe pedir keyframes periódicos al consumer (el PoC lo hacía cada 5 s) para que la búsqueda no decodifique desde el inicio de la pista.
- ffmpeg enlaza el socket **RTP** en la IP del SDP, pero el **RTCP** en todas las interfaces (limitación del demuxer SDP; observado en Windows con ffmpeg 8.1).
- Si la relación de aspecto de una pista cambia a mitad (p. ej. ventana compartida redimensionada), con `-reinit_filter 0` el escalado mantiene el tamaño del primer frame (se deforma, no se corta).
- Debian bookworm trae ffmpeg 5.1; los filtros usados (`drawtext expansion`, `overlay eof_action`, `adelay all`, `amix normalize`, `scale force_divisible_by`, `-filter_complex_script`) existen desde 4.4/5.0, pero la integración solo se ejecutó con ffmpeg 8.1 en Windows.
- Reserva de cuota de CloudStorage al iniciar: no existe API M2M (ver F7_CONTRACTS §8); Communication cubre el rechazo con `StorageRejected`.

## Runbook

```bash
npm install
npm run typecheck && npm run lint && npm test && npm run build
npm run dev                      # local (lee .env)

# Composición real con ffmpeg (fuera de npm test):
RECORDER_IT_FFMPEG=/ruta/ffmpeg RECORDER_IT_FFPROBE=/ruta/ffprobe RECORDER_IT_FONT=/ruta/fuente.ttf \
  RECORDER_IT_KEEP=1 npm run test:integration
```

- **Local (flota)**: `scripts/start-fleet.ps1` lo arranca con `npm run dev` en el puerto **5365** (el 5360 lo usa Subscription). `.env` local reutiliza RabbitMQ/Redis/MinIO del transcript worker y el JWKS/issuer de Communication; como la cuenta MinIO del worker solo puede escribir `taxvision-temp/transcript/*`, el prefijo local es `transcript/recording`. Rango RTP local 50000–50999 (fuera del rango de respaldo de mediasoup, que comparte host). Spool en `./.spool`.
- **Sesión colgada en `limit_reached`**: espera el `stop`; a los `RECORDER_ORPHAN_TIMEOUT_MINUTES` compone sola.
- **Trabajo atascado en `recorder:compose:processing`**: se recupera solo al reiniciar el contenedor.
- **Ver qué pasó con una sesión**: `{spool}/{sessionId}/manifest.json` (estado, `failure`, `result`) y `{trackId}.log` (stderr del ffmpeg de captura).
- **Recomponer** (otro layout/formato): Communication llama `POST /v1/sessions/:id/recompose` mientras el spool exista.
