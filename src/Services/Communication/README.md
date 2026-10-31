# TaxVision Communication

Microservicio de tiempo real de TaxVision — chat, llamadas 1:1 audio/video, meetings multi-participante, notificaciones in-app y soporte cross-tenant. **Único servicio en Node.js/TypeScript** del stack; el resto son .NET 10.

## Stack

| Capa | Tecnología |
|---|---|
| Runtime | Node.js ≥ 20.11 + TypeScript strict |
| HTTP | Fastify 5 (+ helmet, cors, rate-limit, sensible) |
| Realtime | Socket.IO 4 + `@socket.io/redis-adapter` |
| Persistencia | Prisma 5 sobre SQL Server (`TaxVision_Communication`) |
| Bus de eventos | RabbitMQ (`amqplib`) al exchange `taxvision-events` |
| Cache / lock / presencia | Redis (`ioredis`) |
| Auth | Verificación JWT RS256 vía JWKS público de Auth (`jose`) |
| Validación | Zod en boundaries + branded types en dominio |
| Logs | Pino JSON estructurado |
| Observabilidad | OpenTelemetry SDK Node → OTLP |
| Testing | Vitest |

## Layout DDD

```
src/
├── domain/
│   ├── conversations/   # Conversation + Message aggregates
│   ├── calls/           # Call aggregate (audio/video 1:1)
│   ├── meetings/        # Meeting aggregate (multi-participante)
│   ├── notifications/   # Notification aggregate
│   ├── support/         # SupportTicket aggregate (cross-tenant)
│   └── settings/        # TenantCommunicationSettings
├── application/
│   ├── use-cases/       # Comandos / consultas
│   └── consumers/       # Handlers de integration events entrantes
├── infrastructure/
│   ├── prisma/          # PrismaClient + outbox
│   ├── socket/          # Socket.IO namespaces + handlers
│   ├── rabbit/          # RabbitMQ publisher + consumer
│   ├── redis/           # Adapter, denylist, locks, presencia
│   ├── jwks/            # Verificación JWT
│   └── otel/            # OpenTelemetry
├── api/
│   ├── http/            # Rutas Fastify REST
│   └── socket/          # Namespaces Socket.IO
└── contracts/
    └── events/          # Integration events (ver tabla abajo)
```

## Cómo correr en dev

```powershell
cd src/Services/Communication
npm install
npx prisma generate
copy .env.example .env
# Editar .env: COMMUNICATION_DB_CONNECTION, JWKS_URI, RABBIT_URL, REDIS_URL, etc.
npx prisma migrate dev --name init
npm run dev
```

Salida: HTTP en `http://localhost:5350`, Socket.IO en `ws://localhost:5350/communication/socket.io`.

## Health checks

| Endpoint | Qué verifica |
|---|---|
| `GET /health/live` | Proceso vivo |
| `GET /health/ready` | SQL Server + Redis + RabbitMQ disponibles |

## Autorización

- **HTTP**: `Authorization: Bearer <accessToken>` verificado contra JWKS de Auth (rota sin redeploy).
- **Socket.IO**: `handshake.auth.token` (nunca `handshake.query` — cierra CRIT del legacy).
- **Denylist Redis** (`auth:session-denylist`) compartido con Auth: la revocación es inmediata, sin esperar a que expire el JWT.
- **Sesión revocada**: el socket emite `session.revoked` en su propio canal y luego se desconecta — el frontend DEBE hacer logout sin preguntar.

## Endpoints HTTP

### Conversaciones / Chat

| Método | Ruta | Descripción |
|---|---|---|
| `POST` | `/communication/conversations` | Crear conversación (Direct/Group/Support) |
| `GET` | `/communication/conversations` | Listar conversaciones; `includeArchived=true|false` es estricto |
| `GET` | `/communication/conversations/:id` | Detalle + últimos mensajes |
| `POST` | `/communication/conversations/:id/messages` | Enviar mensaje (fallback HTTP; preferir socket) |
| `PATCH` | `/communication/conversations/:id/messages/:msgId` | Editar mensaje |
| `DELETE` | `/communication/conversations/:id/messages/:msgId` | Eliminar mensaje (soft delete) |
| `POST` | `/communication/conversations/:id/participants` | Añadir participante a grupo |
| `DELETE` | `/communication/conversations/:id/participants/:userId` | Remover participante |
| `POST` | `/communication/conversations/:id/read` | Marcar mensajes como leídos hasta `untilMessageId` |

### Llamadas (WebRTC 1:1)

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/communication/webrtc/ice` | Obtener ICE servers (STUN/TURN) con TTL |

### Meetings

| Método | Ruta | Descripción |
|---|---|---|
| `POST` | `/communication/meetings` | Crear meeting |
| `GET` | `/communication/meetings` | Listar meetings del tenant |
| `GET` | `/communication/meetings/:id` | Detalle + participantes + estado |
| `POST` | `/communication/meetings/:id/invitations` | Generar link de invitación |
| `PATCH` | `/communication/meetings/:id` | Actualizar título / schedule |
| `DELETE` | `/communication/meetings/:id` | Cancelar meeting |

### Notificaciones

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/communication/notifications` | Listar notificaciones; `unreadOnly=true|false` es estricto |
| `GET` | `/communication/notifications/unread-count` | Contador de no leídas con `asOfUtc` para descartar respuestas obsoletas |
| `POST` | `/communication/notifications/:id/read` | Marcar como leída y confirmar por socket |
| `POST` | `/communication/notifications/read-all` | Marcar atómicamente las creadas hasta `upToCreatedAtUtc` y confirmar por socket |

