# communication-transcript-worker

Worker separado (Node 24 + whisper.cpp + ffmpeg) que transcribe grabaciones y notas de voz de
Communication. Proceso propio para no competir por CPU con el realtime de Communication. Es un
pipeline genérico: lo que varía por dominio es solo el contrato de mensajería
(`TRANSCRIPT_WORKER_RECORDING_KINDS`).

## Flujo (F7)

```
taxvision-events ──trigger──► worker
  ├─ con audioTracks (reuniones/llamadas server-side, T-28):
  │    por pista: CloudStorage download → ffprobe (audio, duración, TooLong) → ffmpeg (filtro) →
  │    whisper-cli -oj → segmentos desplazados por startOffsetMs y etiquetados con el nombre
  │    → entrelazado por tiempo
  ├─ sin pistas: archivo único (recordingFileId), segmentos sin hablante
  ├─ salida file   → TXT ([hh:mm:ss] Nombre: texto) + JSON v2 a CloudStorage `Transcripts`
  │                  (MinIO temp + SaveFileRequested) → communication.{meeting|call}.transcript_ready.v1 v2
  └─ salida inline → sin subida → evento ready con transcriptText (notas de voz, D-27)
cualquier stage que falla → …transcript_failed.v1 {failureReason} + nack a la DLQ
```

- Una pista sin audio se omite; si ninguna tiene audio → `NoAudioStream`.
- Las pistas se procesan de a una y sus temporales se borran al terminar cada una.
- `failureReason`: `NoAudioStream`, `DownloadFailed`, `FfmpegError`, `WhisperError`, `UploadFailed`,
  `Timeout` (whisper/ffmpeg vencidos o fallo al publicar el ready), `TooLong`.
- Reintentos solo en download (5xx; `File.NotAvailable` con política larga propia porque
  CloudStorage escanea de a un archivo) y upload (errores de red/5xx de MinIO).

### Contratos (fuente: F7_CONTRACTS §9.3/§9.4)

Trigger de meeting/call (`recording_processing_started.v1`), campos nuevos opcionales:
`audioTracks:[{fileId, userId, displayName, startOffsetMs}]`, `language` (`auto`|código),
`prompt`, `maxDurationSeconds`, `timeoutFactorPct`.

`transcript_ready.v1` v2 añade: `transcriptJsonFileId`, `segmentCount`, `speakerCount`, `model`
(nombre del archivo del modelo), `vad`, `realtimeFactor` (segundos de whisper / segundos de
audio transcrito), `wordsPerSecond`.

JSON v2 (`application/json`):
`{version:2, language, model, vad, durationSeconds, speakers:[{userId, displayName}], segments:[{startMs, endMs, speakerUserId|null, speakerName|null, text}]}`.

Notas de voz: trigger `communication.chat.voice_note_transcription_requested.v1`
`{messageId, conversationId, fileId, durationMs, language, prompt}` → ready
`communication.chat.voice_note_transcript_ready.v1`
`{messageId, conversationId, transcriptText (≤ 4000), detectedLanguage, durationSeconds, model}` /
failed `communication.chat.voice_note_transcript_failed.v1 {messageId, conversationId, failureReason, errorMessage}`.

### Recording kinds

`RecordingKindMapping`: `kind`, `targetIdField`, `triggerEventTypes`, `transcriptReadyEventType`,
`transcriptFailedEventType`, `recordingFileIdField` (default `recordingFileId`), `output`
(`file` | `inline`, default `file`), `passthroughFields` (campos del trigger que vuelven tal cual en
ready/failed, default `[]`). Default sin variable: `call`, `meeting` (file) y `voice-note`
(inline, `messageId` / `fileId`, devuelve `conversationId`). Cualquier kind puede ser inline.

## whisper.cpp y modelos

- Versión **v1.9.2** (D-26), igual que la local (`whisper-cli --version`). Compilada en el
  Dockerfile con `BUILD_SHARED_LIBS=ON`; en x86_64 con `GGML_BACKEND_DL` +
  `GGML_CPU_ALL_VARIANTS` (ggml elige en runtime la variante `libggml-cpu-*.so` del CPU real); en
  arm64 con `GGML_CPU_ARM_ARCH` (`WHISPER_ARM64_ARCH`, default `armv8.2-a+dotprod+fp16`). Todas las
  `.so` quedan en `/opt/whisper.cpp/build/bin` junto a `whisper-cli` y `whisper-bench`.
