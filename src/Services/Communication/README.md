# TaxVision Communication

Microservicio de tiempo real de TaxVision — chat, calls 1:1, meetings, notificaciones in-app y support cross-tenant. **Único servicio en Node.js/TypeScript** del stack; el resto son .NET 10.

## Stack

| Capa | Tecnología |
|---|---|
| Runtime | Node.js ≥ 20.11 + TypeScript strict |
| HTTP | Fastify 5 (+ helmet, cors, rate-limit, sensible) |
| Realtime | Socket.IO 4 + `@socket.io/redis-adapter` |
| Persistencia | Prisma 5 sobre SQL Server (`TaxVision_Communication`) |
| Bus eventos | RabbitMQ (`amqplib`) al exchange `taxvision-events` |
| Cache/lock/presence | Redis (`ioredis`) |
| Auth | Verificación JWT RS256 vía JWKS público de Auth (`jose`) |
| Validación | Zod en boundaries + branded types en dominio |
| Logs | Pino JSON estructurado |
| Observabilidad | OpenTelemetry SDK Node → OTLP |
| Testing | Vitest |

## Layout DDD

```
src/
├── domain/          # aggregates, VOs, ports (interfaces)
├── application/     # use cases, event handlers, ports impl
├── infrastructure/  # Prisma, Socket.IO, Fastify, Rabbit, Redis, JWKS, OTel
├── api/
│   ├── http/        # rutas + plugins Fastify
│   └── socket/      # namespaces + handlers Socket.IO
└── contracts/       # integration events + tipos socket exportables
```

## Cómo correr en dev

```powershell
cd src/Services/Communication
npm install
npx prisma generate
copy .env.example .env
# Editar .env con COMMUNICATION_DB_CONNECTION, JWKS_URI apuntando a Auth, RabbitMQ URI, etc.
npx prisma migrate dev --name init
npm run dev
```

Salida: HTTP en `http://localhost:5350`, Socket.IO en `ws://localhost:5350/communication/socket.io`.

## Health checks

- `GET /health/live` — proceso vivo.
- `GET /health/ready` — SQL + Redis + Rabbit disponibles.

## Autorización

- HTTP: `Authorization: Bearer <accessToken>` verificado contra JWKS de Auth (rota sin redeploy).
- Socket.IO: `handshake.auth.token` (nunca `handshake.query` — cierra un CRIT del legacy).
- Denylist Redis (`auth:session-denylist`) compartido con Auth: la revocación es inmediata.

## Reglas de oro (no repetir el legacy)

1. Redis adapter Socket.IO obligatorio — nunca in-memory Map.
2. Rol/tenant SIEMPRE del JWT verificado, nunca del body/query.
3. `console.log` prohibido; usar Pino (`import { logger }`).
4. Idempotencia por `Idempotency-Key` en cada comando socket que muta.
5. Passcodes con Argon2, tokens efímeros de recording con `exp` + jti revocable.
6. Nada de `sleep(2000)` en disconnect: presence con lease TTL + Pub/Sub Redis.
7. Cualquier variable sensible se lee de `config` (Zod-validado) — no `process.env` directo.
8. Outbox transaccional para publicar integration events.

## Próximas fases

Ver README principal §30 (llegará en Fase 8) y las tareas en el tracker: `Communication Fase 1..8`.