**Puente de in-app (quién crea la fila).** Communication consume `notification.inapp.requested.v1` (emitido por Notification .NET) y crea/agrupa la fila de la campana. Para los hechos de negocio que históricamente creaba Communication directamente, un flag por grupo decide la fuente: `COMMUNICATION_INAPP_SOURCE_<GRUPO>` = `Communication` (default) o `Notification`. Con `Notification`, el handler de ese grupo deja de llamar `pushNotification` (lo crea el consumer .NET) pero mantiene su realtime propio (proyecciones, pings de lista). El dedupe `(TenantId, SourceEventId, UserId)` evita duplicados si ambos corren un instante. Grupos migrables hoy: `CUSTOMER_IMPORT`, `CONNECTORS`, `CLOUDSTORAGE_LEGAL`, `SUPPORT`. `SIGNATURE` espera a Fase 7 (notifica además al cliente del portal, que requiere la resolución `CustomerPortal → UserId`).

### Soporte cross-tenant

| Método | Ruta | Descripción |
|---|---|---|
| `POST` | `/communication/support` | Abrir ticket (desde tenant cliente) |
| `GET` | `/communication/support` | Listar tickets; `includeClosed` y `mine` aceptan `true|false` |
| `POST` | `/communication/support/:id/claim` | Agente reclama el ticket |
| `POST` | `/communication/support/:id/reassign` | Agente reasigna (`{ newAgentUserId: UUID }`) |
| `POST` | `/communication/support/:id/escalate` | Escalar prioridad (`{ newPriority: 'Low'\|'Normal'\|'High'\|'Urgent' }`) |
| `POST` | `/communication/support/:id/resolve` | Resolver ticket |
| `POST` | `/communication/support/:id/close` | Cerrar ticket |
| `POST` | `/communication/support/:id/reopen` | Reabrir ticket (`{ reason?: string }`) |

### Settings

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/communication/settings` | Leer settings del tenant (límites de plan) |
| `PATCH` | `/communication/settings` | Actualizar settings (admin only) |

### Analytics

| Método | Ruta | Descripción |
|---|---|---|
Todo sale del snapshot diario `CommunicationAnalyticsSnapshot` (una fila por oficina y día), alimentado
solo por eventos de integración; ningún reporte escanea tablas operacionales. Los días se cortan en la
zona de la plataforma (`analyticsTimeZone`) y cada fila guarda el plan de la oficina ese día.

**Oficina** (`communication.analytics.read`, el tenant del token):

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/communication/analytics/summary` | Totales del rango `?from=&to=` (YYYY-MM-DD, por defecto 30 días) |
| `GET` | `/communication/analytics/timeline` | Filas por día del rango |