- Modelos por build-arg, con SHA-256 verificado:
  - `WHISPER_MODEL=ggml-large-v3-turbo-q5_0.bin` (Hugging Face `ggerganov/whisper.cpp`; 574 MB,
    ~1,0–1,4 GB de RAM pico).
  - `WHISPER_VAD_MODEL=ggml-silero-v6.2.0.bin` (Hugging Face `ggml-org/whisper-vad`).
- **Por qué turbo (INV-T01):** las oficinas hablan inglés y español; `small` pierde demasiado en
  español y en el cambio de idioma. Turbo es clase large en ambos con un costo de CPU cercano a
  medium (≈ 3–4× small). Para volver a otro modelo:
  `--build-arg WHISPER_MODEL=<archivo> --build-arg WHISPER_MODEL_SHA256=<sha|"">`.
  `whisper-bench` va en la imagen para medir ahí mismo
  (`/opt/whisper.cpp/build/bin/whisper-bench -m /opt/whisper.cpp/models/<modelo> -t <hilos>`);
  la métrica `transcript_worker_realtime_factor` da el dato real en producción.

Línea de comandos que arma `buildWhisperArgs` (función pura, testeada):

```
whisper-cli -m <modelo> -f <wav> -oj -of <prefijo> -l <idioma> [--prompt "<prompt>" [--carry-initial-prompt]]
            -mc <maxContext> -et <entropía> -bs <beam> -t <hilos> [-sns]
            [--vad --vad-model <silero> -vt <umbral> -vp <margen ms> -vsd <silencio ms>]
```

Idioma: el de la pista (`audioTracks[].language`, el que eligió ese participante para su voz; la
pantalla compartida va en `auto`) y, si no trae, el del evento (oficina); el env es el respaldo; sin
ninguno, `auto`. **No forzar un idioma a quien habla otro:** Whisper lo traduce en vez de
transcribirlo. En notas de voz el evento ya trae el idioma de quien la grabó.

**Idiomas permitidos:** en `auto`, whisper decide con los primeros 30 s y en clips cortos confunde
español con italiano o portugués (notas de voz de 4–6 s salieron en italiano y en portugués). Si el
idioma detectado no está en `TRANSCRIPT_WORKER_WHISPER_ALLOWED_LANGUAGES`, se repite la transcripción
forzando el permitido más parecido (`_LANGUAGE_FALLBACKS`: pt/it/gl/ca/fr/ro → es, nl/de/cy → en y el
resto → es: medido con turbo, el español corto sale como it/pt/is y el inglés se reconoce con p ≈ 0,99). No se desempata por confianza: forzado a inglés, whisper traduce el español con mucha
seguridad y "gana". Solo cuesta de más cuando la detección falla; se cuenta en
`transcript_worker_language_corrections_total{detected,chosen}`.

Prompt: el del evento (oficina) o el del env. Con `-mc 64` solo caben ~60 tokens (unos 190
caracteres): conviene un glosario corto de nombres, siglas y anglicismos, sin frases. Con
`--carry-initial-prompt` se repite en cada ventana de 30 s.

whisper-cli corre con `nice` (`TRANSCRIPT_WORKER_WHISPER_NICE`) para no quitarle CPU a las llamadas
y reuniones en vivo de la misma VM; el compose le pone `mem_limit` (`TRANSCRIPT_WORKER_MEM_LIMIT`,
3g).

## Configuración

Requeridas (sin default): `TRANSCRIPT_WORKER_RABBITMQ_URI`, `TRANSCRIPT_WORKER_REDIS_URI`,
`TRANSCRIPT_WORKER_AUTH_BASE_URL`, `TRANSCRIPT_WORKER_SERVICE_AUTH_CLIENT_SECRET`,
`TRANSCRIPT_WORKER_CLOUDSTORAGE_BASE_URL`, `TRANSCRIPT_WORKER_MINIO_ENDPOINT`,
`TRANSCRIPT_WORKER_MINIO_ACCESS_KEY`, `TRANSCRIPT_WORKER_MINIO_SECRET_KEY`.

