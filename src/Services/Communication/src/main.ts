import { startTelemetry, shutdownTelemetry } from './infrastructure/telemetry/telemetry.js';
// Telemetria PRIMERO — auto-instrumentaciones deben cargarse antes de importar Fastify/Prisma.
startTelemetry();

import { config } from './infrastructure/config.js';
import { logger } from './infrastructure/logger/logger.js';
import { prisma, connectPrisma, disconnectPrisma } from './infrastructure/persistence/prisma-client.js';
import { connectRedis, disconnectRedis } from './infrastructure/redis/redis-client.js';
import {
  connectRabbit,
  disconnectRabbit,
  onRabbitReconnected,
} from './infrastructure/rabbit/rabbit-connection.js';
import { buildHttpServer } from './infrastructure/http/build-server.js';
import { buildSocketServer, markShuttingDown } from './infrastructure/socket/build-io.js';
import { buildContainer } from './infrastructure/container.js';
import { registerChatHandlers } from './api/socket/handlers/chat-handlers.js';
import { registerCallHandlers } from './api/socket/handlers/call-handlers.js';
import { registerMeetingHandlers } from './api/socket/handlers/meeting-handlers.js';
import { registerNotificationHandlers } from './api/socket/handlers/notification-handlers.js';
import { startMissedCallScheduler } from './infrastructure/schedulers/missed-call-scheduler.js';
import { startPurgeScheduler } from './infrastructure/schedulers/purge-scheduler.js';
import { startRecordingConsentTimeoutScheduler } from './infrastructure/schedulers/recording-consent-timeout-scheduler.js';
import { startCustomerReconciliationScheduler } from './infrastructure/schedulers/customer-reconciliation-scheduler.js';
import { startCustomerAssignmentReconciliationScheduler } from './infrastructure/schedulers/customer-assignment-reconciliation-scheduler.js';
import { startOutboxDrainer } from './infrastructure/rabbit/outbox-drainer.js';
import { ConsumerRuntime } from './infrastructure/rabbit/consumer-runtime.js';
import { bindSignatureConsumers } from './application/event-handlers/signature-consumers.js';
import { bindCustomerConsumers } from './application/event-handlers/customer-consumers.js';
import { bindAuthConsumers } from './application/event-handlers/auth-consumers.js';
import { bindOffboardingConsumers } from './application/event-handlers/offboarding-consumers.js';
import { bindCloudStorageConsumers } from './application/event-handlers/cloudstorage-consumers.js';
import { bindCloudStorageNotificationConsumers } from './application/event-handlers/cloudstorage-notification-consumers.js';
import { bindConnectorsConsumers } from './application/event-handlers/connectors-consumers.js';
import { bindCorrespondenceConsumers } from './application/event-handlers/correspondence-consumers.js';
import { bindTranscriptConsumers } from './application/event-handlers/transcript-consumers.js';
import { bindSubscriptionConsumers } from './application/event-handlers/subscription-consumers.js';
import { moduleFor } from './domain/shared/permission-module-map.js';
import { configureModuleGate } from './domain/shared/permissions.js';
import { bindAnalyticsConsumers } from './application/event-handlers/analytics-consumers.js';
import { bindCalendarConsumers } from './application/event-handlers/calendar-consumers.js';
import { SocketRealtimeEmitter } from './infrastructure/socket/socket-realtime-emitter.js';
import { startSessionDenylistWatcher } from './infrastructure/redis/session-denylist-watcher.js';
import { startPresenceChangedWatcher } from './infrastructure/redis/presence-changed-watcher.js';
import { redisSub } from './infrastructure/redis/redis-client.js';
import { seedDefaultNotificationActionMappings } from './infrastructure/seed/seed-notification-action-mappings.js';

/**
 * Red de seguridad del proceso: un error async no capturado en un handler de socket (p.ej. un deadlock
 * transitorio de Prisma, P2034) NO debe tumbar TODO el servicio de realtime — eso deja sin chat/llamadas
 * a todos los tenants conectados. Se registra y se sigue. Los errores esperables ya se manejan en su sitio;
 * esto es la última línea para lo imprevisto (Node por defecto termina el proceso ante estos eventos).
 */
function installProcessSafetyNet(): void {
  process.on('unhandledRejection', (reason) => {
    logger.error(
      {
        err: reason instanceof Error ? reason.message : String(reason),
        stack: reason instanceof Error ? reason.stack : undefined,
      },
      'Unhandled promise rejection — logged and swallowed to keep the service alive',
    );
  });
  process.on('uncaughtException', (err) => {
    logger.error({ err: err.message, stack: err.stack }, 'Uncaught exception — logged; service kept alive');
  });
}