**Plataforma** (F10, ADD-08; solo PlatformAdmin con `communication.platform.analytics.read`; sin UI, D-36;
cada consulta queda en `ConfigurationAudit` con scope `AdminQuery`):

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/communication/admin/analytics/summary` | Totales de todas las oficinas y periodos `?from=&to=&groupBy=day\|week\|month&tenantId=&planCode=`; semana ISO y mes agregados en el servidor; picos = máximo de una oficina en un día; derivados (duración media, tasas de fallo, invitados) |
| `GET` | `/communication/admin/analytics/tenants` | Una fila por oficina con su plan, totales, picos y derivados `?from=&to=&planCode=&limit=` |
| `GET` | `/communication/admin/analytics/live` | Reuniones en vivo, participantes y sockets ahora mismo, por oficina y por proceso (latido en Redis) |
| `GET` | `/communication/admin/analytics/infra` | Uso del SFU por proceso y worker (`getResourceUsage`: CPU, RSS), salas, workers en respaldo y enlace a Grafana si está configurado |

Contadores del snapshot: mensajes y conversaciones; llamadas por tipo (audio/video/upgrade) y resultado
(completadas, fallidas por ICE, rechazadas, canceladas, perdidas, cortadas por límite) con segundos exactos;
reuniones agendadas/iniciadas/terminadas/canceladas/vencidas, fin por sistema, mesh vs SFU, segundos de
reunión y de participante; participantes, invitados, reconexiones, caídas por gracia, fallos de transporte y
reportes de calidad por nivel; grabaciones (pedidas, listas, fallidas, segundos, bytes del compuesto) y
transcripts (listos, fallidos, segundos de audio y de proceso); invitaciones (creadas, usadas, vencidas,
revocadas); picos de reuniones y participantes simultáneos.

Retención (configuración de plataforma, `PUT /communication/admin/platform-settings`): `analyticsRetentionDays`
(snapshot, 730 por defecto) y `callsRetentionDays` / `meetingsRetentionDays` / `invitationsRetentionDays`
(histórico; `null` = no se purga, por defecto). La purga nunca borra una llamada o reunión que aún tenga
grabación o transcript con archivos.

Observabilidad: `/metrics` (prom-client) expone sockets, salas y participantes del SFU, fallos de transporte
y proyecciones de analítica fallidas. Prometheus + Grafana están definidos y apagados en el perfil de compose
`observability` (`docker compose --profile observability up -d`); la plataforma no depende de ellos.

## Socket.IO — Events

Namespace: `/communication/socket.io`. Autenticación vía `handshake.auth.token`.

### Sesión (canal global)

| Evento | Dirección | Payload |
|---|---|---|
| `session.revoked` | S→C | `{ reason: string }` — logout inmediato |
| `user.profile.changed` | S→C | `{ userId, displayName, avatarFileId }` — cambió el nombre o la foto (staff por `auth.user.profile_updated.v1`; cliente del portal cuando cambia la foto de su ficha). Va a `t:{tenant}:staff` y a `t:{tenant}:u:{userId}`, nunca al tenant entero. Imagen: `/storage/avatars/{avatarFileId}`; null = iniciales |

> **Fotos (F5 avatares).** Los DTOs que muestran personas traen `avatarFileId` resuelto al leer (staff → `UserDirectoryEntry`, cliente del portal → `CustomerPortalAccount` → `CustomerDirectoryEntry`): participantes de `GET /conversations`, `senderAvatarFileId` del historial, directorio de empleados/clientes, snapshot de meetings (join/rejoin/admit), `callerAvatarFileId` de `call.incoming` y `avatarFileId` del peer al aceptar. **Campo ausente = no resuelto, conservar lo que se tenga; `null` = sin foto.** Las notificaciones no llevan foto del actor.

> **Reconciliaciones contra Auth** (Redis lock, al arrancar + cada 12 h, M2M con `COMMUNICATION_AUTH_BASE_URL`): `UserDirectoryEntry` desde `internal/users/directory/reconciliation` (`COMMUNICATION_USER_RECONCILE_*`) y `CustomerPortalAccount` desde `internal/users/portal-accounts/reconciliation` (`COMMUNICATION_PORTAL_ACCOUNT_RECONCILE_*`; activa → upsert, inactiva → baja solo la fila de ese usuario, nunca borra).

### Chat

| Evento | Dirección | Payload clave |
|---|---|---|
| `chat.message.send` | C→S | `{ conversationId, body, kind, clientKey }` |
| `chat.message.edit` | C→S | `{ conversationId, messageId, newBody }` |
| `chat.message.delete` | C→S | `{ conversationId, messageId }` |
| `chat.message.read` | C→S | `{ conversationId, untilMessageId }` |
| `chat.typing.start` | C→S | `{ conversationId }` |
| `chat.typing.stop` | C→S | `{ conversationId }` |
| `chat.message.new` | S→C | `MessageDto` |
| `chat.message.edited` | S→C | `{ conversationId, messageId, newBody, editedAt }` |
| `chat.message.deleted` | S→C | `{ conversationId, messageId, deletedAt }` |
| `chat.message.read` | S→C | `{ conversationId, readByUserId, untilMessageId }` |
| `chat.typing.started` | S→C | `{ conversationId, userId }` |
| `chat.typing.stopped` | S→C | `{ conversationId, userId }` |
| `chat.presence.changed` | S→C | `{ userId, status: 'Online'\|'Busy'\|'Offline' }`; staff y clientes asignados |

### Llamadas (WebRTC 1:1)

| Evento | Dirección | Payload clave |
|---|---|---|
| `call.initiate` | C→S | `{ calleeUserId, kind: 'Audio'\|'Video' }` |
| `call.accept` | C→S | `{ callId }` |
| `call.reject` | C→S | `{ callId }` |
| `call.end` | C→S | `{ callId }` |
| `call.signal` | C→S | `{ callId, signal: RTCSessionDescriptionInit\|RTCIceCandidateInit }` |
| `call.media_status` | C→S | `{ callId, audio: bool, video: bool, screenShare: bool }` |
| `call.incoming` | S→C | `{ callId, callerUserId, kind, ringingAtUtc }` |
| `call.accepted` | S→C | `{ callId, acceptedAtUtc }` |
| `call.rejected` | S→C | `{ callId, rejectedByUserId }` |
| `call.ended` | S→C | `{ callId, endReason, durationSeconds }` |
| `call.signal` | S→C | `{ callId, fromUserId, signal }` |
| `call.media_status_changed` | S→C | `{ callId, userId, audio, video, screenShare }` |
| `call.ice_failed` | S→C | `{ callId }` |

### Meetings

| Evento | Dirección | Payload clave |
|---|---|---|
| `meeting.join` | C→S | `{ meetingId, passcode? }` |
| `meeting.leave` | C→S | `{ meetingId }` |
| `meeting.signal` | C→S | `{ meetingId, toUserId, signal }` |
| `meeting.raise_hand` | C→S | `{ meetingId, raised: bool }` |
| `meeting.host.admit` | C→S | `{ meetingId, userId }` (host only) |
| `meeting.host.deny` | C→S | `{ meetingId, userId }` (host only) |
| `meeting.host.remove` | C→S | `{ meetingId, userId }` (host only) |
| `meeting.host.mute_all` | C→S | `{ meetingId }` (host only) |
| `meeting.host.lock` | C→S | `{ meetingId, locked: bool }` (host only) |
| `meeting.host.transfer` | C→S | `{ meetingId, newHostUserId }` (host only) |
| `meeting.host.recording_start` | C→S | `{ meetingId }` (host only) |
| `meeting.host.recording_stop` | C→S | `{ meetingId }` (host only) |
| `meeting.participant.joined` | S→C | `{ meetingId, participant: ParticipantDto }` |
| `meeting.participant.left` | S→C | `{ meetingId, userId }` |
| `meeting.participant.admitted` | S→C | `{ meetingId, userId }` |
| `meeting.participant.denied` | S→C | `{ meetingId, userId }` |
| `meeting.participant.removed` | S→C | `{ meetingId, userId, byHostUserId }` |
| `meeting.participant.muted_by_host` | S→C | `{ meetingId, userId }` |
| `meeting.host.changed` | S→C | `{ meetingId, newHostUserId }` |
| `meeting.locked` | S→C | `{ meetingId, locked: bool }` |
| `meeting.signal` | S→C | `{ meetingId, fromUserId, signal }` |
| `meeting.dominant_speaker` | S→C | `{ meetingId, userId }` |
| `meeting.hand_raised` | S→C | `{ meetingId, userId, raised: bool }` |
| `meeting.recording.started` | S→C | `{ meetingId, startedAtUtc }` |
| `meeting.recording.stopped` | S→C | `{ meetingId, stoppedAtUtc }` |
| `meeting.recording.ready` | S→C | `{ meetingId, fileUrl, durationSeconds }` |

#### Chat de la sala

Solo para participantes `Joined` (la sala de espera, los rechazados y los expulsados no entran a la room del chat). El invitado no tiene JWT, por eso el historial y los adjuntos también van por el socket.

| Evento | Dirección | Payload |
|---|---|---|
| `meeting.chat.send` | C→S (ack) | `{ clientKey, meetingId, body? , attachmentFileId? }` — el adjunto se sube antes por la ruta mediada de `/attachments` y la reserva se consume aquí; invitados solo texto (D-14); topes D-15 `Meeting.Chat.AttachmentRateLimited` / `Meeting.Chat.AttachmentLimit` |
| `meeting.chat.edit` / `meeting.chat.delete` / `meeting.chat.mark_read` | C→S (ack) | `{ meetingId, messageId, … }` |
| `meeting.chat.history` | C→S (ack) | `{ meetingId, take?, before? }` → `{ items }` (DESC) |
| `meeting.chat.attachment.meta` | C→S (ack) | `{ meetingId, fileId }` → metadata y estado del escaneo |
| `meeting.chat.attachment.download_url` | C→S (ack) | `{ meetingId, fileId }` → `{ downloadUrl }` (solo archivos listos) |
| `meeting.chat.message.new` / `.edited` / `.deleted` / `.read` | S→C | mensaje o cambio |

### Notificaciones

| Evento | Dirección | Payload |
|---|---|---|
| `notification.received` | S→C | `NotificationDto` con `kind`, `priority`, `resourceType`/`resourceId`/`action`/`titleKey` para el deep-link del front |
| `notification.updated` | S→C | `NotificationDto` cuando una fila agrupada por `groupKey` incrementa su contador |
| `notification.read.confirmed` | S→C | `{ notificationId, asOfUtc }` |
| `notification.read_all.confirmed` | S→C | `{ cutoffUtc, asOfUtc }` — `cutoffUtc` es la hora de corte que decidió el servidor |
| `notification.unread_count.changed` | S→C | `{ count, asOfUtc }`; el cliente ignora estados anteriores al último aplicado |

> El deep-link lo construye el front (CRM y Portal) a partir de `resourceType` + `resourceId` + `action`. El backend ya no mantiene un catálogo `NotificationActionMapping` con `urlTemplate`.

### Soporte

| Evento | Dirección | Payload |
|---|---|---|
| `support.ticket.new` | S→C | `{ ticketId, subject, priority }` → emitido a todos los agentes |
| `support.ticket.claimed` | S→C | `{ ticketId, agentId }` → emitido a customer |
| `support.ticket.escalated` | S→C | `{ ticketId, newPriority }` |
| `support.ticket.resolved` | S→C | `{ ticketId, resolvedByAgent }` |
| `support.ticket.closed` | S→C | `{ ticketId }` |
| `support.ticket.reopened` | S→C | `{ ticketId, reason }` |

## Integration Events publicados al exchange `taxvision-events`

### Chat (8 eventos)

| Tipo de evento | Cuándo | Consumidores típicos |
|---|---|---|
| `communication.chat.conversation_started.v1` | Al crear conversación | Notification, Analytics |
| `communication.chat.conversation_participant_added.v1` | Al añadir miembro a grupo | Notification |
| `communication.chat.conversation_participant_removed.v1` | Al remover miembro | Analytics |
| `communication.chat.conversation_archived.v1` | Al archivar conversación | Analytics |
| `communication.chat.message_sent.v1` | Al enviar mensaje | Notification (offline email), Analytics |
| `communication.chat.message_edited.v1` | Al editar mensaje | Compliance/Audit |
| `communication.chat.message_deleted.v1` | Al eliminar mensaje | Compliance/Audit |
| `communication.chat.attachment_uploaded.v1` | Al adjuntar archivo | Analytics (usage by plan) |

### Llamadas (7 eventos)

| Tipo de evento | Cuándo | Consumidores típicos |
|---|---|---|
| `communication.call.started.v1` | Al iniciar llamada (ring) | Analytics |
| `communication.call.accepted.v1` | Al aceptar llamada | Analytics (ring-to-answer) |
| `communication.call.ended.v1` | Al colgar | Notification (missed call email), Analytics |
| `communication.call.missed.v1` | Sin respuesta / timeout | Notification (push/email) |
| `communication.call.screen_share_started.v1` | Al iniciar pantalla compartida | Analytics (feature usage) |
| `communication.call.screen_share_stopped.v1` | Al detener pantalla compartida | Analytics (duration metric) |
| `communication.call.recording_ready.v1` | Cuando grabación está lista en CloudStorage | Notification (link al grabador) |

### Meetings (17 eventos)

| Tipo de evento | Cuándo | Consumidores típicos |
|---|---|---|
| `communication.meeting.scheduled.v1` | Al crear meeting | Notification (invitaciones), Planner |
| `communication.meeting.cancelled.v1` | Al cancelar meeting | Notification (email cancelación), **Planner** |
| `communication.meeting.rescheduled.v1` | Al reprogramar meeting | Notification, **Planner** |
| `communication.meeting.started.v1` | Al iniciar meeting | Analytics, Notification |
| `communication.meeting.ended.v1` | Al terminar meeting | Analytics, Planner |
| `communication.meeting.locked.v1` | Al bloquear sala | Compliance |
| `communication.meeting.unlocked.v1` | Al desbloquear sala | Compliance |
| `communication.meeting.host_transferred.v1` | Al transferir host | Analytics |
| `communication.meeting.participant_joined.v1` | Al unirse participante | Analytics (participant-minutes billing) |
| `communication.meeting.participant_left.v1` | Al salir participante | Analytics (durationSeconds calculado) |
| `communication.meeting.participant_admitted.v1` | Al admitir desde waiting room | Analytics |
| `communication.meeting.participant_denied.v1` | Al denegar desde waiting room | Analytics |
| `communication.meeting.participant_removed_by_host.v1` | Al expulsar participante | Compliance/Audit |
| `communication.meeting.recording_started.v1` | Al iniciar grabación | Compliance (saber si se grabó) |
| `communication.meeting.recording_stopped.v1` | Al detener grabación | Compliance |
| `communication.meeting.recording_ready.v1` | Grabación lista en CloudStorage | Notification (link), Analytics |
| `communication.meeting.invitation_requested.v1` | Al generar link de invitación | Notification (email con link) |

### Soporte (9 eventos)

| Tipo de evento | Cuándo | Consumidores típicos |
|---|---|---|
| `communication.support.opened.v1` | Al abrir ticket | Notification (alerta a agentes) |
| `communication.support.claimed.v1` | Al reclamar ticket | Notification (confirmación a customer) |
| `communication.support.reassigned.v1` | Al reasignar a otro agente | Notification |
| `communication.support.escalated.v1` | Al escalar prioridad | Notification (urgente a equipo) |
| `communication.support.first_response_set.v1` | Primera respuesta del agente | Analytics (SLA compliance) |
| `communication.support.message_added.v1` | Al enviar mensaje en ticket | Analytics (response time tracking) |
| `communication.support.resolved.v1` | Al resolver ticket | Notification (email a customer) |
| `communication.support.closed.v1` | Al cerrar ticket | Analytics |
| `communication.support.reopened.v1` | Al reabrir ticket cerrado/resuelto | Notification |

### Integration events consumidos (entrantes desde otros servicios)

| Fuente | Tipo de evento | Qué hace Communication |
|---|---|---|
| Signature | `signature.request.signer_invited.v1` | Push notification al firmante |
| Signature | `signature.request.completed.v1` | Push notification al preparador |
| Signature | `signature.request.expired.v1` | Push notification al preparador |
| Customer | `customer.created.v1` | Seed de settings de comunicación para el tenant |
| Planner (futuro) | `planner.appointment.meeting_requested.v1` | Crear meeting automáticamente |

## Política de media (F4)

Todo lo que un cliente necesita para capturar, medir y adaptar media sale de la BD por la capa de F2, nunca de constantes en los fronts:

- `GET /communication/media-policy` (cualquier principal autenticado): calidad (umbrales Excellent/Good/Unstable por RTT/pérdida/jitter, muestreo, histéresis, Poor sostenido → solo audio D-40), perfiles de video (cámara, cámara en mesh, pantalla T-52), audio (Opus FEC/DTX, `maxaveragebitrate`, AEC/NS/AGC efectivos D-46, mute que libera hardware), pantalla (D-42), sonidos (T-70), eco (D-51/D-52) y preferencias del usuario. `version` = versión global: el front refresca al recibir `settings.changed`.
- Plataforma: campos `quality*`, `camera*`, `meshCamera*`, `screen*`, `audio*`, `opus*`, `muteReleasesHardwareMinutes`, `clientsCanShareScreen`, `screenAudioAllowed`, `sounds*`, `echo*`, `sfuMaxIncomingBitrateKbps`, `sfuStatsSampleSeconds` en `PUT /communication/admin/platform-settings` (rango técnico + orden de umbrales). Oficina (null = hereda): `clientsCanShareScreen`, `screenAudioAllowed`, `audio*Default`, `echoMuteOnDetect`. Usuario: `echoCancellation`/`noiseSuppression`/`autoGainControl` (null = oficina), `joinMuted`, `joinCameraOff`, `preferAudioOnly`.
- Servicios por `Scope` (ADD-11 T-76) en `application/services/`: `connection-quality.service.ts` (persiste el nivel, `quality.changed` con cifras en el `MeetingEventLog`, difunde `meeting.participant.quality_changed` / `call.peer_quality_changed`), `media-status.service.ts` (meetings publican `screen_share_started/stopped.v1`), `recording-application.service.ts` (entrada única de grabación por scope).
- SFU: `producer/consumer.on('score')` y `layerschange` → observador (`sfu.score` en el log al cruzar el umbral 5/7, agregado para `communication.meeting.quality_summary.v1` al terminar), `consumer.setPriority` (audio 255 > pantalla 200 > tile grande 150 > thumbnails 100), `setMaxIncomingBitrate` y muestreo de `transport.getStats()` por política; el producer de audio acepta `source: 'screen'` (D-41).
- Calls (INV-12 H-3): `Calls.Disabled`, `Calls.VideoDisabled`, `Call.LimitReached` ('concurrent' | 'monthly') en `call.initiate`; `maxCallMinutes` se fija al aceptar y `call-lifecycle-scheduler` avisa (`call.duration.warning`) y corta (`MaxDurationReached`); `TenantCallUsage` acumula segundos por mes; `CallParticipant.CustomerId` (D-58).

## Manos, reacciones y hablantes (F5)

- **Manos (D-49):** `MeetingParticipant.HandRaisedAtUtc` ordena la cola del host; `meeting.raise_hand { meetingId, raised }` responde con ack `{ handRaised, handRaisedAtUtc }` (la UI hace rollback si falla) y difunde `meeting.participant.changed`. Host/cohost (permiso `communication.meeting.host` + rol en el agregado): `meeting.host.lower_hand { meetingId, targetUserId }` → ack `{ participant }`; `meeting.host.lower_all_hands { meetingId }` → ack `{ lowered }`. El afectado recibe `meeting.hand.lowered_by_host { meetingId, byUserId, all }` en su room de usuario. El re-join completo (`meeting.join` con `rejoined: true`) baja la mano y el ack lo dice (`handLoweredOnRejoin`); la reconexión transparente (`meeting.rejoin`) la conserva; salir/expulsar la limpia. `MeetingEventLog`: `hand.raised`, `hand.lowered` (`{ reason: 'rejoin' }` cuando aplica), `hand.lowered_by_host` (actor + target), `hand.lowered_all` (`{ count }`).
- **Reacciones (D-50):** política en BD — plataforma `reactionsEnabled`, `reactionDurationSeconds` (1..15), `reactionsPerUserPer10s` (1..60), `reactionsPerMeetingPerMinute` (10..2000), `meetingReactionCatalog` (JSON array de emojis, 1..30 distintos) en `PUT /communication/admin/platform-settings`; oficina (null = hereda) `reactionsEnabled` (solo puede apagar) y `meetingReactionCatalog` (sustituye) en `PUT /communication/settings`. `GET /communication/media-policy` expone `reactions: { enabled, catalog, durationSeconds, perUserPer10s }` y `speaking: { maxPerSecond }`. Socket `meeting.reaction { meetingId, emoji }` → ack `{ ok: true, value: null }` o `Meeting.ReactionsDisabled | Meeting.ReactionNotInCatalog | Meeting.ReactionRateLimited | Meeting.NotParticipant`; cupos por usuario (10 s) y por sala (60 s) en Redis (`comm:react:*`, ventana fija que no consume al rechazar); difusión **volátil** `meeting.reaction.received { meetingId, userId, displayName, emoji, at }`. Nada se persiste por reacción: `MeetingReactionAggregator` suma por minuto y escribe `reaction.summary { minuteUtc, counts }` al cerrar cada minuto y al terminar la reunión (`finalizeEndedMeeting`).
- **Hablantes (T-69):** `meeting.speaking { meetingId, speaking }` (sin ack; solo al cambiar el estado propio, tope `speaking.maxPerSecond` = `DominantSpeakerMaxPerSecond`, `SpeakingThrottle` en Redis) → `speaker.started` / `speaker.stopped` en el `MeetingEventLog` para la línea de tiempo. No se difunde: la UI detecta al hablante en local. `meeting.dominant_speaker` y `meeting.dominant_speaker.changed` quedaron retirados.
- Migración `20261030030000_add_reactions_policy_and_hand_raised_at` (sube `Version` de la fila global para invalidar las cachés por oficina).

## Invitaciones, guests y ciclo de notificaciones (F6)

- **Token (D-48):** un invitado **con cuenta** (Employee/Customer) reutiliza su token hasta `ExpiresAtUtc` (fin previsto + gracia, mínimo global), revocación o cierre de la reunión; cada uso deja `FirstUsedAtUtc/LastUsedAtUtc/UseCount` y `invitation.used` + `communication.meeting.invitation_used.v1`. Un **guest** (External) es de un solo uso (`UsedAtUtc`), salvo el re-join mientras su participante sigue dentro (gracia), en la sala de espera o tras salir con Leave (F11); quien fue expulsado por el host no vuelve con el mismo enlace. Al terminar/cancelar/expirar la reunión las invitaciones abiertas pasan a `ClosedAtUtc` (`invitation.expired` + `invitation_expired.v1`). Invitar a una reunión terminada → 400 `Meeting.NotJoinable`.
- **Enlaces:** External → página pública de guest del **CRM** `https://{host}/meet/{token}` (D-47); Employee → `https://{host}/meetings`; Customer → portal `accept/:id?token=`.
- **Guest (T-63):** `POST /communication/meetings/guest/context` con `{ token }` en el cuerpo (público, rate limit por token, 404 uniforme; F11: el token ya no va en la URL para que no quede en los logs) → título, host, hora, estado (`Scheduled|Live|Ended`), código, nombre sugerido, `guestsAllowed`, `waitingRoom` y la `mediaPolicy` de la oficina (el guest no tiene JWT). `POST join-by-token` responde 403 `Meeting.GuestsNotAllowed` cuando la oficina no admite externos. Política de oficina `guestsWaitingRoom` (null = `guestsWaitingRoomDefault` global): el guest pasa por la sala de espera aunque la reunión no la exija. Revocar con el guest dentro → salida forzada `Revoked` + cierre de sus sockets (`SocketSessionTerminator`) + `invitation_revoked.v1 { ejected }`.
- **Eventos para Notification (T-64):** `invitation_created.v1` ampliado (`meetingTitle`, `meetingStatus`, `shortCode`, `scheduledForUtc`, `durationMinutes`, `hostUserId`, `hostDisplayName`, `timeZone`, `invitedByUserId`, `invitedByDisplayName`); `cancelled.v1` / `rescheduled.v1` ampliados (mismos datos + `inviteeUserIds`; `invitedEmails` = solo externos sin cuenta); `communication.meeting.reminder.v1` T−N minutos antes (`meetingReminderMinutesBefore` global, override de oficina, 0 = apagado; barrido en `meeting-lifecycle-scheduler`, una sola vez por reunión, `Meeting.ReminderSentAtUtc`) + socket `meeting.reminder`. `timeZone` sale de Auth (`internal/tenants/{id}/primary-host` → `timeZoneId`).
- **Socket:** `meeting.invited` ahora trae `{ meetingId, title, status, invitedByUserId, invitedByDisplayName }` (toast "X invited you to a meeting in progress").
- Migración `20261030040000_add_invitation_reuse_reminder_and_guest_policy`.

