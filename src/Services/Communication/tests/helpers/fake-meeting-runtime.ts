import { randomUUID } from 'node:crypto';
import type { Meeting } from '../../src/domain/meetings/meeting.js';
import type { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import type { MeetingEventLogEntry, MeetingEventScope } from '../../src/domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { MeetingEventLogRepository } from '../../src/application/ports/meeting-event-log-repository.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { SfuService } from '../../src/application/ports/sfu-service.js';
import type { PresenceService } from '../../src/application/ports/presence-service.js';
import type { RecordingSessionRepository } from '../../src/application/ports/recording-repository.js';
import type { IdempotencyStore } from '../../src/application/ports/idempotency-store.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { SocketEnvelope } from '../../src/contracts/socket/socket-envelope.js';
import { FakeEffectiveSettingsResolver } from './fake-effective-settings.js';
import type { PlatformCommunicationSettingsSnapshot } from '../../src/domain/settings/platform-communication-settings.js';

/** Repositorio en memoria con lo que usan el ciclo de vida y la salida forzada. */
export class InMemoryMeetingRepository {
  readonly meetings = new Map<string, Meeting>();
  async save(meeting: Meeting): Promise<void> {
    this.meetings.set(meeting.id, meeting);
  }
  async findById(tenantId: string, meetingId: string): Promise<Meeting | null> {
    const m = this.meetings.get(meetingId);
    return m && m.tenantId === tenantId ? m : null;
  }
  async listLive(): Promise<Meeting[]> {
    return [...this.meetings.values()].filter((m) => m.status === 'Live');
  }
  async listScheduledForReminder(input: { untilUtc: Date }): Promise<Meeting[]> {
    return [...this.meetings.values()].filter((m) => {
      const s = m.toSnapshot();
      return m.status === 'Scheduled' && s.reminderSentAtUtc === null && s.scheduledForUtc !== null && s.scheduledForUtc.getTime() <= input.untilUtc.getTime();
    });
  }
  async listScheduledBefore(input: { cutoffUtc: Date }): Promise<Meeting[]> {
    return [...this.meetings.values()].filter((m) => {
      const s = m.toSnapshot();
      const anchor = s.scheduledForUtc ?? s.createdAtUtc;
      return m.status === 'Scheduled' && anchor.getTime() < input.cutoffUtc.getTime();
    });
  }
  /** F6: invitaciones en memoria (por id) para probar cierre, revocacion y recordatorio. */
  readonly invitationStore = new Map<string, MeetingInvitation>();
  async saveInvitation(invitation: MeetingInvitation): Promise<void> {
    this.invitationStore.set(invitation.id, invitation);
  }
  async findInvitationById(tenantId: string, invitationId: string): Promise<MeetingInvitation | null> {
    const inv = this.invitationStore.get(invitationId);
    return inv && inv.toSnapshot().tenantId === tenantId ? inv : null;
  }
  async findInvitationByHash(tokenHash: string): Promise<MeetingInvitation | null> {
    return [...this.invitationStore.values()].find((inv) => inv.toSnapshot().tokenHash === tokenHash) ?? null;
  }
  async listInvitationsByMeeting(_tenantId: string, meetingId: string): Promise<MeetingInvitation[]> {
    return [...this.invitationStore.values()].filter((inv) => inv.toSnapshot().meetingId === meetingId);
  }
  asRepository(): MeetingRepository {
    return this as unknown as MeetingRepository;
  }
}

export class InMemoryMeetingEventLog implements MeetingEventLogRepository {
  readonly entries: MeetingEventLogEntry[] = [];
  async append(entry: Omit<MeetingEventLogEntry, 'seq'>): Promise<MeetingEventLogEntry> {
    const seq = this.entries.filter((e) => e.scopeId === entry.scopeId).length + 1;
    const full = { ...entry, seq };
    this.entries.push(full);
    return full;
  }
  async list(input: { scopeId: string; scope: MeetingEventScope; afterSeq?: number; take: number }): Promise<MeetingEventLogEntry[]> {
    return this.entries
      .filter((e) => e.scopeId === input.scopeId && e.scope === input.scope && (input.afterSeq === undefined || e.seq > input.afterSeq))
      .slice(0, input.take);
  }
  types(meetingId: string): string[] {
    return this.entries.filter((e) => e.scopeId === meetingId).map((e) => e.type);
  }
}

export class RecordingPublisher implements IntegrationEventPublisher {
  readonly events: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.events.push(event);
  }
  types(): string[] {
    return this.events.map((e) => e.eventType);
  }
}