async function main(): Promise<void> {
  installProcessSafetyNet();
  // Fase Backend 11 — sin `announcedIp`, mediasoup (SFU, meetings >4
  // participantes) anuncia `listenIp` (tipicamente 0.0.0.0 o una IP privada de
  // contenedor/VM) en los ICE candidates: cualquier peer detras de NAT nunca
  // conecta el WebRTC transport. No es fatal a nivel de proceso — el resto del
  // backend (chat, calls 1:1 via mesh, HTTP) sigue funcionando — por eso solo
  // logueamos `fatal` (maxima severidad de log, altamente visible en
  // Loki/OTLP) en vez de abortar el boot.
  if (config.isProduction && !config.mediasoup.announcedIp) {
    logger.fatal(
      'COMMUNICATION_MEDIASOUP_ANNOUNCED_IP is unset in production — SFU meetings (>4 participants) behind NAT will fail to establish WebRTC transports.',
    );
  }

  await connectRedis();
  await connectRabbit();
  await connectPrisma();

  const container = buildContainer();
  await seedDefaultNotificationActionMappings(container.notificationActionMappings);
  await container.sfu.start();
  const app = await buildHttpServer(container);
  const io = buildSocketServer(app.server);
  registerChatHandlers(io, container);
  registerCallHandlers(io, container);
  registerMeetingHandlers(io, container);
  registerNotificationHandlers(io, container);
  const emitter = new SocketRealtimeEmitter(io);
  // Wire tarde el emitter para que las rutas HTTP (Fase Backend 6+) que
  // emiten a rooms del socket puedan leerlo desde container.emitter en el
  // handler del request. Ver container.ts para el porque del post-init.
  container.emitter = emitter;
  const missedCalls = startMissedCallScheduler(
    { intervalSeconds: 30, ringingTimeoutSeconds: 60 },
    {
      calls: container.calls,
      conversations: container.conversations,
      publisher: container.publisher,
      presence: container.presence,
      emitter,
      lock: container.distributedLock,
    },
  );
  const outbox = startOutboxDrainer({ intervalMs: 2000 }, { lock: container.distributedLock });
  const purge = startPurgeScheduler(
    { intervalHours: 24 },
    { prisma, settings: container.tenantSettings, lock: container.distributedLock },
  );
  const recordingConsentTimeout = startRecordingConsentTimeoutScheduler(
    { intervalSeconds: 10, meetingTimeoutSeconds: 30, callTimeoutSeconds: 15 },
    {
      meetings: container.meetings,
      calls: container.calls,
      recordingSessions: container.recordingSessions,
      recordingConsents: container.recordingConsents,
      tenantSettings: container.tenantSettings,
      publisher: container.publisher,
      emitter,
      lock: container.distributedLock,
    },
  );
  const customerReconciliation = startCustomerReconciliationScheduler(
    { enabled: config.customerReconcile.enabled, intervalHours: config.customerReconcile.intervalHours },
    {
      client: container.customerReconciliation,
      customerDirectory: container.customerDirectory,
      lock: container.distributedLock,
    },
  );
  // P2.5 — siembra/auto-repara la proyeccion M:N que alimenta el gate de chat por asignacion.
  const customerAssignmentReconciliation = startCustomerAssignmentReconciliationScheduler(
    { enabled: config.customerReconcile.enabled, intervalHours: config.customerReconcile.intervalHours },
    {
      client: container.customerAssignmentsReconciliation,
      customerAssignments: container.customerAssignments,
      lock: container.distributedLock,
    },
  );

  // Consumer runtime + registro de handlers Signature/Customer/Auth.
  const consumers = new ConsumerRuntime(container.processedEvents);
  bindSignatureConsumers(consumers.register.bind(consumers), {
    notifications: container.notifications,
    emitter,
    customerPortalAccounts: container.customerPortalAccounts,
    actionMappings: container.notificationActionMappings,
  });
  bindCustomerConsumers(consumers.register.bind(consumers), {
    notifications: container.notifications,
    emitter,
    customerDirectory: container.customerDirectory,
    customerPreparerAssignments: container.customerPreparerAssignments,
    customerAssignments: container.customerAssignments,
  });
  bindAuthConsumers(consumers.register.bind(consumers), {
    userPermissions: container.userPermissions,
    userDirectory: container.userDirectory,
    rolePermissions: container.rolePermissions,
    customerPortalAccounts: container.customerPortalAccounts,
  });
  // Offboarding (retiro terminal): baja las proyecciones + reasigna/cancela reuniones del host retirado.
  bindOffboardingConsumers(consumers.register.bind(consumers), {
    userPermissions: container.userPermissions,
    userDirectory: container.userDirectory,
    customerPortalAccounts: container.customerPortalAccounts,
    meetings: container.meetings,
    publisher: container.publisher,
    emitter,
  });
  bindSubscriptionConsumers(consumers.register.bind(consumers), {
    limits: container.limits,
    planCodeCache: container.planCodeCache,
    modulesCache: container.tenantModulesCache,
  });

  // Gate de modulo (Entitlements en runtime), modo LOG-ONLY — espejo del hook de
  // PermissionPolicyProvider.cs (.NET). Tras conceder un permiso, observa si el tenant tiene el
  // modulo del permiso y lo loguea SIN bloquear. PlatformAdmin ya bypasea en checkPermission; aca se
  // bypasea el actor Service (M2M). `null` del reader = sin proyeccion aun -> no gatea.
  configureModuleGate(async (subject, required) => {
    if (subject.actorType === 'Service') return;
    const module = moduleFor(required);
    if (!module) return;
    const enabledModules = await container.tenantModulesCache.getEnabledModules(subject.tenantId);
    if (enabledModules === null) return;
    if (!enabledModules.includes(module)) {
      logger.info(
        { permission: required, module, tenantId: subject.tenantId },
        'Module gate (log-only): permiso pertenece a un modulo NO habilitado para el tenant; seria 403 al enforzar',
      );
    }
  });
  bindCloudStorageConsumers(consumers.register.bind(consumers), {
    attachmentTracking: container.attachmentTracking,
    notifications: container.notifications,
    emitter,
  });
  bindCloudStorageNotificationConsumers(consumers.register.bind(consumers), {
    notifications: container.notifications,
    emitter,
  });
  bindConnectorsConsumers(consumers.register.bind(consumers), {
    notifications: container.notifications,
    emitter,
  });
  bindCorrespondenceConsumers(consumers.register.bind(consumers), { emitter });
  bindCalendarConsumers(consumers.register.bind(consumers), {
    meetings: container.meetings,
    publisher: container.publisher,
    passcodes: container.passcodes,
    settings: container.settings,
    emitter,
  });
  bindAnalyticsConsumers(consumers.register.bind(consumers), { analytics: container.analytics });
  bindTranscriptConsumers(consumers.register.bind(consumers), {
    calls: container.calls,
    meetings: container.meetings,
    recordingSessions: container.recordingSessions,
    publisher: container.publisher,
    emitter,
  });
  await consumers.start();
  // Re-suscribir el consumer en cada reconexión de Rabbit: el canal nuevo necesita su propio
  // channel.consume. Sin esto, tras el primer corte del broker el servicio queda conectado pero
  // sin consumir (la cola durable se llena y ninguna proyección se actualiza).
  onRabbitReconnected(() => consumers.start());

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
      void shutdown(
        signal,
        app,
        io,
        container,
        missedCalls,
        outbox,
        purge,
        recordingConsentTimeout,
        customerReconciliation,
        customerAssignmentReconciliation,
        sessionWatcher,
        presenceWatcher,
        consumers,
      );
    });
  }
}

