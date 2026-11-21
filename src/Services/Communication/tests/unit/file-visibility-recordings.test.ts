import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { getVisibleFiles } from '../../src/application/use-cases/file-visibility.js';
import { getFileScope } from '../../src/application/use-cases/file-scope.js';
import type { Call } from '../../src/domain/calls/call.js';
import type { Meeting } from '../../src/domain/meetings/meeting.js';
import {
  createFakeProjectionRepository,
  fakeSnapshot,
} from '../helpers/fake-user-permissions-projection-repository.js';
import {
  conversationWith,
  conversationsOf,
  FakeFileLibraryReadRepository,
} from '../helpers/chat-file-library.js';
import { callWith, entitiesOf, meetingWith } from '../helpers/recording-file-library.js';

const u = () => randomUUID();

function world(opts: { permissions?: string[]; assignmentOn?: boolean } = {}) {
  const tenantId = u();
  const me = u();
  const repo = new FakeFileLibraryReadRepository();
  const userPermissions = createFakeProjectionRepository([
    fakeSnapshot({ userId: me, permissions: opts.permissions ?? [] }),
  ]);
  const viewer = {
    tenantId,
    userId: me,
    actorType: 'TenantEmployee',
    canReadChat: true,
    canReadCalls: true,
    canReadMeetings: true,
    holdsCallRecordPermission: false,
  };
  const deps = (...entities: (Meeting | Call)[]) => ({
    fileLibrary: repo,
    conversations: conversationsOf(),
    ...entitiesOf(...entities),
    userPermissions,
    customerAssignments: { isAssigned: async () => false },
    assignmentVisibilityEnabled: opts.assignmentOn ?? false,
  });
  const file = (
    scope: 'Call' | 'Meeting',
    scopeId: string,
    role: 'recording' | 'transcript',
    extra: { sequence?: number; readyCount?: number } = {},
  ) => {
    const fileId = u();
    repo.recordingFiles.push({
      tenantId,
      fileId,
      scope,
      scopeId,
      role,
      sequence: extra.sequence ?? 1,
      readyCount: extra.readyCount ?? 1,
    });
    return fileId;
  };
  return { tenantId, me, repo, viewer, deps, file };
}