| Variable | Default | Uso |
|---|---|---|
| `TRANSCRIPT_WORKER_WHISPER_BIN_PATH` | `/opt/whisper.cpp/build/bin/whisper-cli` | Binario |
| `TRANSCRIPT_WORKER_WHISPER_MODEL_PATH` | `/opt/whisper.cpp/models/ggml-large-v3-turbo-q5_0.bin` | Modelo (su nombre va en el evento) |
| `TRANSCRIPT_WORKER_WHISPER_VAD_MODEL_PATH` | vacío (la imagen lo fija a Silero) | Vacío = VAD apagado |
| `TRANSCRIPT_WORKER_WHISPER_VAD_THRESHOLD` | `0.5` | `-vt` |
| `TRANSCRIPT_WORKER_WHISPER_VAD_SPEECH_PAD_MS` | `300` | `-vp`; el default de whisper.cpp (30) corta el borde de las palabras |
| `TRANSCRIPT_WORKER_WHISPER_VAD_MIN_SILENCE_MS` | `500` | `-vsd`; el default (100) parte frases en cada pausa |
| `TRANSCRIPT_WORKER_WHISPER_CARRY_INITIAL_PROMPT` | `true` | `--carry-initial-prompt` (solo con prompt) |
| `TRANSCRIPT_WORKER_WHISPER_NICE` | `10` | Prioridad de whisper-cli (0–19); 0 = sin cambio |
| `TRANSCRIPT_WORKER_WHISPER_LANGUAGE` | vacío (= `auto`) | Respaldo si el evento no trae idioma |
| `TRANSCRIPT_WORKER_WHISPER_ALLOWED_LANGUAGES` | `en,es` | Idiomas de las oficinas. En `auto`, si whisper detecta otro, decide solo entre estos; vacío = cualquiera |
| `TRANSCRIPT_WORKER_WHISPER_LANGUAGE_FALLBACKS` | `pt:es,gl:es,it:es,ca:es,fr:es,ro:es,oc:es,la:es,nl:en,de:en,cy:en,*:es` | A qué idioma permitido va cada detección equivocada (`*` = el resto) |
| `TRANSCRIPT_WORKER_WHISPER_PROMPT` | vacío | Respaldo si el evento no trae prompt |
| `TRANSCRIPT_WORKER_WHISPER_MAX_CONTEXT` | `64` | `-mc` (anti-repetición) |
| `TRANSCRIPT_WORKER_WHISPER_ENTROPY_THOLD` | `2.6` | `-et` (2,6–2,8) |
| `TRANSCRIPT_WORKER_WHISPER_BEAM_SIZE` | `5` | `-bs` |
| `TRANSCRIPT_WORKER_WHISPER_SUPPRESS_NST` | `true` | `-sns` |
| `TRANSCRIPT_WORKER_WHISPER_THREADS` | `0` | `-t`; 0 = núcleos − 1 |
| `TRANSCRIPT_WORKER_AUDIO_FILTER` | `highpass=f=80,aresample=resampler=soxr` | `-af` de ffmpeg; vacío = sin filtro. Sin loudnorm ni reducción de ruido: suben el ruido de las pausas y el WER |
| `TRANSCRIPT_WORKER_CONCURRENCY` | `1` | Mensajes en paralelo (whisper ya usa todos los hilos) |
| `TRANSCRIPT_WORKER_MIN_TIMEOUT_SECONDS` | `600` | Timeout por corrida = max(este, maxDuration × timeoutFactorPct/100) |
| `TRANSCRIPT_WORKER_DURATION_TOLERANCE_SECONDS` | `120` | Duración > maxDuration + esto ⇒ `TooLong` |
| `TRANSCRIPT_WORKER_WPS_MIN` / `_MAX` | `0.3` / `5` | Rango de palabras/segundo; fuera ⇒ log warn + métrica |
| `TRANSCRIPT_WORKER_WPS_MIN_DURATION_SECONDS` | `30` | Clips más cortos no se evalúan |
| `TRANSCRIPT_WORKER_INLINE_TEXT_MAX_CHARS` | `4000` | Tope de `transcriptText` (inline) |
| `TRANSCRIPT_WORKER_RETRY_MAX_ATTEMPTS` | `4` | Download 5xx / upload de red |
| `TRANSCRIPT_WORKER_RETRY_BACKOFF_MS` | `1000,5000,30000` | Backoff (el último se repite) |
| `TRANSCRIPT_WORKER_NOT_AVAILABLE_RETRY_MAX_ATTEMPTS` | `15` | `File.NotAvailable` (escaneo ClamAV) |
| `TRANSCRIPT_WORKER_NOT_AVAILABLE_RETRY_BACKOFF_MS` | `5000,10000,20000,30000,60000` | Backoff de `File.NotAvailable` |
| `TRANSCRIPT_WORKER_FFMPEG_BIN_PATH` / `_FFPROBE_BIN_PATH` | `ffmpeg` / `ffprobe` | Binarios |
| `TRANSCRIPT_WORKER_FFPROBE_TIMEOUT_SECONDS` | `30` | Timeout de ffprobe |
| `TRANSCRIPT_WORKER_RECORDING_KINDS` | vacío (= default) | JSON `RecordingKindMapping[]` |
| `TRANSCRIPT_WORKER_TEMP_DIR` | `/tmp/transcript-worker` | Temporales por evento |
| `TRANSCRIPT_WORKER_METRICS_PORT` | `9464` | `/metrics` |

