import { prisma } from './persistence/prisma-client.js';
import { redis } from './redis/redis-client.js';
import { PrismaConversationRepository } from './persistence/prisma-conversation-repository.js';
import { PrismaMessageRepository } from './persistence/prisma-message-repository.js';
import { PrismaOutboxPublisher } from './persistence/prisma-outbox-publisher.js';
import { PrismaIdempotencyStore } from './persistence/prisma-idempotency-store.js';
import { PrismaCallRepository } from './persistence/prisma-call-repository.js';
import { PrismaMeetingRepository } from './persistence/prisma-meeting-repository.js';
import { PrismaNotificationRepository } from './persistence/prisma-notification-repository.js';
import { PrismaProcessedEventStore } from './persistence/prisma-processed-event-store.js';
import { PrismaUserPermissionsProjectionRepository } from './persistence/prisma-user-permissions-projection.js';
import { PrismaRolePermissionsProjectionRepository } from './persistence/prisma-role-permissions-projection.js';
import { PrismaCustomerPortalAccountRepository } from './persistence/prisma-customer-portal-account-repository.js';
import { PrismaUserDirectoryRepository } from './persistence/prisma-user-directory-repository.js';
import { PrismaCustomerDirectoryRepository } from './persistence/prisma-customer-directory-repository.js';
import { PrismaAvatarLookupRepository } from './persistence/prisma-avatar-lookup-repository.js';
import { DirectoryAvatarResolver, type AvatarResolver } from '../application/use-cases/resolve-avatars.js';
import { PrismaCustomerPreparerAssignmentRepository } from './persistence/prisma-customer-preparer-assignment-repository.js';
import { PrismaPresenceAudienceRepository } from './persistence/prisma-presence-audience-repository.js';
import { PrismaCustomerAssignmentProjectionRepository } from './persistence/prisma-customer-assignment-projection-repository.js';
import { PrismaAttachmentTrackingRepository } from './persistence/prisma-attachment-tracking-repository.js';
import { PrismaSupportTicketRepository } from './persistence/prisma-support-ticket-repository.js';
import {
  PrismaLimitsRepository,
  PrismaSettingsRepository,
} from './persistence/prisma-settings-repository.js';
import { PrismaAnalyticsRepository } from './persistence/prisma-analytics-repository.js';
import {
  PrismaRecordingSessionRepository,
  PrismaRecordingConsentRepository,
} from './persistence/prisma-recording-repository.js';
import { RedisCachedTenantSettingsProvider } from './persistence/tenant-settings-provider-impl.js';
import { ConfigPlatformTenantProvider } from './config-platform-tenant-provider.js';
import { RedisPresenceService } from './redis/redis-presence-service.js';
import { HmacTurnCredentialFactory } from './turn/hmac-turn-credential-factory.js';
import { Argon2PasscodeHasher } from './security/argon2-passcode-hasher.js';
import { SpeakingThrottle } from './redis/speaking-throttle.js';
import { RedisMeetingReactionLimiter } from './redis/meeting-reaction-throttle.js';
import type { MeetingReactionLimiter } from '../application/ports/meeting-reaction-limiter.js';
import { MeetingReactionAggregator } from '../application/services/meeting-reaction-aggregator.js';
import { SocketRateLimiter } from './redis/socket-rate-limiter.js';
import { HttpRateLimiter } from './redis/http-rate-limiter.js';
import { RedisDistributedLock } from './redis/redis-distributed-lock.js';
import { MediasoupSfuService } from './webrtc/mediasoup-sfu-service.js';
import { PrismaCallUsageRepository } from './persistence/prisma-call-usage-repository.js';
import type { CallUsageRepository } from '../application/ports/call-usage-repository.js';
import { MeetingQualityAggregator } from '../application/services/meeting-quality-aggregator.js';
import { ServiceTokenClient } from './auth/service-token-client.js';
import { HttpTenantHostResolver } from './auth/tenant-host-resolver.js';
import { HttpCloudStorageMetadataClient } from './cloudstorage/http-cloudstorage-metadata-client.js';
import { HttpCloudStorageDownloadClient } from './cloudstorage/http-cloudstorage-download-client.js';
import { HttpCloudStorageUploadClient } from './cloudstorage/http-cloudstorage-upload-client.js';
import { HttpCustomerReconciliationClient } from './customer/http-customer-reconciliation-client.js';
import { HttpUserDirectoryReconciliationClient } from './auth/http-user-directory-reconciliation-client.js';
import { HttpPortalAccountsReconciliationClient } from './auth/http-portal-accounts-reconciliation-client.js';
import { HttpCustomerAssignmentsReconciliationClient } from './customer/http-customer-assignments-reconciliation-client.js';
import { CachedPlanCodeReader } from './rate-limit/cached-plan-code-reader.js';
import { CachedTenantModulesReader } from './rate-limit/cached-tenant-modules-reader.js';
import { HttpPlanRateLimitReader } from './rate-limit/http-plan-rate-limit-reader.js';
import { TierAwareQuotaResolver } from './rate-limit/tier-aware-quota-resolver.js';
import { config } from './config.js';
import type { ConversationRepository } from '../application/ports/conversation-repository.js';
import type { MessageRepository } from '../application/ports/message-repository.js';
import type { IntegrationEventPublisher } from '../application/ports/integration-event-publisher.js';
import type { IdempotencyStore } from '../application/ports/idempotency-store.js';
import type { TenantSettingsProvider } from '../application/ports/tenant-settings-provider.js';
import type { PresenceService } from '../application/ports/presence-service.js';
import type { CallRepository } from '../application/ports/call-repository.js';
import type { TurnCredentialFactory } from '../application/ports/turn-credential-factory.js';
import type { MeetingRepository } from '../application/ports/meeting-repository.js';
import type { PasscodeHasher } from '../application/ports/passcode-hasher.js';
import type { NotificationRepository } from '../application/ports/notification-repository.js';
import type { InAppNotificationRepository } from '../application/ports/in-app-notification-repository.js';
import type { ProcessedEventStore } from '../application/ports/processed-event-store.js';
import type { UserPermissionsProjectionRepository } from '../application/ports/user-permissions-projection-repository.js';
import type { RolePermissionsProjectionRepository } from '../application/ports/role-permissions-projection-repository.js';
import type {
  CustomerPortalAccountReconcileWriter,
  CustomerPortalAccountRepository,
} from '../application/ports/customer-portal-account-repository.js';
import type { UserDirectoryRepository } from '../application/ports/user-directory-repository.js';
import type { CustomerDirectoryRepository } from '../application/ports/customer-directory-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../application/ports/customer-preparer-assignment-repository.js';
import type { PresenceAudienceRepository } from '../application/ports/presence-audience-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../application/ports/customer-assignment-projection-repository.js';
import type { AttachmentTrackingRepository } from '../application/ports/attachment-tracking-repository.js';
import type { SupportTicketRepository } from '../application/ports/support-ticket-repository.js';
import type { PlatformTenantProvider } from '../application/ports/platform-tenant-provider.js';
import type { LimitsRepository, SettingsRepository } from '../application/ports/settings-repository.js';
import type { AnalyticsRepository } from '../application/ports/analytics-repository.js';
import type {
  RecordingSessionRepository,
  RecordingConsentRepository,
} from '../application/ports/recording-repository.js';
import type { SfuService } from '../application/ports/sfu-service.js';
import type { MeetingMediaPolicyProvider } from '../application/ports/meeting-media-policy-provider.js';
import { ResolverMeetingMediaPolicyProvider } from './resolver-meeting-media-policy-provider.js';
import type { PlatformCommunicationSettingsRepository } from '../application/ports/platform-communication-settings-repository.js';
import type { TenantCommunicationConstraintsRepository } from '../application/ports/tenant-communication-constraints-repository.js';
import type { UserCommunicationPreferencesRepository } from '../application/ports/user-communication-preferences-repository.js';
import type { ConfigurationAuditRepository } from '../application/ports/configuration-audit-repository.js';
import type { EffectiveSettingsResolver } from '../application/ports/effective-settings-resolver.js';
import type { TenantSettingsCacheInvalidator } from '../application/ports/tenant-settings-provider.js';
import { PrismaPlatformCommunicationSettingsRepository } from './persistence/prisma-platform-communication-settings-repository.js';
import { PrismaTenantCommunicationConstraintsRepository } from './persistence/prisma-tenant-communication-constraints-repository.js';
import { PrismaUserCommunicationPreferencesRepository } from './persistence/prisma-user-communication-preferences-repository.js';
import { PrismaConfigurationAuditRepository } from './persistence/prisma-configuration-audit-repository.js';
import { RedisEffectiveSettingsResolver } from './persistence/redis-effective-settings-resolver.js';
import type { MeetingEventLogRepository } from '../application/ports/meeting-event-log-repository.js';
import { PrismaMeetingEventLogRepository } from './persistence/prisma-meeting-event-log-repository.js';
import type { RealtimeEmitter } from '../application/ports/realtime-emitter.js';
import type { SocketSessionTerminator } from '../application/ports/socket-session-terminator.js';
import type { TenantHostResolver } from '../application/ports/tenant-host-resolver.js';
import type { TenantHostCache } from '../application/ports/tenant-host-cache.js';
import type { CloudStorageMetadataClient } from '../application/ports/cloudstorage-metadata-client.js';
import type { CloudStorageDownloadClient } from '../application/ports/cloudstorage-download-client.js';
import type { CloudStorageUploadClient } from '../application/ports/cloudstorage-upload-client.js';
import type { TranscriptArtifactRepository } from '../application/ports/transcript-artifact-repository.js';
import type { FileDeletionRequester } from '../application/ports/file-deletion-requester.js';
import { RecordingCaptureService, type RecordingCaptureContext } from '../application/services/recording-capture.service.js';
import { RecordingScope } from '../domain/recording/recording-session-state.js';
import type { RecordingSession } from '../domain/recording/recording-session.js';
import { PrismaTranscriptArtifactRepository } from './persistence/prisma-transcript-artifact-repository.js';
import { HttpRecorderClient } from './recorder/http-recorder-client.js';
import { RabbitFileDeletionRequester } from './rabbit/rabbit-file-deletion-requester.js';

