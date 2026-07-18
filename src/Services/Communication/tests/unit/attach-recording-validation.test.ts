import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting, type MeetingSnapshot } from '../../src/domain/meetings/meeting.js';
import { Call, type CallSnapshot } from '../../src/domain/calls/call.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import { RecordingSession } from '../../src/domain/recording/recording-session.js';
import { RecordingScope } from '../../src/domain/recording/recording-session-state.js';
import { RecordingConsentEntry } from '../../src/domain/recording/recording-consent-entry.js';
import type {
  RecordingSessionRepository,
  RecordingConsentRepository,
} from '../../src/application/ports/recording-repository.js';
import type { IdempotencyStore, IdempotencyReservation } from '../../src/application/ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type {
  CloudStorageFileMetadata,
  CloudStorageMetadataClient,
} from '../../src/application/ports/cloudstorage-metadata-client.js';
import { attachMeetingRecording } from '../../src/application/use-cases/attach-meeting-recording.js';
import { attachCallRecording } from '../../src/application/use-cases/attach-call-recording.js';

function u(): string {
  return randomUUID();
}

class FakeMeetingRepository implements MeetingRepository {
  private readonly meetings = new Map<string, Meeting>();
  async save(meeting: Meeting): Promise<void> {
    this.meetings.set(meeting.id, meeting);
  }
  async findById(_tenantId: string, meetingId: string): Promise<Meeting | null> {
    return this.meetings.get(meetingId) ?? null;
  }
  async findByShortCode(): Promise<null> {
    return null;
  }
  async findByShortCodeAnyTenant(): Promise<null> {
    return null;
  }
  async saveInvitation(): Promise<void> {}
  async findInvitationByHash(): Promise<null> {
    return null;
  }
  async findInvitationById(): Promise<null> {
    return null;
  }
  async listInvitationsByMeeting(): Promise<MeetingInvitation[]> {
    return [];
  }
  async listUpcomingForUser(): Promise<MeetingSnapshot[]> {
    return [];
  }
  async countUpcomingForUser(): Promise<number> {
    return 0;
  }
  async listPastForUser(): Promise<MeetingSnapshot[]> {
    return [];
  }
  async countPastForUser(): Promise<number> {
    return 0;
  }
}

class FakeCallRepository implements CallRepository {
  private readonly store = new Map<string, Call>();
  async save(call: Call): Promise<void> {
    this.store.set(call.id, call);
  }
  async findById(_tenantId: string, callId: string): Promise<Call | null> {
    return this.store.get(callId) ?? null;
  }
  async findRingingOlderThan(): Promise<CallSnapshot[]> {
    return [];
  }
  async listRecentForUser(): Promise<CallSnapshot[]> {
    return [];
  }
  async countRecentForUser(): Promise<number> {
    return 0;
  }
}

class FakeRecordingSessionRepository implements RecordingSessionRepository {
  private readonly store = new Map<string, RecordingSession>();
  private key(scope: string, scopeId: string): string {
    return `${scope}:${scopeId}`;
  }
  async save(session: RecordingSession): Promise<void> {
    const s = session.toSnapshot();
    this.store.set(this.key(s.scope, s.scopeId), session);
  }
  async findByScope(_tenantId: string, scope: RecordingScope, scopeId: string): Promise<RecordingSession | null> {
    return this.store.get(this.key(scope, scopeId)) ?? null;
  }
  async listStaleRequesting(): Promise<RecordingSession[]> {
    return [];
  }
}

class NoopConsentRepository implements RecordingConsentRepository {
  async append(): Promise<void> {}
  async listByScope(): Promise<RecordingConsentEntry[]> {
    return [];
  }
}

class FakeIdempotencyStore implements IdempotencyStore {
  readonly reservations: string[] = [];
  readonly commits: string[] = [];
  readonly releases: string[] = [];
  async tryReserve<T>(_input: unknown): Promise<IdempotencyReservation<T>> {
    const token = randomUUID();
    this.reservations.push(token);
    return { status: 'fresh', token };
  }
  async commit(): Promise<void> {
    this.commits.push('commit');
  }
  async release(): Promise<void> {
    this.releases.push('release');
  }
}

class FakePublisher implements IntegrationEventPublisher {
  readonly events: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.events.push(event);
  }
}

class FakeEmitter implements RealtimeEmitter {
  emitToConversation(): void {}
  emitToCall(): void {}
  emitToMeeting(): void {}
  emitToUser(): void {}
  emitToTenant(): void {}
}

class StaticMetadataClient implements CloudStorageMetadataClient {
  constructor(private readonly result: CloudStorageFileMetadata | null) {}
  async getMetadata(): Promise<CloudStorageFileMetadata | null> {
    return this.result;
  }
}

function liveMeeting() {
  const host = { userId: u(), displayName: 'Host' };
  const scheduled = Meeting.schedule({ tenantId: u(), title: 'x', host });
  if (!scheduled.isSuccess) throw new Error();
  const meeting = scheduled.value;
  meeting.start({ hostUserId: host.userId });
  return { meeting, host };
}

