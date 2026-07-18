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
| `GET` | `/communication/conversations` | Listar conversaciones del usuario |
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
| `GET` | `/communication/notifications` | Listar notificaciones del usuario (paginado) |
| `GET` | `/communication/notifications/unread-count` | Contador de no leídas |
| `POST` | `/communication/notifications/:id/read` | Marcar como leída |
| `POST` | `/communication/notifications/read-all` | Marcar todas como leídas |

### Soporte cross-tenant

| Método | Ruta | Descripción |
|---|---|---|
| `POST` | `/communication/support` | Abrir ticket (desde tenant cliente) |
| `GET` | `/communication/support` | Listar tickets (cliente ve los suyos; agente ve todos) |
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
| `GET` | `/communication/analytics/snapshot` | Snapshot diario del tenant (mensajes, llamadas, meetings, support) |
| `GET` | `/communication/analytics/snapshot/range` | Rango de fechas `?from=&to=` |

## Socket.IO — Events

Namespace: `/communication/socket.io`. Autenticación vía `handshake.auth.token`.

### Sesión (canal global)

| Evento | Dirección | Payload |
|---|---|---|
| `session.revoked` | S→C | `{ reason: string }` — logout inmediato |

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
| `chat.presence.changed` | S→C | `{ userId, status: 'Online'\|'Away'\|'Offline' }` |

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

### Notificaciones

| Evento | Dirección | Payload |
|---|---|---|
| `notification.received` | S→C | `NotificationDto` con `kind`, `priority`, `payload` (deep-link) |

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