describe('getVisibleFiles — rama de grabaciones', () => {
  it('quien participó en una llamada interna ve la grabación y el transcript, marcados como de participantes', async () => {
    const w = world();
    const call = callWith({
      tenantId: w.tenantId,
      callerUserId: w.me,
      calleeUserId: u(),
      calleeName: 'Bob Staff',
    });
    const [rec, txt] = [w.file('Call', call.id, 'recording'), w.file('Call', call.id, 'transcript')];

    const items = await getVisibleFiles(w.viewer, [rec, txt], w.deps(call));

    expect(items).toEqual([
      {
        fileId: rec,
        resourceType: 'call',
        resourceId: call.id,
        title: 'Call with Bob Staff',
        customerId: null,
        context: { participant: 'true' },
        canDownload: true,
        restrictedTo: 'participants',
      },
      expect.objectContaining({
        fileId: txt,
        resourceType: 'call',
        resourceId: call.id,
        restrictedTo: 'participants',
      }),
    ]);
  });

  it('una llamada con cliente donde no participé, con call.record: participant=false, el cliente y sin marca', async () => {
    const w = world();
    const customerId = u();
    const call = callWith({
      tenantId: w.tenantId,
      callerUserId: u(),
      calleeUserId: u(),
      calleeCustomerId: customerId,
    });
    const rec = w.file('Call', call.id, 'recording');

    const items = await getVisibleFiles(
      { ...w.viewer, holdsCallRecordPermission: true },
      [rec],
      w.deps(call),
    );

    expect(items).toEqual([
      expect.objectContaining({
        fileId: rec,
        customerId,
        context: { participant: 'false' },
        restrictedTo: null,
      }),
    ]);
  });

  it('una reunión: el participante la ve con su título y el número si hay varias; un ajeno con meeting.record no', async () => {
    const w = world();
    const meeting = meetingWith({
      tenantId: w.tenantId,
      hostUserId: u(),
      title: 'Quarterly review',
      participants: [{ userId: w.me }],
    });
    const second = w.file('Meeting', meeting.id, 'recording', { sequence: 2, readyCount: 2 });
    const outsiderMeeting = meetingWith({ tenantId: w.tenantId, hostUserId: u() });
    const foreign = w.file('Meeting', outsiderMeeting.id, 'recording');

    const items = await getVisibleFiles(
      { ...w.viewer, holdsCallRecordPermission: true },
      [second, foreign],
      w.deps(meeting, outsiderMeeting),
    );

    expect(items).toEqual([
      expect.objectContaining({
        fileId: second,
        resourceType: 'meeting',
        resourceId: meeting.id,
        title: 'Quarterly review · Recording 2',
        restrictedTo: 'participants',
        context: { participant: 'true' },
      }),
    ]);
  });

  it('sin el permiso de entrada del tipo no responde esa rama', async () => {
    const w = world();
    const call = callWith({ tenantId: w.tenantId, callerUserId: w.me, calleeUserId: u() });
    const meeting = meetingWith({ tenantId: w.tenantId, hostUserId: w.me });
    const [c, m] = [w.file('Call', call.id, 'recording'), w.file('Meeting', meeting.id, 'recording')];

    const noCalls = await getVisibleFiles(
      { ...w.viewer, canReadCalls: false },
      [c, m],
      w.deps(call, meeting),
    );
    const noMeetings = await getVisibleFiles(
      { ...w.viewer, canReadMeetings: false },
      [c, m],
      w.deps(call, meeting),
    );

    expect(noCalls.map((i) => i.fileId)).toEqual([m]);
    expect(noMeetings.map((i) => i.fileId)).toEqual([c]);
  });

  it('un .txt que además es adjunto de chat se contesta como la llamada, nunca como la conversación', async () => {
    const w = world();
    const call = callWith({ tenantId: w.tenantId, callerUserId: w.me, calleeUserId: u() });
    const chat = conversationWith({
      tenantId: w.tenantId,
      kind: 'Direct',
      participants: [{ userId: w.me }, { userId: u() }],
    });
    const txt = w.file('Call', call.id, 'transcript');
    w.repo.attachments.push({ tenantId: w.tenantId, fileId: txt, conversationId: chat.id });

    const items = await getVisibleFiles(w.viewer, [txt], {
      ...w.deps(call),
      conversations: conversationsOf(chat),
    });

    expect(items).toEqual([
      expect.objectContaining({ fileId: txt, resourceType: 'call', resourceId: call.id }),
    ]);
  });
});

describe('getFileScope — llamadas y reuniones', () => {
  it('pide las llamadas según call.record y la asignación, y las reuniones donde participó', async () => {
    const cases = [
      { record: false, assignmentOn: false, viewAll: false, expected: 'none' },
      { record: true, assignmentOn: false, viewAll: false, expected: 'all' },
      { record: true, assignmentOn: true, viewAll: false, expected: 'assigned' },
      { record: true, assignmentOn: true, viewAll: true, expected: 'all' },
    ] as const;
    for (const c of cases) {
      const w = world({ permissions: c.viewAll ? ['customers.view_all'] : [], assignmentOn: c.assignmentOn });
      const [call, meeting] = [u(), u()];
      w.repo.callScope = [{ scopeId: call, lastActivityAtUtc: new Date() }];
      w.repo.meetingScope = [{ scopeId: meeting, lastActivityAtUtc: new Date() }];

      const scope = await getFileScope({ ...w.viewer, holdsCallRecordPermission: c.record }, w.deps());

      expect(scope.callIds).toEqual([call]);
      expect(scope.meetingIds).toEqual([meeting]);
      expect(w.repo.callScopeCalls).toEqual([c.expected]);
    }
  });

  it('sin call.start ni meeting.join las listas van vacías; el corte en 2 000 se avisa por lista', async () => {
    const w = world();
    w.repo.callScope = Array.from({ length: 2_001 }, (_, i) => ({
      scopeId: u(),
      lastActivityAtUtc: new Date(2_000_000 - i),
    }));
    w.repo.meetingScope = [{ scopeId: u(), lastActivityAtUtc: null }];

    const none = await getFileScope({ ...w.viewer, canReadCalls: false, canReadMeetings: false }, w.deps());
    const cut = await getFileScope({ ...w.viewer, canReadChat: false }, w.deps());

    expect(none).toEqual({ conversationIds: [], callIds: [], meetingIds: [], truncated: [] });
    expect(cut.callIds).toHaveLength(2_000);
    expect(cut.truncated).toEqual(['calls']);
  });
});
