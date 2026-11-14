import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call, type CallSnapshot } from '../../src/domain/calls/call.js';
import type { CallHistoryFilter, CallRepository } from '../../src/application/ports/call-repository.js';
import { getCallHistoryItem, listCallHistory } from '../../src/application/use-cases/list-call-history.js';

const TENANT = randomUUID();
const STAFF = { userId: randomUUID(), displayName: 'Carlos Castillo' };
const COLLEAGUE = { userId: randomUUID(), displayName: 'Ana Ruiz' };
const CLIENT = { userId: randomUUID(), displayName: 'Minato Kenyimura' };
const CUSTOMER_ID = randomUUID();

function endedCall(input: {
  caller: { userId: string; displayName: string };
  callee: { userId: string; displayName: string };
  callerCustomerId?: string;
  calleeCustomerId?: string;
  recordingFileId?: string;
}): CallSnapshot {
  const created = Call.initiate({
    tenantId: TENANT,
    kind: 'Video',
    caller: { ...input.caller, customerId: input.callerCustomerId ?? null },
    callee: input.callee,
  });
  if (!created.isSuccess) throw new Error('initiate');
  const call = created.value;
  call.accept({
    byUserId: input.callee.userId,
    calleeDisplayName: input.callee.displayName,
    ...(input.calleeCustomerId ? { calleeCustomerId: input.calleeCustomerId } : {}),
  });
  const snap = call.toSnapshot();
  return { ...snap, status: 'Ended' as CallSnapshot['status'], durationSeconds: 120, recordingFileId: input.recordingFileId ?? null };
}

function deps(calls: CallSnapshot[], states: Record<string, string> = {}, visibleInPortal = true) {
  const seen: { filter: CallHistoryFilter | undefined }[] = [];
  return {
    seen,
    deps: {
      calls: {
        async listRecentForUser(input: { tenantId: string; userId: string; take: number; skip: number; filter?: CallHistoryFilter }) {
          seen.push({ filter: input.filter });
          return calls.filter((c) => c.participants.some((p) => p.userId === input.userId));
        },
        async countRecentForUser(_t: string, userId: string) {
          return calls.filter((c) => c.participants.some((p) => p.userId === userId)).length;
        },
        async findById(_t: string, id: string) {
          const snap = calls.find((c) => c.id === id);
          return snap ? Call.rehydrate(snap) : null;
        },
      } as unknown as CallRepository,
      recordingSessions: {
        async listStatesByScopeIds(_t: string, _s: string, ids: readonly string[]) {
          return new Map(ids.flatMap((id) => (states[id] ? [[id, states[id]] as const] : [])));
        },
      },
      effectiveSettings: {
        async resolve() {
          return { recording: { visibleInPortal } } as never;
        },
      },
    },
  };
}

const staffViewer = { tenantId: TENANT, userId: STAFF.userId, actorType: 'TenantEmployee' };
const clientViewer = { tenantId: TENANT, userId: CLIENT.userId, actorType: 'CustomerPortal' };

describe('listCallHistory', () => {
  it('da la contraparte, la direccion y la audiencia relativas a quien pide', async () => {
    const withClient = endedCall({ caller: STAFF, callee: CLIENT, calleeCustomerId: CUSTOMER_ID });
    const withTeam = endedCall({ caller: COLLEAGUE, callee: STAFF });
    const { deps: d } = deps([withClient, withTeam]);

    const page = await listCallHistory({ viewer: staffViewer, page: 1, size: 20 }, d);

    expect(page.totalCount).toBe(2);
    expect(page.items[0]).toMatchObject({
      id: withClient.id,
      direction: 'outgoing',
      audience: 'client',
      counterpart: { userId: CLIENT.userId, displayName: CLIENT.displayName, customerId: CUSTOMER_ID },
    });
    expect(page.items[1]).toMatchObject({
      id: withTeam.id,
      direction: 'incoming',
      audience: 'team',
      counterpart: { userId: COLLEAGUE.userId, displayName: COLLEAGUE.displayName, customerId: null },
    });
  });

  it('pasa los filtros al repositorio', async () => {
    const { deps: d, seen } = deps([]);

    await listCallHistory({ viewer: staffViewer, page: 2, size: 10, filter: { audience: 'team', recordedOnly: true } }, d);

    expect(seen[0]?.filter).toEqual({ audience: 'team', recordedOnly: true });
  });

  it('marca el estado de la grabacion y si el staff puede verla', async () => {
    const recorded = endedCall({ caller: STAFF, callee: COLLEAGUE, recordingFileId: randomUUID() });
    const processing = endedCall({ caller: STAFF, callee: COLLEAGUE });
    const none = endedCall({ caller: STAFF, callee: COLLEAGUE });
    const { deps: d } = deps([recorded, processing, none], { [recorded.id]: 'Ready', [processing.id]: 'Processing' });

    const page = await listCallHistory({ viewer: staffViewer, page: 1, size: 20 }, d);

    expect(page.items.find((i) => i.id === recorded.id)?.recording).toEqual({ state: 'Ready', transcriptReady: false, viewable: true });
    expect(page.items.find((i) => i.id === processing.id)?.recording).toEqual({ state: 'Processing', transcriptReady: false, viewable: false });
    expect(page.items.find((i) => i.id === none.id)?.recording).toBeNull();
  });

  it('el cliente del portal solo puede verla si la oficina la hace visible', async () => {
    const recorded = endedCall({ caller: CLIENT, callee: STAFF, callerCustomerId: CUSTOMER_ID, recordingFileId: randomUUID() });

    const hidden = await listCallHistory({ viewer: clientViewer, page: 1, size: 20 }, deps([recorded], { [recorded.id]: 'Ready' }, false).deps);
    const visible = await listCallHistory({ viewer: clientViewer, page: 1, size: 20 }, deps([recorded], { [recorded.id]: 'Ready' }, true).deps);

    expect(hidden.items[0]?.recording?.viewable).toBe(false);
    expect(visible.items[0]?.recording?.viewable).toBe(true);
    expect(visible.items[0]).toMatchObject({ direction: 'outgoing', counterpart: { userId: STAFF.userId } });
  });
});

describe('getCallHistoryItem', () => {
  it('devuelve la llamada a quien participo', async () => {
    const call = endedCall({ caller: STAFF, callee: COLLEAGUE });
    const result = await getCallHistoryItem({ viewer: staffViewer, callId: call.id }, deps([call]).deps);

    expect(result.isSuccess).toBe(true);
    if (result.isSuccess) expect(result.value).toMatchObject({ id: call.id, audience: 'team' });
  });

  it('quien no participo recibe NotFound, sin revelar que existe', async () => {
    const call = endedCall({ caller: COLLEAGUE, callee: CLIENT, calleeCustomerId: CUSTOMER_ID });
    const result = await getCallHistoryItem({ viewer: staffViewer, callId: call.id }, deps([call]).deps);

    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) expect(result.error.code).toBe('Call.NotFound');
  });
});