const SHUTDOWN_DRAIN_MS = 30_000;

async function shutdown(
  signal: NodeJS.Signals,
  app: Awaited<ReturnType<typeof buildHttpServer>>,
  io: ReturnType<typeof buildSocketServer>,
  container: ReturnType<typeof buildContainer>,
  missedCalls: ReturnType<typeof startMissedCallScheduler>,
  outbox: ReturnType<typeof startOutboxDrainer>,
  purge: ReturnType<typeof startPurgeScheduler>,
  recordingConsentTimeout: ReturnType<typeof startRecordingConsentTimeoutScheduler>,
  customerReconciliation: ReturnType<typeof startCustomerReconciliationScheduler>,
  customerAssignmentReconciliation: ReturnType<typeof startCustomerAssignmentReconciliationScheduler>,
  sessionWatcher: ReturnType<typeof startSessionDenylistWatcher>,
  presenceWatcher: ReturnType<typeof startPresenceChangedWatcher>,
  consumers: ConsumerRuntime,
): Promise<void> {
  logger.info({ signal }, 'Shutting down (graceful drain up to 30s)');
  try {
    missedCalls.stop();
    outbox.stop();
    purge.stop();
    recordingConsentTimeout.stop();
    customerReconciliation.stop();
    customerAssignmentReconciliation.stop();
    await container.sfu.stop();
    await sessionWatcher.stop();
    await presenceWatcher.stop();

    // Deja de aceptar mensajes NUEVOS de RabbitMQ y espera hasta
    // SHUTDOWN_DRAIN_MS a que los handlers ya en curso (dispatch()) terminen
    // de ackear/nackear antes de seguir — ver docblock de ConsumerRuntime.stop.
    await consumers.stop(SHUTDOWN_DRAIN_MS);

    // Deja de aceptar sockets NUEVOS (middleware `io.use` en build-io.ts
    // rechaza con 'Server.ShuttingDown') y espera a que los ya conectados se
    // desconecten por su cuenta (cierre normal de pestaña, fin de llamada,
    // etc.) hasta el mismo margen — no forzamos el corte inmediato como antes
    // (`disconnectSockets(true)` sin espera), que cortaba un ack/emit a mitad
    // de camino. Nota honesta: esto es un margen de espera fijo, no un
    // conteo de "handlers de socket en vuelo" (no existe tal contador hoy,
    // a diferencia de ConsumerRuntime) — pasado el margen, se fuerza igual.
    markShuttingDown();
    const drainDeadline = Date.now() + SHUTDOWN_DRAIN_MS;
    while (io.engine.clientsCount > 0 && Date.now() < drainDeadline) {
      await new Promise((resolve) => setTimeout(resolve, 200));
    }
    if (io.engine.clientsCount > 0) {
      logger.warn({ remaining: io.engine.clientsCount }, 'Socket drain timeout exceeded, forcing disconnect');
    }
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
