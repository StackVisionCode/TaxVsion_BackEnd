import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { Call } from '../../src/domain/calls/call.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import { reportConnectionQuality } from '../../src/application/services/connection-quality.service.js';
import { MeetingQualityAggregator } from '../../src/application/services/meeting-quality-aggregator.js';
import { buildMeetingRuntime } from '../helpers/fake-meeting-runtime.js';

const u = (): string => randomUUID();

describe('reportConnectionQuality (F4, D-39)', () => {
  it('meeting: persists, logs with metrics, aggregates and broadcasts only when the level changes', async () => {
    const rt = buildMeetingRuntime();
    const host = { userId: u(), displayName: 'Host' };
    const r = Meeting.schedule({ tenantId: u(), title: 'Q', host });
    if (!r.isSuccess) throw new Error();
    const meeting = r.value;
    meeting.start({ hostUserId: host.userId, maxDurationMinutes: 60 });
    await rt.repository.save(meeting);
    const aggregator = new MeetingQualityAggregator();
    const deps = { ...rt.deps, calls: {} as CallRepository, qualityAggregator: aggregator };

    const first = await reportConnectionQuality({ scope: 'Meeting', scopeId: meeting.id, tenantId: meeting.tenantId, userId: host.userId, quality: 'Fair', metrics: { rttMs: 320, lossPct: 6, viaRelay: true }, correlationId: 'c' }, deps);
    expect(first.isSuccess && first.value.changed).toBe(true);
    const same = await reportConnectionQuality({ scope: 'Meeting', scopeId: meeting.id, tenantId: meeting.tenantId, userId: host.userId, quality: 'Fair', correlationId: 'c' }, deps);
    expect(same.isSuccess && same.value.changed).toBe(false);

    expect(rt.emitter.events('meeting')).toEqual(['meeting.participant.quality_changed']);
    expect(rt.emitter.emitted[0]?.payload).toEqual({ meetingId: meeting.id, userId: host.userId, quality: 'Fair' });
    expect(rt.meetingEvents.types(meeting.id)).toEqual(['quality.changed']);
    expect(rt.meetingEvents.entries[0]?.payload).toEqual({ quality: 'Fair', metrics: { rttMs: 320, lossPct: 6, viaRelay: true } });
    expect(meeting.toSnapshot().participants[0]?.connectionQuality).toBe('Fair');
    expect(aggregator.summarize(meeting.id)).toMatchObject({ participants: 1, levels: { Fair: 2 }, relayReports: 1 });

    const stranger = await reportConnectionQuality({ scope: 'Meeting', scopeId: meeting.id, tenantId: meeting.tenantId, userId: u(), quality: 'Good', correlationId: 'c' }, deps);
    expect(stranger.isSuccess).toBe(false);
  });

  it('call: persists on the participant and tells the peer', async () => {
    const rt = buildMeetingRuntime();
    const created = Call.initiate({ tenantId: u(), kind: 'Audio', caller: { userId: u(), displayName: 'A' }, callee: { userId: u(), displayName: 'B' } });
    if (!created.isSuccess) throw new Error();
    const call = created.value;
    const snap = call.toSnapshot();
    const calls = { findById: async () => call, save: async () => undefined } as unknown as CallRepository;
    const deps = { ...rt.deps, calls };

    const result = await reportConnectionQuality({ scope: 'Call', scopeId: snap.id, tenantId: snap.tenantId, userId: snap.callerUserId, quality: 'Poor', correlationId: 'c' }, deps);
    expect(result.isSuccess && result.value.changed).toBe(true);
    expect(rt.emitter.events('call')).toEqual(['call.peer_quality_changed']);
    expect(rt.emitter.emitted[0]?.payload).toEqual({ callId: snap.id, peerUserId: snap.callerUserId, quality: 'Poor' });
    expect(call.toSnapshot().participants[0]?.connectionQuality).toBe('Poor');
  });
});