## Métricas (`/metrics`)

- `transcript_worker_pipeline_duration_seconds{kind,status}` y
  `transcript_worker_pipeline_failures_total{reason,kind}` (incluye `reason="TooLong"`).
- `transcript_worker_realtime_factor{kind,model}` (histograma; criterio D-26 ≤ 0,5).
- `transcript_worker_wps_anomalies_total{kind,direction}` (`low` = grabación vacía, `high` = alucinación).
- `transcript_worker_too_long_total{kind}`.
- `transcript_worker_language_corrections_total{detected,chosen}` (idioma detectado fuera de los permitidos y corregido).

## Desarrollo local

- `.env` local con rutas Windows (whisper.cpp 1.9.2 en `C:\Users\devcacg\whisper.cpp`, ffmpeg Gyan
  full con libsoxr). VAD apagado hasta descargar `ggml-silero-v6.2.0.bin` a
  `C:\Users\devcacg\whisper.cpp\models`.
- Gates: `npm run typecheck`, `npm run lint`, `npm test`, `npm run build`.

## Medir la exactitud (`tools/wer`)

Cada cambio de modelo, de versión de whisper.cpp, de preprocesado o de idioma se juzga por números
(INV-T01 §7). La herramienta es Python offline y no entra en la imagen:

```
cd tools/wer
python -m venv .venv && .venv/Scripts/pip install -r requirements.txt   # Linux: .venv/bin/pip
.venv/Scripts/python measure_wer.py --manifest sample/manifest.json --glossary sample/glosario.txt
.venv/Scripts/python -m unittest test_measure_wer
```

- **Corpus:** pistas originales del worker (con consentimiento) y una referencia escrita a mano por
  pista: `inicio-fin [EN|ES|MIX] texto`, lo que se dijo en el idioma en que se dijo. El corpus real va
  fuera del repo; `sample/` solo enseña el formato.
- **Hipótesis:** el JSON v2 que sube el worker (filtrable por `speakerUserId`) o `whisper-cli -oj`.
- **Mide:** WER y CER total y por idioma (EN, ES, MIX), también sin tildes; tasa de segmentos que
  salen en el otro idioma (traducidos); palabras inventadas en silencio; recall del glosario.
- **Comparar:** `--out base.json` en la línea base y `--compare base.json` en la corrida nueva
  imprime ADOPTAR / NO ADOPTAR: baja el WER total ≥ 5 % relativo (o el glosario +10 puntos) sin
  empeorar ningún idioma más de 2 %, sin subir la traducción ni lo inventado en silencio.
