import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { SfuProducePayloadSchema } from '../../src/contracts/socket/meeting-socket-events.js';

/** Fase B: el screen-share viaja como un `source:'screen'` separado del `'camera'` para que un
 *  participante produzca DOS videos a la vez. Retrocompatible: un cliente viejo sin `source` = 'camera'. */
describe('SfuProducePayloadSchema source tagging', () => {
  const base = {
    meetingId: randomUUID(),
    transportId: 'transport-1',
    kind: 'video' as const,
    rtpParameters: {},
  };

  it('defaults source to camera when omitted (retrocompatible)', () => {
    const parsed = SfuProducePayloadSchema.parse(base);
    expect(parsed.source).toBe('camera');
  });

  it('accepts source screen', () => {
    const parsed = SfuProducePayloadSchema.parse({ ...base, source: 'screen' });
    expect(parsed.source).toBe('screen');
  });

  it('rejects an unknown source', () => {
    expect(SfuProducePayloadSchema.safeParse({ ...base, source: 'window' }).success).toBe(false);
  });
});
