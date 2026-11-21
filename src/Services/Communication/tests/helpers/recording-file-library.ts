import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { Call } from '../../src/domain/calls/call.js';
import {
  RecordingSession,
  type RecordingSessionSnapshot,
} from '../../src/domain/recording/recording-session.js';
import {
  RecordingCaptureMode,
  type RecordingScope,
  type RecordingSessionState,
} from '../../src/domain/recording/recording-session-state.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import { F7_SNAPSHOT_DEFAULTS } from './fake-recording-session-repository.js';

const u = () => randomUUID();

export interface MeetingGuestSeed {
  readonly userId: string;
  readonly displayName?: string;
  readonly actorType?: string;
  readonly customerId?: string | null;
}

/** Reunión en curso con el anfitrión y, si se piden, más participantes conocidos. */
export function meetingWith(input: {
  tenantId: string;
  hostUserId: string;
  title?: string;
  participants?: readonly MeetingGuestSeed[];
}): Meeting {
  const scheduled = Meeting.schedule({
    tenantId: input.tenantId,
    title: input.title ?? 'Review',
    host: { userId: input.hostUserId, displayName: 'Host' },
  });
  if (!scheduled.isSuccess) throw new Error('schedule');
  const meeting = scheduled.value;
  meeting.start({ hostUserId: input.hostUserId });
  for (const p of input.participants ?? []) {
    const joined = meeting.requestJoin({
      userId: p.userId,
      displayName: p.displayName ?? 'Guest',
      actorType: p.actorType ?? 'TenantEmployee',
      customerId: p.customerId ?? null,
      hasValidInvitation: true,
      passcodeMatch: null,
    });
    if (!joined.isSuccess) throw new Error(joined.error.code);
  }
  return meeting;
}

/** Llamada activa entre dos personas; `calleeCustomerId` la vuelve una llamada con cliente. */
export function callWith(input: {
  tenantId: string;
  callerUserId: string;
  calleeUserId: string;
  callerName?: string;
  calleeName?: string;
  calleeCustomerId?: string | null;
}): Call {
  const initiated = Call.initiate({
    tenantId: input.tenantId,
    kind: 'Audio',
    caller: { userId: input.callerUserId, displayName: input.callerName ?? 'Ana' },
    callee: { userId: input.calleeUserId, displayName: input.calleeName ?? 'Bob' },
  });
  if (!initiated.isSuccess) throw new Error('initiate');
  const call = initiated.value;
  call.accept({
    byUserId: input.calleeUserId,
    calleeDisplayName: input.calleeName ?? 'Bob',
    calleeCustomerId: input.calleeCustomerId ?? null,
  });
  call.markActive();
  return call;
}

export function sessionWith(input: {
  tenantId: string;
  scope: RecordingScope;
  scopeId: string;
  state: RecordingSessionState;
  fileId?: string | null;
  sequence?: number;
  patch?: Partial<RecordingSessionSnapshot>;
}): RecordingSession {
  return RecordingSession.rehydrate({
    id: u(),
    tenantId: input.tenantId,
    scope: input.scope,
    scopeId: input.scopeId,
    state: input.state,
    requestedByUserId: u(),
    requestedAtUtc: new Date(Date.now() - 600_000),
    startedAtUtc: new Date(Date.now() - 300_000),
    stoppedAtUtc: new Date(Date.now() - 120_000),
    recordingFileId: input.fileId ?? null,
    durationSeconds: 60,
    failureReason: null,
    ...F7_SNAPSHOT_DEFAULTS,
    captureMode: RecordingCaptureMode.Server,
    sequence: input.sequence ?? 1,
    ...input.patch,
  });
}

/** Repositorios en memoria de llamadas y reuniones, con el tenant en la búsqueda. */
export function entitiesOf(...items: (Meeting | Call)[]) {
  const meetings = items.filter((i): i is Meeting => i instanceof Meeting);
  const calls = items.filter((i): i is Call => i instanceof Call);
  return {
    meetings: {
      findById: async (tenantId: string, id: string) =>
        meetings.find((m) => m.id === id && m.tenantId === tenantId) ?? null,
      save: async () => undefined,
    } as unknown as MeetingRepository,
    calls: {
      findById: async (tenantId: string, id: string) =>
        calls.find((c) => c.id === id && c.tenantId === tenantId) ?? null,
      save: async () => undefined,
    } as unknown as CallRepository,
  };
}