function activeCall() {
  const caller = { userId: u(), displayName: 'A' };
  const callee = { userId: u(), displayName: 'B' };
  const initiated = Call.initiate({ tenantId: u(), kind: 'Audio', caller, callee });
  if (!initiated.isSuccess) throw new Error();
  const call = initiated.value;
  call.accept({ byUserId: callee.userId, calleeDisplayName: callee.displayName });
  call.markActive();
  return { call, caller, callee };
}

describe('attachMeetingRecording — Fase Backend 8 validation', () => {
  it('rejects with Meeting.Recording.EmptyFile when CloudStorage reports size=0', async () => {
    const meetings = new FakeMeetingRepository();
    const { meeting, host } = liveMeeting();
    await meetings.save(meeting);
    const fileId = u();
    const meta: CloudStorageFileMetadata = { fileId, sizeBytes: 0, mimeType: 'video/webm', originalName: 'x.webm' };
    const result = await attachMeetingRecording(
      { tenantId: meeting.tenantId, correlationId: u(), clientKey: u(), meetingId: meeting.id, actorUserId: host.userId, fileId },
      {
        meetings,
        recordingSessions: new FakeRecordingSessionRepository(),
        idempotency: new FakeIdempotencyStore(),
        publisher: new FakePublisher(),
        emitter: new FakeEmitter(),
        cloudStorageMetadata: new StaticMetadataClient(meta),
      },
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.Recording.EmptyFile');
  });

  it('rejects with Meeting.Recording.FileNotFound when metadata returns null (404)', async () => {
    const meetings = new FakeMeetingRepository();
    const { meeting, host } = liveMeeting();
    await meetings.save(meeting);
    const result = await attachMeetingRecording(
      { tenantId: meeting.tenantId, correlationId: u(), clientKey: u(), meetingId: meeting.id, actorUserId: host.userId, fileId: u() },
      {
        meetings,
        recordingSessions: new FakeRecordingSessionRepository(),
        idempotency: new FakeIdempotencyStore(),
        publisher: new FakePublisher(),
        emitter: new FakeEmitter(),
        cloudStorageMetadata: new StaticMetadataClient(null),
      },
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Meeting.Recording.FileNotFound');
  });

  it('proceeds with legacy path when file is valid (size>0, no RecordingSession)', async () => {
    const meetings = new FakeMeetingRepository();
    const { meeting, host } = liveMeeting();
    await meetings.save(meeting);
    const fileId = u();
    const publisher = new FakePublisher();
    const result = await attachMeetingRecording(
      { tenantId: meeting.tenantId, correlationId: u(), clientKey: u(), meetingId: meeting.id, actorUserId: host.userId, fileId },
      {
        meetings,
        recordingSessions: new FakeRecordingSessionRepository(),
        idempotency: new FakeIdempotencyStore(),
        publisher,
        emitter: new FakeEmitter(),
        cloudStorageMetadata: new StaticMetadataClient({
          fileId,
          sizeBytes: 1_048_576,
          mimeType: 'video/webm',
          originalName: 'r.webm',
        }),
      },
    );
    expect(result.isSuccess).toBe(true);
    expect(publisher.events[0]?.eventType).toBe('communication.meeting.recording_ready.v1');
  });
});

describe('attachCallRecording — Fase Backend 8 validation', () => {
  it('rejects with Call.Recording.EmptyFile when size=0', async () => {
    const calls = new FakeCallRepository();
    const { call, caller } = activeCall();
    await calls.save(call);
    const fileId = u();
    const result = await attachCallRecording(
      { tenantId: call.tenantId, correlationId: u(), clientKey: u(), callId: call.id, actorUserId: caller.userId, fileId },
      {
        calls,
        recordingSessions: new FakeRecordingSessionRepository(),
        idempotency: new FakeIdempotencyStore(),
        publisher: new FakePublisher(),
        emitter: new FakeEmitter(),
        cloudStorageMetadata: new StaticMetadataClient({ fileId, sizeBytes: 0, mimeType: null, originalName: null }),
      },
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Call.Recording.EmptyFile');
  });

  it('rejects with Call.Recording.FileNotFound when metadata is null', async () => {
    const calls = new FakeCallRepository();
    const { call, caller } = activeCall();
    await calls.save(call);
    const result = await attachCallRecording(
      { tenantId: call.tenantId, correlationId: u(), clientKey: u(), callId: call.id, actorUserId: caller.userId, fileId: u() },
      {
        calls,
        recordingSessions: new FakeRecordingSessionRepository(),
        idempotency: new FakeIdempotencyStore(),
        publisher: new FakePublisher(),
        emitter: new FakeEmitter(),
        cloudStorageMetadata: new StaticMetadataClient(null),
      },
    );
    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Call.Recording.FileNotFound');
  });
});

// Verifica que el harness pueda instanciar cosas del recording namespace sin cycles.
describe('NoopConsentRepository is wired but unused', () => {
  it('exists', () => {
    expect(new NoopConsentRepository()).toBeInstanceOf(NoopConsentRepository);
  });
});