/**
 * Contenedor de dependencias — sin frameworks DI. Cada campo es una interfaz
 * de puerto y su implementacion viene lista. El proceso construye UN unico
 * container en `main.ts` y se lo pasa a los handlers HTTP/socket.
 */
export interface AppContainer {
  readonly conversations: ConversationRepository;
  readonly messages: MessageRepository;
  readonly calls: CallRepository;
  /** F4: minutos de llamada por oficina y mes. */
  readonly callUsage: CallUsageRepository;
  /** F4: agregado de calidad por reunion para `quality_summary.v1`. */
  readonly qualityAggregator: MeetingQualityAggregator;
  readonly meetings: MeetingRepository;
  /** F3: MeetingEventLog, todo lo que pasa dentro de una reunión (C.3). */
  readonly meetingEvents: MeetingEventLogRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly idempotency: IdempotencyStore;
  readonly settings: TenantSettingsProvider;
  readonly presence: PresenceService;
  readonly turn: TurnCredentialFactory;
  readonly passcodes: PasscodeHasher;
  /** F5 (T-69): tope de `meeting.speaking` por segundo. */
  readonly speakingThrottle: SpeakingThrottle;
  /** F5 (D-50): cupos (Redis) y agregado por minuto de reacciones. */
  readonly reactionLimiter: MeetingReactionLimiter;
  readonly reactionAggregator: MeetingReactionAggregator;
  readonly rateLimiter: SocketRateLimiter;
  readonly httpRateLimiter: HttpRateLimiter;
  readonly tierAwareQuota: TierAwareQuotaResolver;
  readonly planCodeCache: CachedPlanCodeReader;
  readonly tenantModulesCache: CachedTenantModulesReader;
  readonly distributedLock: RedisDistributedLock;
  readonly notifications: NotificationRepository;
  readonly inAppNotifications: InAppNotificationRepository;
  readonly processedEvents: ProcessedEventStore;
  readonly userPermissions: UserPermissionsProjectionRepository;
  readonly rolePermissions: RolePermissionsProjectionRepository;
  readonly customerPortalAccounts: CustomerPortalAccountRepository;
  /** La misma instancia que customerPortalAccounts, vista como escritor de la reconciliacion. */
  readonly customerPortalAccountsReconcile: CustomerPortalAccountReconcileWriter;
  readonly userDirectory: UserDirectoryRepository;
  readonly customerDirectory: CustomerDirectoryRepository;
  /** F5 avatares: foto por lote al armar participantes/remitentes (staff → Auth, portal → ficha). */
  readonly avatars: AvatarResolver;
  readonly customerPreparerAssignments: CustomerPreparerAssignmentRepository;
  readonly presenceAudiences: PresenceAudienceRepository;
  readonly customerAssignments: CustomerAssignmentProjectionRepository;
  // Flag GLOBAL de visibilidad por asignacion (P2), leido de config. Los use-cases
  // (picker de customers, gate de chat) lo combinan por OR con el setting por-tenant.
  readonly assignmentVisibilityEnabled: boolean;
  readonly attachmentTracking: AttachmentTrackingRepository;
  readonly supportTickets: SupportTicketRepository;
  readonly platform: PlatformTenantProvider;
  readonly tenantSettings: SettingsRepository;
  readonly limits: LimitsRepository;
  readonly analytics: AnalyticsRepository;
  readonly sfu: SfuService;
  /** Umbral mesh/SFU y cupo por oficina, resueltos desde la capa de configuración. */
  readonly meetingMediaPolicy: MeetingMediaPolicyProvider;
  // Capa de configuración (F2): global → plan → techo de oficina → oficina → usuario.
  readonly platformSettings: PlatformCommunicationSettingsRepository;
  readonly tenantConstraints: TenantCommunicationConstraintsRepository;
  readonly userPreferences: UserCommunicationPreferencesRepository;
  readonly configurationAudit: ConfigurationAuditRepository;
  readonly effectiveSettings: EffectiveSettingsResolver;
  /** La misma instancia que `settings`, vista como invalidador (T-13). */
  readonly settingsCache: TenantSettingsCacheInvalidator;
  readonly recordingSessions: RecordingSessionRepository;
  readonly recordingConsents: RecordingConsentRepository;
  /** F7: estado del transcript de cada grabacion (separado del archivo, T-34). */
  readonly transcripts: TranscriptArtifactRepository;
  /** F7 (D-28): captura server-side por pista hacia `communication-recorder`. */
  readonly capture: RecordingCaptureService;
  /** F7 (T-37): borrado de archivos vencidos via el contrato de CloudStorage. */
  readonly fileDeletion: FileDeletionRequester;
  readonly cloudStorageMetadata: CloudStorageMetadataClient;
  readonly cloudStorageDownload: CloudStorageDownloadClient;
  readonly cloudStorageUpload: CloudStorageUploadClient;
  readonly customerReconciliation: HttpCustomerReconciliationClient;
  readonly userDirectoryReconciliation: HttpUserDirectoryReconciliationClient;
  readonly portalAccountsReconciliation: HttpPortalAccountsReconciliationClient;
  readonly customerAssignmentsReconciliation: HttpCustomerAssignmentsReconciliationClient;
  readonly tenantHostResolver: TenantHostResolver;
  /** La misma instancia que tenantHostResolver: los consumers invalidan lo que el resolver cachea. */
  readonly tenantHostCache: TenantHostCache;
  /**
   * Wired late (post-init) por main.ts inmediatamente despues de construir el
   * Socket.IO server, porque `SocketRealtimeEmitter` necesita el `io` que a
   * su vez necesita el `app.server` de Fastify — el HTTP se construye antes
   * y no puede llevar el emitter en el constructor. Rutas HTTP que necesitan
   * emitir a rooms (Fase Backend 6+) leen `container.emitter` en el momento
   * del request handler, no en el registro; para entonces main.ts ya lo
   * cableo. Undefined solo mientras `buildHttpServer` esta corriendo su
   * pasada de registro — nunca durante un request real.
   */
  emitter?: RealtimeEmitter;
  /** F6: desconecta los sockets de un usuario (guest revocado). Se cablea tarde como `emitter`. */
  sessionTerminator?: SocketSessionTerminator;
}

