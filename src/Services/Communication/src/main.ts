import { startTelemetry, shutdownTelemetry } from './infrastructure/telemetry/telemetry.js';
import { startInstanceHeartbeat } from './infrastructure/telemetry/instance-heartbeat.js';
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
import { startMeetingLifecycleScheduler } from './infrastructure/schedulers/meeting-lifecycle-scheduler.js';
import { startCallLifecycleScheduler } from './infrastructure/schedulers/call-lifecycle-scheduler.js';
import { registerSessionHandlers } from './api/socket/handlers/session-handlers.js';
import { startCustomerReconciliationScheduler } from './infrastructure/schedulers/customer-reconciliation-scheduler.js';
import { startUserDirectoryReconciliationScheduler } from './infrastructure/schedulers/user-directory-reconciliation-scheduler.js';
import { startPortalAccountReconciliationScheduler } from './infrastructure/schedulers/portal-account-reconciliation-scheduler.js';
import { startCustomerAssignmentReconciliationScheduler } from './infrastructure/schedulers/customer-assignment-reconciliation-scheduler.js';
import { startOutboxDrainer } from './infrastructure/rabbit/outbox-drainer.js';
import { ConsumerRuntime } from './infrastructure/rabbit/consumer-runtime.js';
import { bindSignatureConsumers } from './application/event-handlers/signature-consumers.js';
import { bindCustomerConsumers } from './application/event-handlers/customer-consumers.js';
import { bindAuthConsumers } from './application/event-handlers/auth-consumers.js';
import { bindTenantHostConsumers } from './application/event-handlers/tenant-host-consumers.js';
import { bindOffboardingConsumers } from './application/event-handlers/offboarding-consumers.js';
import { bindCloudStorageConsumers } from './application/event-handlers/cloudstorage-consumers.js';
import { bindCloudStorageNotificationConsumers } from './application/event-handlers/cloudstorage-notification-consumers.js';
import { bindConnectorsConsumers } from './application/event-handlers/connectors-consumers.js';
import { bindCorrespondenceConsumers } from './application/event-handlers/correspondence-consumers.js';
import { bindCampaignConsumers } from './application/event-handlers/campaign-consumers.js';
import { bindWalletConsumers } from './application/event-handlers/wallet-consumers.js';
import { bindSmsConsumers } from './application/event-handlers/sms-consumers.js';
import { bindTranscriptConsumers } from './application/event-handlers/transcript-consumers.js';
import { bindSubscriptionConsumers } from './application/event-handlers/subscription-consumers.js';
import { shouldEnforceModule } from './domain/shared/module-gate-settings.js';
import { moduleFor } from './domain/shared/permission-module-map.js';
import { configureModuleGate } from './domain/shared/permissions.js';
import { bindAnalyticsConsumers } from './application/event-handlers/analytics-consumers.js';
import { bindCalendarConsumers } from './application/event-handlers/calendar-consumers.js';
import { bindSupportConsumers } from './application/event-handlers/support-consumers.js';
import { bindInAppNotificationConsumers } from './application/event-handlers/notification-inapp-consumers.js';
import { SocketRealtimeEmitter } from './infrastructure/socket/socket-realtime-emitter.js';
import { SocketIoSessionTerminator } from './infrastructure/socket/socket-session-terminator.js';
import { startSessionDenylistWatcher } from './infrastructure/redis/session-denylist-watcher.js';
import { startPresenceChangedWatcher } from './infrastructure/redis/presence-changed-watcher.js';
import { redisSub } from './infrastructure/redis/redis-client.js';
import { platformSettings } from './infrastructure/platform/platform-settings-source.js';
import { buildSfuObserver } from './infrastructure/webrtc/sfu-transport-telemetry.js';
import { createSfuTransportStateReporter } from './application/use-cases/report-sfu-transport-state.js';
import { sfuWebRtcServerFallbackWorkers } from './infrastructure/telemetry/metrics.js';
import type { SfuObserver, SfuRuntimeInfo } from './application/ports/sfu-service.js';
import { bindRecordingConsumers } from './application/event-handlers/recording-consumers.js';
import { startRecordingScheduler } from './infrastructure/schedulers/recording-scheduler.js';
import {
  lifecycleFrom,
  markRecordingFileAvailable,
  markRecordingFileRejected,
} from './application/use-cases/recording-lifecycle.js';
import type { RecordingCaptureService } from './application/services/recording-capture.service.js';

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