## Grabación server-side, límites, retención y transcript (F7)

- **Modo de captura (C-03):** flag de plataforma `serverSideRecordingEnabled` (default true). Server = captura por pista en `communication-recorder`; Client = mezcla en el navegador (fallback temporal). `RecordingSession.CaptureMode` queda fijo al empezar.
- **Estados:** `Requesting → Recording → Stopping → Composing → Processing → Ready | Failed`. Client: `Stopping → Processing` al adjuntar. `Ready` = archivo `Available` en CloudStorage (T-34); el transcript vive aparte en `TranscriptArtifact` y su fallo no falla la grabación. `StopReason`: `User | LimitReached | MeetingEnded | CallEnded`.
- **Captura (D-28):** `RecordingCaptureService` abre la sesión en el recorder (`POST /v1/sessions`) y engancha un tap por producer (`PlainTransport` + consumer pausado → `POST /v1/sessions/:id/tracks` → connect + keyframe, repetido cada `COMMUNICATION_RECORDER_KEYFRAME_INTERVAL_SECONDS`). Productores que nacen o mueren durante la grabación se enganchan/sueltan por el observador del SFU. Al parar envía el timeline (participantes con su `avatarFileId` + `MeetingEventLog` hasta el corte) y el recorder compone offline; con la cámara apagada pinta la foto de perfil en círculo (la baja de `GET storage/avatars/{fileId}` con su propia `RECORDER_CLOUDSTORAGE_BASE_URL`) o las iniciales si no hay foto. M2M: token de servicio de Communication (`communication-worker`), el recorder valida JWKS + `client_id`.
- **Reuniones grabadas exigen SFU (D-30):** pedir grabación en mesh con captura server → `Meeting.RecordingRequiresSfu`. **Llamadas grabadas (D-56):** el mesh no cambia; cada parte publica send-only al router propio de la llamada (id = callId) por `call.sfu.create_transport` / `call.sfu.connect_transport` / `call.sfu.produce` tras `call.recording.capture_requested { callId, routerRtpCapabilities, captureVideo }`; `call.recording.capture_stopped` lo cierra. Solo participantes y solo con la sesión `Recording` + `Server` (`Call.Recording.NotCapturing`).
- **Límite (T-35):** `MaxDurationSeconds` sale de los efectivos al empezar (reunión / llamada de audio / de video). `recording-scheduler` (15 s, lock Redis) avisa una vez `recordingLimitWarningMinutes` antes (`meeting|call.recording.limit_warning { remainingSeconds, stopsAtUtc }`) y corta al llegar (`StopReason=LimitReached`, `recording_limit_reached.v1`). El recorder corta solo a `max + recordingLimitToleranceSeconds`.
- **Eventos del recorder:** `communication.recording.composed.v1` → `Processing` + Meeting/Call apuntan al compuesto + `recording_processing_started.v1` (con `audioTracks` por participante, `language`, `prompt`, `maxDurationSeconds`, `timeoutFactorPct`). `communication.recording.composition_failed.v1` → `Failed`.
- **Disponibilidad:** `cloudstorage.file.available/infected/blocked_by_policy/scan_failed.v1` de la carpeta `Recordings` → `Ready` o `Failed`. Respaldo en el barrido: metadata de CloudStorage pasado 60 s; sin registro tras `recordingAvailabilityTimeoutMinutes` → `StorageRejected`; composiciones sin respuesta → `CompositionTimeout`.
- **Retención (T-37):** cada 60 min. Pistas de audio (`RecordingTracks`) a los `recordingTracksRetentionDays` para todas las oficinas; grabación + transcript a los `RecordingRetentionDays` solo con `PurgeEnabled`. Borrado por el contrato existente `FilesDeletionRequestedIntegrationEvent` a la cola `cloudstorage-file-deletions` (borrado lógico en CloudStorage).
- **HTTP (contrato 9.6):** `GET /communication/meetings/:id/recording` y `GET /communication/calls/:id/recording` → `{ state, captureMode, startedAtUtc, stoppedAtUtc, durationSeconds, maxDurationSeconds, stopReason, format, sizeBytes, failureReason, playback, transcript }` (URLs presignadas solo con `Ready`). Ven quienes participaron; el staff que no participó necesita `communication.meeting.record` / `communication.call.record` (y la asignación del cliente en llamadas si aplica); el portal solo si `recordingVisibleInPortal`. 404 `*.Recording.NotFound`, 403 `*.Recording.Forbidden`. La lista de reuniones trae `recordingState` por ítem. `POST /communication/admin/recordings/:scope/:id/recompose` (PlatformOnly, 202) recompone desde el spool del recorder.
- **Notas de voz (D-27):** al enviar, `Message.TranscriptState = Processing | Skipped` según plan, oficina (`voiceNoteTranscriptEnabled`) y `voiceNoteTranscriptMaxSeconds`; `communication.chat.voice_note_transcription_requested.v1` → el worker responde `voice_note_transcript_ready|failed.v1` con el texto en línea → socket `chat.message.transcript_updated`. La búsqueda de mensajes incluye `TranscriptText`.
- **Configuración en BD:** plataforma (`recordingLimitWarningMinutes`, `recordingLimitToleranceSeconds`, `recordingComposition{Width,Height,Fps,VideoKbps,AudioKbps,Format}`, `recordingCaptureSpatialLayer`, `recordingTracksRetentionDays`, `voiceNoteTranscriptMaxSeconds`, `transcriptWorkerTimeoutFactorPct`, `recordingAvailabilityTimeoutMinutes`, `serverSideRecordingEnabled`, `voiceNoteTranscriptEnabled`); oficina (`transcriptPrompt` ≤ 500, `voiceNoteTranscriptEnabled`).
- **Variables:** `COMMUNICATION_RECORDER_BASE_URL` (vacío = sin recorder: la captura server falla con `RecorderUnavailable`), `COMMUNICATION_RECORDER_TAP_LISTEN_IP`, `COMMUNICATION_RECORDER_TIMEOUT_MS`, `COMMUNICATION_RECORDER_KEYFRAME_INTERVAL_SECONDS`. En local el recorder escucha en 5365 (5360 lo usa Subscription en la flota).
- Migración `20261031010000_add_server_recording_transcript_artifacts`.

