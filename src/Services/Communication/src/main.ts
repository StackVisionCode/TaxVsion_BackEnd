import { startTelemetry, shutdownTelemetry } from './infrastructure/telemetry/telemetry.js';
// Telemetria PRIMERO — auto-instrumentaciones deben cargarse antes de importar Fastify/Prisma.
startTelemetry();

import { config } from './infrastructure/config.js';
import { logger } from './infrastructure/logger/logger.js';
import { prisma, connectPrisma, disconnectPrisma } from './infrastructure/persistence/prisma-client.js';
import { connectRedis, disconnectRedis } from './infrastructure/redis/redis-client.js';
import { connectRabbit, disconnectRabbit } from './infrastructure/rabbit/rabbit-connection.js';
import { buildHttpServer } from './infrastructure/http/build-server.js';
import { buildSocketServer } from './infrastructure/socket/build-io.js';
import { buildContainer } from './infrastructure/container.js';
import { registerChatHandlers } from './api/socket/handlers/chat-handlers.js';
import { registerCallHandlers } from './api/socket/handlers/call-handlers.js';
import { registerMeetingHandlers } from './api/socket/handlers/meeting-handlers.js';
import { registerNotificationHandlers } from './api/socket/handlers/notification-handlers.js';
import { startMissedCallScheduler } from './infrastructure/schedulers/missed-call-scheduler.js';
import { startPurgeScheduler } from './infrastructure/schedulers/purge-scheduler.js';
import { startOutboxDrainer } from './infrastructure/rabbit/outbox-drainer.js';
import { ConsumerRuntime } from './infrastructure/rabbit/consumer-runtime.js';
import { bindSignatureConsumers } from './application/event-handlers/signature-consumers.js';
import { bindCustomerConsumers } from './application/event-handlers/customer-consumers.js';
import { bindAuthConsumers } from './application/event-handlers/auth-consumers.js';
import { bindCloudStorageConsumers } from './application/event-handlers/cloudstorage-consumers.js';
import { bindTranscriptConsumers } from './application/event-handlers/transcript-consumers.js';
import { bindSubscriptionConsumers } from './application/event-handlers/subscription-consumers.js';
import { bindAnalyticsConsumers } from './application/event-handlers/analytics-consumers.js';
import { SocketRealtimeEmitter } from './infrastructure/socket/socket-realtime-emitter.js';
import { startSessionDenylistWatcher } from './infrastructure/redis/session-denylist-watcher.js';
import { startPresenceChangedWatcher } from './infrastructure/redis/presence-changed-watcher.js';
import { redisSub } from './infrastructure/redis/redis-client.js';

async function main(): Promise<void> {
  await connectRedis();
  await connectRabbit();
  await connectPrisma();

  const container = buildContainer();
  await container.sfu.start();
  const app = await buildHttpServer(container);
  const io = buildSocketServer(app.server);
  registerChatHandlers(io, container);
  registerCallHandlers(io, container);
  registerMeetingHandlers(io, container);
  registerNotificationHandlers(io, container);
  const missedCalls = startMissedCallScheduler(
    { intervalSeconds: 30, ringingTimeoutSeconds: 60 },
    { calls: container.calls, publisher: container.publisher, lock: container.distributedLock },
  );
  const outbox = startOutboxDrainer({ intervalMs: 2000 }, { lock: container.distributedLock });
  const purge = startPurgeScheduler(
    { intervalHours: 24 },
    { prisma, settings: container.tenantSettings, lock: container.distributedLock },
  );

  // Consumer runtime + registro de handlers Signature/Customer/Auth.
  const emitter = new SocketRealtimeEmitter(io);
  const consumers = new ConsumerRuntime(container.processedEvents);
  bindSignatureConsumers(consumers.register.bind(consumers), { notifications: container.notifications, emitter });
  bindCustomerConsumers(consumers.register.bind(consumers), { notifications: container.notifications, emitter });
  bindAuthConsumers(consumers.register.bind(consumers), {
    userPermissions: container.userPermissions,
    userDirectory: container.userDirectory,
  });
  bindSubscriptionConsumers(consumers.register.bind(consumers), { limits: container.limits });
  bindCloudStorageConsumers(consumers.register.bind(consumers), {
    attachmentTracking: container.attachmentTracking,
    emitter,
  });
  bindAnalyticsConsumers(consumers.register.bind(consumers), { analytics: container.analytics });
  bindTranscriptConsumers(consumers.register.bind(consumers), {
    calls: container.calls,
    meetings: container.meetings,
    emitter,
  });
  await consumers.start();

  // Session-revoked (canal Redis Pub/Sub — separado del canal de notifications
  // de negocio). Cierra CRIT legacy que mezclaba force_logout con notifs.
  const sessionWatcher = startSessionDenylistWatcher(redisSub, emitter);

  // Presence transitions online/offline: RedisPresenceService publica en
  // comm:presence:changed:{tenantId}; el watcher se suscribe con PSUBSCRIBE
  // y emite `chat.presence.changed` al room del tenant.
  const presenceWatcher = startPresenceChangedWatcher(redisSub, emitter);

  await app.listen({ host: config.http.host, port: config.http.port });
  logger.info({ host: config.http.host, port: config.http.port }, 'Communication service listening');

  const signals: NodeJS.Signals[] = ['SIGINT', 'SIGTERM'];
  for (const signal of signals) {
    process.once(signal, () => {
      void shutdown(signal, app, io, container, missedCalls, outbox, purge, sessionWatcher, presenceWatcher);
    });
  }
}

async function shutdown(
  signal: NodeJS.Signals,
  app: Awaited<ReturnType<typeof buildHttpServer>>,
  io: ReturnType<typeof buildSocketServer>,
  container: ReturnType<typeof buildContainer>,
  missedCalls: ReturnType<typeof startMissedCallScheduler>,
  outbox: ReturnType<typeof startOutboxDrainer>,
  purge: ReturnType<typeof startPurgeScheduler>,
  sessionWatcher: ReturnType<typeof startSessionDenylistWatcher>,
  presenceWatcher: ReturnType<typeof startPresenceChangedWatcher>,
): Promise<void> {
  logger.info({ signal }, 'Shutting down');
  try {
    missedCalls.stop();
    outbox.stop();
    purge.stop();
    await container.sfu.stop();
    await sessionWatcher.stop();
    await presenceWatcher.stop();
    io.disconnectSockets(true);
    await new Promise<void>((resolve) => io.close(() => resolve()));
    await app.close();
    await disconnectRabbit();
    await disconnectPrisma();
    await disconnectRedis();
    await shutdownTelemetry();
    logger.info('Shutdown complete');
    process.exit(0);
  } catch (err) {
    logger.error({ err }, 'Shutdown error');
    process.exit(1);
  }
}

main().catch((err) => {
  logger.fatal({ err }, 'Fatal boot error');
  process.exit(1);
});