/**
 * Guard de arranque del SFU (D-06: registra, nunca frena). Sin `announcedAddress` detras de NAT los
 * candidatos ICE llevan una IP privada y ningun navegador externo conecta; sin WebRtcServer los puertos
 * son aleatorios y Docker no los publica. Ambos casos quedan en el log con la lista exacta de puertos
 * que el compose y el router deben exponer.
 */
function logSfuStartupGuard(info: SfuRuntimeInfo): void {
  if (config.isProduction && !info.announcedAddress) {
    logger.fatal(
      'COMMUNICATION_MEDIASOUP_ANNOUNCED_IP is unset in production — SFU meetings behind NAT will fail to establish WebRTC transports.',
    );
  }
  if (!info.webRtcServerEnabled) {
    logger.warn('WebRtcServer disabled — each SFU transport takes a random RTC port; unreachable behind Docker unless the whole range is published.');
  } else if (info.fallbackWorkers > 0) {
    logger.error({ fallbackWorkers: info.fallbackWorkers, boundPorts: info.webRtcServerPorts }, 'Some SFU workers could not bind their WebRtcServer port.');
  }
  sfuWebRtcServerFallbackWorkers.set(info.fallbackWorkers);
  logger.info(
    { ports: info.webRtcServerPorts, tcp: info.tcpEnabled, announcedAddress: info.announcedAddress },
    'SFU ports to publish in docker-compose and forward in the router (UDP, and TCP when enabled)',
  );
}

/** F7: el observador del SFU tambien avisa a la captura server-side cuando nace o muere un producer. */
function withRecordingCapture(observer: SfuObserver, capture: RecordingCaptureService): SfuObserver {
  return {
    ...observer,
    onProducerOpened(event) {
      observer.onProducerOpened?.(event);
      void capture.onProducerOpened(event).catch((err: unknown) => logger.warn({ err, producerId: event.producerId }, 'recording capture attach failed'));
    },
    onProducerClosed(event) {
      observer.onProducerClosed?.(event);
      void capture.onProducerClosed(event).catch((err: unknown) => logger.warn({ err, producerId: event.producerId }, 'recording capture detach failed'));
    },
  };
}