## Jobs one-shot (`src/jobs/`)

Corren con `node dist/jobs/<job>.js` (producción, paso de backfills del despliegue) o `npm run job:<nombre>` (local, tsx). Son idempotentes y aceptan `--dry-run` (solo informan).

| Job | Qué hace |
|---|---|
| `job:expire-stale-meetings` | D-60: pasa por el dominio las reuniones eternas — `Scheduled` vencidas (plazo de la oficina, `scheduledMeetingExpiryHours`) → `Expired` con `meeting.expired.v1`; `Live` sin nadie dentro → `Ended` (`reason: Backfill`) con el cierre ordenado. Mismo código que el scheduler de ciclo de vida. |
| `job:backfill-participant-customer-id` | D-58: rellena `MeetingParticipant.CustomerId` de las filas anteriores desde `CustomerPortalAccount`. F4: también `CallParticipant.CustomerId`. |

## Reglas de oro (no repetir el legacy)

1. Redis adapter Socket.IO obligatorio — nunca in-memory `Map`.
2. Rol/tenant SIEMPRE del JWT verificado, nunca del body/query.
3. `console.log` prohibido — usar Pino (`import { logger }`).
4. Idempotencia por `Idempotency-Key` en cada comando socket que muta.
5. Passcodes con Argon2, tokens efímeros de recording con `exp` + jti revocable.
6. Nada de `sleep(2000)` en disconnect — presencia con lease TTL + Pub/Sub Redis.
7. Cualquier variable sensible se lee de `config` (Zod-validado) — no `process.env` directo.
8. Outbox transaccional (Prisma) para publicar integration events — nunca disparar directo al bus.
9. Inbox idempotente (ProcessedEventStore) — rechazar eventos ya procesados por `eventId`.
10. Support cross-tenant es PlatformTenant ↔ office tenant (preparadores), NUNCA preparador ↔ contribuyente.
11. Señalización WebRTC (SDP/ICE) va solo por socket — NUNCA al bus de eventos.
12. `session.revoked` se emite en su propio canal, separado de `notification.received`.

