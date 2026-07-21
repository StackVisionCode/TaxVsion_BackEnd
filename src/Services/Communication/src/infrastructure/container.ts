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
import { PrismaNotificationActionMappingRepository } from './persistence/prisma-notification-action-mapping-repository.js';
import { PrismaUserDirectoryRepository } from './persistence/prisma-user-directory-repository.js';
import { PrismaCustomerDirectoryRepository } from './persistence/prisma-customer-directory-repository.js';
import { PrismaCustomerPreparerAssignmentRepository } from './persistence/prisma-customer-preparer-assignment-repository.js';
import { PrismaAttachmentTrackingRepository } from './persistence/prisma-attachment-tracking-repository.js';
import { PrismaSupportTicketRepository } from './persistence/prisma-support-ticket-repository.js';
import { PrismaLimitsRepository, PrismaSettingsRepository } from './persistence/prisma-settings-repository.js';
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
import { DominantSpeakerThrottle } from './redis/dominant-speaker-throttle.js';
import { SocketRateLimiter } from './redis/socket-rate-limiter.js';
import { RedisDistributedLock } from './redis/redis-distributed-lock.js';
import { MediasoupSfuService } from './webrtc/mediasoup-sfu-service.js';
import { ServiceTokenClient } from './auth/service-token-client.js';
import { HttpCloudStorageMetadataClient } from './cloudstorage/http-cloudstorage-metadata-client.js';
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
import type { ProcessedEventStore } from '../application/ports/processed-event-store.js';
import type { UserPermissionsProjectionRepository } from '../application/ports/user-permissions-projection-repository.js';
import type { RolePermissionsProjectionRepository } from '../application/ports/role-permissions-projection-repository.js';
import type { CustomerPortalAccountRepository } from '../application/ports/customer-portal-account-repository.js';
import type { NotificationActionMappingRepository } from '../application/ports/notification-action-mapping-repository.js';
import type { UserDirectoryRepository } from '../application/ports/user-directory-repository.js';
import type { CustomerDirectoryRepository } from '../application/ports/customer-directory-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../application/ports/customer-preparer-assignment-repository.js';
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
import type { RealtimeEmitter } from '../application/ports/realtime-emitter.js';
import type { CloudStorageMetadataClient } from '../application/ports/cloudstorage-metadata-client.js';

/**
 * Contenedor de dependencias — sin frameworks DI. Cada campo es una interfaz
 * de puerto y su implementacion viene lista. El proceso construye UN unico
 * container en `main.ts` y se lo pasa a los handlers HTTP/socket.
 */
export interface AppContainer {
  readonly conversations: ConversationRepository;
  readonly messages: MessageRepository;
  readonly calls: CallRepository;
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly idempotency: IdempotencyStore;
  readonly settings: TenantSettingsProvider;
  readonly presence: PresenceService;
  readonly turn: TurnCredentialFactory;
  readonly passcodes: PasscodeHasher;
  readonly dominantSpeakerThrottle: DominantSpeakerThrottle;
  readonly rateLimiter: SocketRateLimiter;
  readonly distributedLock: RedisDistributedLock;
  readonly notifications: NotificationRepository;
  readonly processedEvents: ProcessedEventStore;
  readonly userPermissions: UserPermissionsProjectionRepository;
  readonly rolePermissions: RolePermissionsProjectionRepository;
  readonly customerPortalAccounts: CustomerPortalAccountRepository;
  readonly notificationActionMappings: NotificationActionMappingRepository;
  readonly userDirectory: UserDirectoryRepository;
  readonly customerDirectory: CustomerDirectoryRepository;
  readonly customerPreparerAssignments: CustomerPreparerAssignmentRepository;
  readonly attachmentTracking: AttachmentTrackingRepository;
  readonly supportTickets: SupportTicketRepository;
  readonly platform: PlatformTenantProvider;
  readonly tenantSettings: SettingsRepository;
  readonly limits: LimitsRepository;
  readonly analytics: AnalyticsRepository;
  readonly sfu: SfuService;
  readonly recordingSessions: RecordingSessionRepository;
  readonly recordingConsents: RecordingConsentRepository;
  readonly cloudStorageMetadata: CloudStorageMetadataClient;
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
}

export function buildContainer(): AppContainer {
  const serviceTokens = new ServiceTokenClient();
  return {
    conversations: new PrismaConversationRepository(prisma),
    messages: new PrismaMessageRepository(prisma),
    calls: new PrismaCallRepository(prisma),
    meetings: new PrismaMeetingRepository(prisma),
    publisher: new PrismaOutboxPublisher(prisma),
    idempotency: new PrismaIdempotencyStore(prisma, redis),
    settings: new RedisCachedTenantSettingsProvider(prisma, redis),
    presence: new RedisPresenceService(redis),
    turn: new HmacTurnCredentialFactory(),
    passcodes: new Argon2PasscodeHasher(),
    dominantSpeakerThrottle: new DominantSpeakerThrottle(redis),
    rateLimiter: new SocketRateLimiter(redis),
    distributedLock: new RedisDistributedLock(redis),
    notifications: new PrismaNotificationRepository(prisma),
    processedEvents: new PrismaProcessedEventStore(prisma),
    userPermissions: new PrismaUserPermissionsProjectionRepository(prisma),
    rolePermissions: new PrismaRolePermissionsProjectionRepository(prisma),
    customerPortalAccounts: new PrismaCustomerPortalAccountRepository(prisma),
    notificationActionMappings: new PrismaNotificationActionMappingRepository(prisma),
    userDirectory: new PrismaUserDirectoryRepository(prisma),
    customerDirectory: new PrismaCustomerDirectoryRepository(prisma),
    customerPreparerAssignments: new PrismaCustomerPreparerAssignmentRepository(prisma),
    attachmentTracking: new PrismaAttachmentTrackingRepository(prisma),
    supportTickets: new PrismaSupportTicketRepository(prisma),
    platform: new ConfigPlatformTenantProvider(),
    tenantSettings: new PrismaSettingsRepository(prisma),
    limits: new PrismaLimitsRepository(prisma),
    analytics: new PrismaAnalyticsRepository(prisma),
    sfu: new MediasoupSfuService(),
    recordingSessions: new PrismaRecordingSessionRepository(prisma),
    recordingConsents: new PrismaRecordingConsentRepository(prisma),
    cloudStorageMetadata: new HttpCloudStorageMetadataClient(serviceTokens),
  };
}
