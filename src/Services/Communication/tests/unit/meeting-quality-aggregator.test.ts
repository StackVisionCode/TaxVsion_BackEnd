import { describe, expect, it } from 'vitest';
import { MeetingQualityAggregator } from '../../src/application/services/meeting-quality-aggregator.js';
import { createSfuTransportStateReporter, SFU_SCORE_BAD, SFU_SCORE_OK } from '../../src/application/use-cases/report-sfu-transport-state.js';
import { InMemoryMeetingEventLog, RecordingPublisher } from '../helpers/fake-meeting-runtime.js';

describe('MeetingQualityAggregator (F4)', () => {
  it('aggregates levels, relay reports, scores and failures per meeting and clears on summarize', () => {
    const agg = new MeetingQualityAggregator();
    agg.recordLevel('m1', 'u1', 'Good', false);
    agg.recordLevel('m1', 'u2', 'Poor', true);
    agg.recordLevel('m1', 'u1', 'Excellent', undefined);
    agg.recordScore('m1', 'u1', 9);
    agg.recordScore('m1', 'u2', 4);
    agg.recordTransportFailure('m1', 'u2');
    agg.recordLevel('m2', 'u9', 'Good', false);

    expect(agg.summarize('m1')).toEqual({
      participants: 2,
      levels: { Good: 1, Poor: 1, Excellent: 1 },
      relayReports: 1,
      sfuScoreSamples: 2,
      sfuScoreAvg: 6.5,
      transportFailures: 1,
    });
    expect(agg.summarize('m1')).toBeNull();
    expect(agg.summarize('m2')?.participants).toBe(1);
    expect(agg.summarize('unknown')).toBeNull();
  });
});

describe('SFU score reporter (F4)', () => {
  const ctx = { tenantId: 't1', meetingId: 'm1', userId: 'u1' };

  it('writes sfu.score to the event log only when crossing the degradation thresholds', async () => {
    const meetingEvents = new InMemoryMeetingEventLog();
    const aggregator = new MeetingQualityAggregator();
    const reporter = createSfuTransportStateReporter({ publisher: new RecordingPublisher(), meetingEvents, qualityAggregator: aggregator });

    const consumer = (score: number) => reporter.onConsumerScore?.({ ...ctx, consumerId: 'c1', producerId: 'p1', score, producerScore: 10 });
    consumer(10);
    consumer(SFU_SCORE_BAD + 1); // sano: nada en el log
    consumer(SFU_SCORE_BAD); // cruza hacia abajo
    consumer(2); // sigue degradado: no se repite
    consumer(SFU_SCORE_OK - 1); // todavia no recupera
    consumer(SFU_SCORE_OK); // recupera
    await new Promise((r) => setTimeout(r, 0));

    const rows = meetingEvents.entries.filter((e) => e.type === 'sfu.score');
    expect(rows.map((e) => (e.payload as { degraded: boolean }).degraded)).toEqual([true, false]);
    expect(aggregator.summarize('m1')?.sfuScoreSamples).toBe(6);
  });

  it('uses the best layer score of a producer and records transport failures', async () => {
    const meetingEvents = new InMemoryMeetingEventLog();
    const aggregator = new MeetingQualityAggregator();
    const publisher = new RecordingPublisher();
    const reporter = createSfuTransportStateReporter({ publisher, meetingEvents, qualityAggregator: aggregator });

    reporter.onProducerScore?.({ ...ctx, producerId: 'p1', kind: 'video', source: 'camera', scores: [{ score: 2, rid: 'r0' }, { score: 8, rid: 'r2' }] });
    reporter.onTransportStateChanged({ ...ctx, direction: 'send', transportId: 'tr1', layer: 'dtls', state: 'failed' });
    await new Promise((r) => setTimeout(r, 0));

    expect(meetingEvents.entries.filter((e) => e.type === 'sfu.score')).toHaveLength(0);
    expect(meetingEvents.entries.filter((e) => e.type === 'transport.failed')).toHaveLength(1);
    expect(publisher.types()).toEqual(['communication.meeting.transport_failed.v1']);
    const summary = aggregator.summarize('m1');
    expect(summary?.transportFailures).toBe(1);
    expect(summary?.sfuScoreAvg).toBe(8);
  });
});