## Variables de entorno (`.env.example`)

```ini
# Base de datos
COMMUNICATION_DB_CONNECTION="sqlserver://localhost:1433;database=TaxVision_Communication;..."

# Auth / JWKS
JWKS_URI=http://auth-service/auth/.well-known/jwks.json
PLATFORM_TENANT_ID=<uuid del tenant TaxVision>

# RabbitMQ
RABBIT_URL=amqp://guest:guest@localhost:5672
RABBIT_EXCHANGE=taxvision-events
RABBIT_QUEUE=communication-service

# Redis
REDIS_URL=redis://localhost:6379
REDIS_ADAPTER_PREFIX=comm:socket

# WebRTC / TURN
TURN_URL=turn:turn.example.com:3478
TURN_USERNAME=taxvision
TURN_CREDENTIAL=<secret>
TURN_TTL_SECONDS=86400

# SFU (mediasoup) — un puerto fijo por worker (UDP y TCP) via WebRtcServer; worker i = PORT_BASE + i.
# Publicar esos puertos en Docker y reenviarlos en el router. TURN no releva hacia el SFU.
COMMUNICATION_MEDIASOUP_ANNOUNCED_IP=<ip publica>
COMMUNICATION_MEDIASOUP_NUM_WORKERS=4
COMMUNICATION_MEDIASOUP_WEBRTC_SERVER_PORT_BASE=40000
COMMUNICATION_MEDIASOUP_ENABLE_TCP=true

# Argon2 / Passcodes
PASSCODE_PEPPER=<32-byte hex>

# CORS
CORS_ORIGINS=http://localhost:3000,https://app.taxvision.io

# OpenTelemetry
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4318
OTEL_SERVICE_NAME=communication-service
```

## Colección Postman

Ver `Implementaciones/TaxVision_Communication.postman_collection.json` — incluye variables `{{base_url}}`, `{{token}}`, `{{tenant_id}}` y ejemplos para todos los endpoints REST y eventos socket documentados arriba.