export function buildContainer(): AppContainer {
  const serviceTokens = new ServiceTokenClient();
  const limitsRepository = new PrismaLimitsRepository(prisma);
  const planCodeCache = new CachedPlanCodeReader(limitsRepository);
  const tenantModulesCache = new CachedTenantModulesReader(limitsRepository);
  const planRateLimitReader = new HttpPlanRateLimitReader(serviceTokens);
  const tenantHost = new HttpTenantHostResolver(serviceTokens);
  const notifications = new PrismaNotificationRepository(prisma);
  const customerPortalAccounts = new PrismaCustomerPortalAccountRepository(prisma);
  const tenantSettingsRepository = new PrismaSettingsRepository(prisma);
  const settingsProvider = new RedisCachedTenantSettingsProvider(prisma, redis);
  const platformSettings = new PrismaPlatformCommunicationSettingsRepository(prisma);
  const tenantConstraints = new PrismaTenantCommunicationConstraintsRepository(prisma);
  const userPreferences = new PrismaUserCommunicationPreferencesRepository(prisma);
  const effectiveSettings = new RedisEffectiveSettingsResolver(redis, {
    platform: platformSettings,
    limits: limitsRepository,
    constraints: tenantConstraints,
    tenantSettings: tenantSettingsRepository,
    preferences: userPreferences,
  });
  const sfu = new MediasoupSfuService();
  const avatars = new DirectoryAvatarResolver(new PrismaAvatarLookupRepository(prisma));
  const calls = new PrismaCallRepository(prisma);
  const meetings = new PrismaMeetingRepository(prisma);
  const meetingEvents = new PrismaMeetingEventLogRepository(prisma);
  const recordingSessions = new PrismaRecordingSessionRepository(prisma);
  const capture = new RecordingCaptureService({
    recorder: new HttpRecorderClient(serviceTokens, { baseUrl: config.recorder.baseUrl, timeoutMs: config.recorder.timeoutMs }),
    sfu,
    recordingSessions,
    meetings,
    calls,
    meetingEvents,
    resolveContext: (session) => resolveCaptureContext(session, effectiveSettings, calls),
    avatars,
    tapListenIp: config.recorder.tapListenIp,
    keyframeIntervalSeconds: config.recorder.keyframeIntervalSeconds,
  });
  return {
    conversations: new PrismaConversationRepository(prisma),
    messages: new PrismaMessageRepository(prisma),
    calls,
    callUsage: new PrismaCallUsageRepository(prisma),
    qualityAggregator: new MeetingQualityAggregator(),
    meetings,
    meetingEvents,
    publisher: new PrismaOutboxPublisher(prisma),
    idempotency: new PrismaIdempotencyStore(prisma, redis),
    settings: settingsProvider,
    settingsCache: settingsProvider,
    platformSettings,
    tenantConstraints,
    userPreferences,
    configurationAudit: new PrismaConfigurationAuditRepository(prisma),
    effectiveSettings,
    presence: new RedisPresenceService(redis),
    turn: new HmacTurnCredentialFactory(),
    passcodes: new Argon2PasscodeHasher(),
    speakingThrottle: new SpeakingThrottle(redis),
    reactionLimiter: new RedisMeetingReactionLimiter(redis),
    reactionAggregator: new MeetingReactionAggregator(),
    rateLimiter: new SocketRateLimiter(redis),
    httpRateLimiter: new HttpRateLimiter(redis),
    tierAwareQuota: new TierAwareQuotaResolver(
      planCodeCache,
      planRateLimitReader,
      config.rateLimit.enforceTierQuotas,
    ),
    planCodeCache,
    tenantModulesCache,
    distributedLock: new RedisDistributedLock(redis),
    notifications,
    inAppNotifications: notifications,
    processedEvents: new PrismaProcessedEventStore(prisma),
    userPermissions: new PrismaUserPermissionsProjectionRepository(prisma),
    rolePermissions: new PrismaRolePermissionsProjectionRepository(prisma),
    customerPortalAccounts,
    customerPortalAccountsReconcile: customerPortalAccounts,
    userDirectory: new PrismaUserDirectoryRepository(prisma),
    customerDirectory: new PrismaCustomerDirectoryRepository(prisma),
    avatars,
    customerPreparerAssignments: new PrismaCustomerPreparerAssignmentRepository(prisma),
    presenceAudiences: new PrismaPresenceAudienceRepository(prisma),
    customerAssignments: new PrismaCustomerAssignmentProjectionRepository(prisma),
    assignmentVisibilityEnabled: config.assignmentVisibility.enabled,
    attachmentTracking: new PrismaAttachmentTrackingRepository(prisma),
    supportTickets: new PrismaSupportTicketRepository(prisma),
    platform: new ConfigPlatformTenantProvider(),
    tenantSettings: tenantSettingsRepository,
    limits: limitsRepository,
    analytics: new PrismaAnalyticsRepository(prisma),
    sfu,
    meetingMediaPolicy: new ResolverMeetingMediaPolicyProvider(effectiveSettings),
    recordingSessions,
    recordingConsents: new PrismaRecordingConsentRepository(prisma),
    transcripts: new PrismaTranscriptArtifactRepository(prisma),
    capture,
    fileDeletion: new RabbitFileDeletionRequester(),
    cloudStorageMetadata: new HttpCloudStorageMetadataClient(serviceTokens),
    cloudStorageDownload: new HttpCloudStorageDownloadClient(serviceTokens),
    cloudStorageUpload: new HttpCloudStorageUploadClient(serviceTokens),
    customerReconciliation: new HttpCustomerReconciliationClient(serviceTokens),
    userDirectoryReconciliation: new HttpUserDirectoryReconciliationClient(serviceTokens),
    portalAccountsReconciliation: new HttpPortalAccountsReconciliationClient(serviceTokens),
    customerAssignmentsReconciliation: new HttpCustomerAssignmentsReconciliationClient(serviceTokens),
    tenantHostResolver: tenantHost,
    tenantHostCache: tenantHost,
  };
}

/**
 * F7: lo que el recorder necesita de la sesion sale de los efectivos de la oficina. El video se graba
 * en reuniones y videollamadas solo si el plan lo permite; el limite ya quedo fijado al empezar.
 */
async function resolveCaptureContext(
  session: RecordingSession,
  effectiveSettings: EffectiveSettingsResolver,
  calls: CallRepository,
): Promise<RecordingCaptureContext> {
  const s = session.toSnapshot();
  const effective = await effectiveSettings.resolve(s.tenantId);
  const isVideoScope =
    s.scope === RecordingScope.Meeting || (await calls.findById(s.tenantId, s.scopeId))?.toSnapshot().kind === 'Video';
  const fallbackMinutes = s.scope === RecordingScope.Meeting ? effective.recording.maxMinutesMeeting : effective.recording.maxMinutesVideoCall;
  return {
    captureVideo: effective.recording.videoEnabled && isVideoScope,
    maxDurationSeconds: s.maxDurationSeconds ?? fallbackMinutes * 60,
    toleranceSeconds: effective.recording.limitToleranceSeconds,
    tracksRetentionDays: effective.recording.tracksRetentionDays,
    composition: effective.recording.composition,
  };
}
