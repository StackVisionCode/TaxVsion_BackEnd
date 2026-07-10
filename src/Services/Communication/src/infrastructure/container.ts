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
import { PrismaSupportTicketRepository } from './persistence/prisma-support-ticket-repository.js';
import { PrismaLimitsRepository, PrismaSettingsRepository } from './persistence/prisma-settings-repository.js';
import { PrismaAnalyticsRepository } from './persistence/prisma-analytics-repository.js';
import { RedisCachedTenantSettingsProvider } from './persistence/tenant-settings-provider-impl.js';
import { ConfigPlatformTenantProvider } from './config-platform-tenant-provider.js';
import { RedisPresenceService } from './redis/redis-presence-service.js';
import { HmacTurnCredentialFactory } from './turn/hmac-turn-credential-factory.js';
import { Argon2PasscodeHasher } from './security/argon2-passcode-hasher.js';
import { DominantSpeakerThrottle } from './redis/dominant-speaker-throttle.js';
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
import type { SupportTicketRepository } from '../application/ports/support-ticket-repository.js';
import type { PlatformTenantProvider } from '../application/ports/platform-tenant-provider.js';
import type { LimitsRepository, SettingsRepository } from '../application/ports/settings-repository.js';
import type { AnalyticsRepository } from '../application/ports/analytics-repository.js';

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
  readonly notifications: NotificationRepository;
  readonly processedEvents: ProcessedEventStore;
  readonly userPermissions: UserPermissionsProjectionRepository;
  readonly supportTickets: SupportTicketRepository;
  readonly platform: PlatformTenantProvider;
  readonly tenantSettings: SettingsRepository;
  readonly limits: LimitsRepository;
  readonly analytics: AnalyticsRepository;
}

export function buildContainer(): AppContainer {
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
    notifications: new PrismaNotificationRepository(prisma),
    processedEvents: new PrismaProcessedEventStore(prisma),
    userPermissions: new PrismaUserPermissionsProjectionRepository(prisma),
    supportTickets: new PrismaSupportTicketRepository(prisma),
    platform: new ConfigPlatformTenantProvider(),
    tenantSettings: new PrismaSettingsRepository(prisma),
    limits: new PrismaLimitsRepository(prisma),
    analytics: new PrismaAnalyticsRepository(prisma),
  };
}