export interface EmittedRecord {
  readonly target: 'meeting' | 'user' | 'socket' | 'call';
  readonly id: string;
  readonly event: string;
  readonly payload: unknown;
}

/** Emisor que guarda lo emitido a rooms de reunión, de usuario y a sockets concretos. */
export class RecordingEmitter implements RealtimeEmitter {
  readonly emitted: EmittedRecord[] = [];
  emitToConversation(): void {}
  emitToCall<T>(input: { callId: string; event: string; envelope: SocketEnvelope<T> }): void {
    this.emitted.push({ target: 'call', id: input.callId, event: input.event, payload: input.envelope.payload });
  }
  emitToMeeting<T>(input: { meetingId: string; event: string; envelope: SocketEnvelope<T> }): void {
    this.emitted.push({ target: 'meeting', id: input.meetingId, event: input.event, payload: input.envelope.payload });
  }
  emitToUser<T>(input: { userId: string; event: string; envelope: SocketEnvelope<T> }): void {
    this.emitted.push({ target: 'user', id: input.userId, event: input.event, payload: input.envelope.payload });
  }
  emitToTenant(): void {}
  emitToTenantStaff(): void {}
  emitToTenantMembers(): void {}
  emitBroadcast(): void {}
  emitToSocket<T>(input: { socketId: string; event: string; envelope: SocketEnvelope<T> }): void {
    this.emitted.push({ target: 'socket', id: input.socketId, event: input.event, payload: input.envelope.payload });
  }
  events(target: EmittedRecord['target']): string[] {
    return this.emitted.filter((e) => e.target === target).map((e) => e.event);
  }
}

export class RecordingSfu {
  readonly closedMeetings: string[] = [];
  readonly closedParticipants: string[] = [];
  listProducersForUser(): [] {
    return [];
  }
  async closeParticipant(_meetingId: string, userId: string): Promise<void> {
    this.closedParticipants.push(userId);
  }
  async closeMeeting(meetingId: string): Promise<void> {
    this.closedMeetings.push(meetingId);
  }
  asService(): SfuService {
    return this as unknown as SfuService;
  }
}

export class RecordingPresence {
  readonly cleared: string[] = [];
  async clearBusy(input: { userId: string }): Promise<void> {
    this.cleared.push(input.userId);
  }
  async markBusy(): Promise<void> {}
  asService(): PresenceService {
    return this as unknown as PresenceService;
  }
}

/** Todo lo que necesitan finalize/force-leave/sweep, en memoria y observable. */
export function buildMeetingRuntime(platformPatch: Partial<PlatformCommunicationSettingsSnapshot> = {}) {
  const repository = new InMemoryMeetingRepository();
  const meetingEvents = new InMemoryMeetingEventLog();
  const publisher = new RecordingPublisher();
  const emitter = new RecordingEmitter();
  const sfu = new RecordingSfu();
  const presence = new RecordingPresence();
  const deps = {
    meetings: repository.asRepository(),
    meetingEvents,
    publisher,
    emitter,
    sfu: sfu.asService(),
    presence: presence.asService(),
    recordingSessions: { findByScope: async () => null } as unknown as RecordingSessionRepository,
    idempotency: {} as IdempotencyStore,
    conversations: {
      findById: async () => null,
      save: async () => undefined,
      findByUniquenessKey: async () => null,
    } as unknown as ConversationRepository,
    effectiveSettings: new FakeEffectiveSettingsResolver(platformPatch),
  };
  return { repository, meetingEvents, publisher, emitter, sfu, presence, deps, correlationId: randomUUID() };
}