async function main(): Promise<void> {
  installProcessSafetyNet();
  await connectRedis();
  await connectRabbit();
  await connectPrisma();

  const container = buildContainer();
  // F1: cada transition ICE/DTLS del SFU se loguea, se cuenta y, si es un fallo, se publica como evento.
  container.sfu.setObserver(
    withRecordingCapture(
      buildSfuObserver(
        createSfuTransportStateReporter({
          publisher: container.publisher,
          meetingEvents: container.meetingEvents,
          qualityAggregator: container.qualityAggregator,
        }),
      ),
      container.capture,
    ),
  );
  await container.sfu.start();
  logSfuStartupGuard(container.sfu.describe());
  const app = await buildHttpServer(container);
  const io = buildSocketServer(app.server);
  registerChatHandlers(io, container);
  registerCallHandlers(io, container);
  registerMeetingHandlers(io, container);
  registerNotificationHandlers(io, container);
  const sessions = registerSessionHandlers(io, container);
  const emitter = new SocketRealtimeEmitter(io);
  // Wire tarde el emitter para que las rutas HTTP (Fase Backend 6+) que
  // emiten a rooms del socket puedan leerlo desde container.emitter en el
  // handler del request. Ver container.ts para el porque del post-init.
  container.emitter = emitter;
  container.sessionTerminator = new SocketIoSessionTerminator(io);
  // Timeouts, gracias y retenciones salen del resolver (PlatformCommunicationSettings), no de constantes.
  const missedCalls = startMissedCallScheduler(
    { intervalSeconds: 30 },
    {
      calls: container.calls,
      conversations: container.conversations,
      publisher: container.publisher,
      presence: container.presence,
      emitter,
      lock: container.distributedLock,
      effectiveSettings: container.effectiveSettings,
    },
  );
  const outbox = startOutboxDrainer({ intervalMs: 2000 }, { lock: container.distributedLock });
  const purge = startPurgeScheduler(
    {
      intervalHours: 24,
      notificationReadRetentionDays: config.notificationRetention.readDays,
      notificationUnreadRetentionDays: config.notificationRetention.unreadDays,
      batchSize: config.notificationRetention.batchSize,
    },
    {
      prisma,
      settings: container.tenantSettings,
      lock: container.distributedLock,
      effectiveSettings: container.effectiveSettings,
      analytics: container.analytics,
    },
  );
  // F10 (ADD-08 T-74): latido del proceso en Redis (sockets y SFU) para admin/analytics/live|infra.
  // El intervalo no retiene el proceso al apagar; la clave caduca sola (TTL = 3 latidos).
  startInstanceHeartbeat(
    { intervalSeconds: config.platformAnalytics.heartbeatSeconds },
    { store: container.instanceTelemetry, sfu: container.sfu },
  );
  const recordingConsentTimeout = startRecordingConsentTimeoutScheduler(
    { intervalSeconds: 10 },
    {
      effectiveSettings: container.effectiveSettings,
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
  // F3 (T-39): avisos 10/5/1, corte al tope, salas vacías y agendadas vencidas; plazos del resolver.
  const lifecycle = startMeetingLifecycleScheduler(
    { intervalSeconds: 15 },
    { ...container, emitter, lock: container.distributedLock },
  );
  // F4: aviso y corte por `maxCallMinutes` del plan.
  const callLifecycle = startCallLifecycleScheduler(
    { intervalSeconds: 15 },
    { ...container, emitter, lock: container.distributedLock },
  );
  const customerReconciliation = startCustomerReconciliationScheduler(
    { enabled: config.customerReconcile.enabled, intervalHours: config.customerReconcile.intervalHours },
    {
      client: container.customerReconciliation,
      customerDirectory: container.customerDirectory,
      lock: container.distributedLock,
    },
  );
  // F5 avatares — repara UserDirectoryEntry (nombre, foto, estado) contra Auth si se perdio un evento.
  const userDirectoryReconciliation = startUserDirectoryReconciliationScheduler(
    { enabled: config.userReconcile.enabled, intervalHours: config.userReconcile.intervalHours },
    {
      client: container.userDirectoryReconciliation,
      userDirectory: container.userDirectory,
      lock: container.distributedLock,
    },
  );
  // Repara CustomerPortalAccount (portal userId ↔ customerId) contra Auth: chateabilidad y foto del cliente.
  const portalAccountReconciliation = startPortalAccountReconciliationScheduler(
    {
      enabled: config.portalAccountReconcile.enabled,
      intervalHours: config.portalAccountReconcile.intervalHours,
    },
    {
      client: container.portalAccountsReconciliation,
      portalAccounts: container.customerPortalAccountsReconcile,
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
  // F7: ciclo de vida de grabaciones del sistema (recorder, CloudStorage, transcript, limite, retencion).
  const recordingLifecycle = lifecycleFrom({ ...container, emitter });
  if (!recordingLifecycle) throw new Error('Recording lifecycle dependencies are incomplete.');
  bindInAppNotificationConsumers(consumers.register.bind(consumers), {
    notifications: container.inAppNotifications,
    emitter,
  });
  bindSignatureConsumers(consumers.register.bind(consumers), {
    notifications: container.notifications,
    emitter,
    customerPortalAccounts: container.customerPortalAccounts,
  });
  bindCustomerConsumers(consumers.register.bind(consumers), {
    notifications: container.notifications,
    emitter,
    customerDirectory: container.customerDirectory,
    customerPreparerAssignments: container.customerPreparerAssignments,
    customerAssignments: container.customerAssignments,
    inAppSource: config.inAppSource.customerImport,
    customerPortalAccounts: container.customerPortalAccounts,
    userDirectory: container.userDirectory,
  });
  bindAuthConsumers(consumers.register.bind(consumers), {
    userPermissions: container.userPermissions,
    userDirectory: container.userDirectory,
    rolePermissions: container.rolePermissions,
    customerPortalAccounts: container.customerPortalAccounts,
    emitter,
    conversations: container.conversations,
    conversationAudit: container.conversationAudit,
  });
  bindTenantHostConsumers(consumers.register.bind(consumers), {
    hostCache: container.tenantHostCache,
    platformSettings,
  });
  // Offboarding (retiro terminal): baja las proyecciones + reasigna/cancela reuniones del host retirado.
  bindOffboardingConsumers(consumers.register.bind(consumers), {
    userPermissions: container.userPermissions,
    userDirectory: container.userDirectory,
    customerPortalAccounts: container.customerPortalAccounts,
    meetings: container.meetings,
    publisher: container.publisher,
    emitter,
    conversations: container.conversations,
    conversationAudit: container.conversationAudit,
  });
  bindSubscriptionConsumers(consumers.register.bind(consumers), {
    limits: container.limits,
    planCodeCache: container.planCodeCache,
    modulesCache: container.tenantModulesCache,
    effectiveSettings: container.effectiveSettings,
    emitter,
  });

  // Gate de modulo (Entitlements en runtime) — espejo del hook de PermissionPolicyProvider.cs (.NET).
  // Tras conceder un permiso, comprueba si el tenant tiene el modulo al que pertenece. PlatformAdmin
  // ya bypasea en checkPermission; aca se bypasea el actor Service (M2M), que no esta sujeto al plan
  // de ningun tenant.
  //
  // Las dos respuestas posibles del lector NO significan lo mismo, y confundirlas es el bug que le
  // daria acceso completo a un tenant vencido:
  //   `null`  = todavia no llego la proyeccion de entitlements  -> no gatear (consistencia eventual)
  //   `[]`    = se sabe que no tiene ningun modulo habilitado    -> gatear
  //
  // `denied` solo se devuelve con el escalon encendido; apagado registra y deja pasar. La decision de
  // enforzar vive aca y no en checkPermission a proposito (ver ModuleGate en permissions.ts).
  // Misma linea que el lado .NET (ModuleGateRegistration.Announce): el escalon se sube por
  // configuracion, asi que sin esto la unica forma de saber en que modo quedo un despliegue es
  // provocar un 403 en produccion.
  logger.info(
    {
      mode: config.moduleGate.enforce ? 'ENFORCING' : 'log-only',
      modules: config.moduleGate.enforcedModules.length > 0 ? config.moduleGate.enforcedModules : 'all',
    },
    'Module gate registered',
  );
  configureModuleGate(async (subject, required) => {
    if (subject.actorType === 'Service') return { denied: false };
    const module = moduleFor(required);
    if (!module) return { denied: false };
    const enabledModules = await container.tenantModulesCache.getEnabledModules(subject.tenantId);
    if (enabledModules === null) return { denied: false };
    if (enabledModules.includes(module)) return { denied: false };

    // El escalon se decide POR MODULO (ver `shouldEnforceModule`): con la lista vacia se aplican
    // todos, y con modulos listados solo esos — el resto sigue midiendose.
    if (shouldEnforceModule(config.moduleGate, module)) {
      logger.info(
        { permission: required, module, tenantId: subject.tenantId },
        'Module gate: 403 — el plan del tenant no incluye el modulo de este permiso',
      );
      return { denied: true, module };
    }

    logger.info(
      { permission: required, module, tenantId: subject.tenantId },
      'Module gate (log-only): permiso pertenece a un modulo NO habilitado para el tenant; seria 403 al enforzar',
    );
    return { denied: false };
  });
  bindCloudStorageConsumers(consumers.register.bind(consumers), {
    attachmentTracking: container.attachmentTracking,
    notifications: container.notifications,
    emitter,
    inAppSourcePolicy: config.inAppSource.cloudStoragePolicy,
    cloudStorageMetadata: container.cloudStorageMetadata,
    messages: container.messages,
    recordingFiles: {
      onAvailable: (tenantId, fileId, correlationId) =>
        markRecordingFileAvailable({ tenantId, fileId, correlationId }, recordingLifecycle),
      onRejected: (tenantId, fileId, reason, correlationId) =>
        markRecordingFileRejected({ tenantId, fileId, reason, correlationId }, recordingLifecycle),
    },
  });
  bindCloudStorageNotificationConsumers(consumers.register.bind(consumers), {
    notifications: container.notifications,
    emitter,
    inAppSource: config.inAppSource.cloudStorageLegal,
  });
  bindConnectorsConsumers(consumers.register.bind(consumers), {
    notifications: container.notifications,
    emitter,
    inAppSource: config.inAppSource.connectors,
  });
  bindCorrespondenceConsumers(consumers.register.bind(consumers), {
    emitter,
    userPermissions: container.userPermissions,
  });
  bindCampaignConsumers(consumers.register.bind(consumers), { emitter });
  bindWalletConsumers(consumers.register.bind(consumers), { emitter });
  bindSmsConsumers(consumers.register.bind(consumers), { emitter });
  bindCalendarConsumers(consumers.register.bind(consumers), {
    meetings: container.meetings,
    publisher: container.publisher,
    passcodes: container.passcodes,
    settings: container.settings,
    meetingMediaPolicy: container.meetingMediaPolicy,
    effectiveSettings: container.effectiveSettings,
    emitter,
  });
  bindSupportConsumers(consumers.register.bind(consumers), {
    supportTickets: container.supportTickets,
    analytics: container.analytics,
    analyticsDay: (iso) => container.analyticsRecorder.day(iso),
    notifications: container.notifications,
    userPermissions: container.userPermissions,
    emitter,
    inAppSource: config.inAppSource.support,
    conversations: container.conversations,
  });
  // F10: proyecciones — conviven con el handler principal del mismo evento (grabaciones, transcripts…).
  bindAnalyticsConsumers(consumers.registerProjection.bind(consumers), {
    analytics: container.analytics,
    recorder: container.analyticsRecorder,
  });
  bindTranscriptConsumers(consumers.register.bind(consumers), {
    calls: container.calls,
    meetings: container.meetings,
    recordingSessions: container.recordingSessions,
    publisher: container.publisher,
    emitter,
    transcripts: container.transcripts,
    conversations: container.conversations,
    messages: container.messages,
    attachmentTracking: container.attachmentTracking,
    cloudStorageMetadata: container.cloudStorageMetadata,
    lifecycle: recordingLifecycle,
  });
  bindRecordingConsumers(consumers.register.bind(consumers), recordingLifecycle);
  // F7: limite (T-35), respaldo de disponibilidad, composiciones colgadas y retencion (T-37).
  const recordingScheduler = startRecordingScheduler(
    { intervalSeconds: 15, retentionIntervalMinutes: 60 },
    {
      ...recordingLifecycle,
      effectiveSettings: container.effectiveSettings,
      cloudStorageMetadata: container.cloudStorageMetadata,
      settings: container.tenantSettings,
      fileDeletion: container.fileDeletion,
      lock: container.distributedLock,
    },
  );
  // Orígenes del CORS: dominio vigente de la plataforma (Tenant), refrescado cada 5 min.
  platformSettings.start();
  await consumers.start();
  // Re-suscribir el consumer en cada reconexión de Rabbit: el canal nuevo necesita su propio
  // channel.consume. Sin esto, tras el primer corte del broker el servicio queda conectado pero
  // sin consumir (la cola durable se llena y ninguna proyección se actualiza).
  onRabbitReconnected(() => consumers.start());

  // Session-revoked (canal Redis Pub/Sub — separado del canal de notifications
  // de negocio). Cierra CRIT legacy que mezclaba force_logout con notifs.
  const sessionWatcher = startSessionDenylistWatcher(redisSub, emitter, (message) => sessions.revoke(message));

  // Presence transitions online/offline: RedisPresenceService publica en
  // comm:presence:changed:{tenantId}; el watcher se suscribe con PSUBSCRIBE
  // y emite `chat.presence.changed` al room del tenant.
  const presenceWatcher = startPresenceChangedWatcher(redisSub, emitter, container.presenceAudiences);

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
        lifecycle,
        callLifecycle,
        customerReconciliation,
        customerAssignmentReconciliation,
        userDirectoryReconciliation,
        portalAccountReconciliation,
        sessionWatcher,
        presenceWatcher,
        consumers,
        recordingScheduler,
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
  lifecycle: ReturnType<typeof startMeetingLifecycleScheduler>,
  callLifecycle: ReturnType<typeof startCallLifecycleScheduler>,
  customerReconciliation: ReturnType<typeof startCustomerReconciliationScheduler>,
  customerAssignmentReconciliation: ReturnType<typeof startCustomerAssignmentReconciliationScheduler>,
  userDirectoryReconciliation: ReturnType<typeof startUserDirectoryReconciliationScheduler>,
  portalAccountReconciliation: ReturnType<typeof startPortalAccountReconciliationScheduler>,
  sessionWatcher: ReturnType<typeof startSessionDenylistWatcher>,
  presenceWatcher: ReturnType<typeof startPresenceChangedWatcher>,
  consumers: ConsumerRuntime,
  recordingScheduler: ReturnType<typeof startRecordingScheduler>,
): Promise<void> {
  logger.info({ signal }, 'Shutting down (graceful drain up to 30s)');
  try {
    missedCalls.stop();
    outbox.stop();
    purge.stop();
    recordingConsentTimeout.stop();
    lifecycle.stop();
    callLifecycle.stop();
    customerReconciliation.stop();
    customerAssignmentReconciliation.stop();
    userDirectoryReconciliation.stop();
    portalAccountReconciliation.stop();
    recordingScheduler.stop();
    await container.sfu.stop();
    await sessionWatcher.stop();
    await presenceWatcher.stop();

    // Deja de aceptar mensajes NUEVOS de RabbitMQ y espera hasta
    // SHUTDOWN_DRAIN_MS a que los handlers ya en curso (dispatch()) terminen
    // de ackear/nackear antes de seguir — ver docblock de ConsumerRuntime.stop.
    await consumers.stop(SHUTDOWN_DRAIN_MS);
    platformSettings.stop();

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
