import { describe, expect, it } from 'vitest';
import { callSystemMessageBody, formatCallDuration } from '../../src/application/use-cases/append-call-system-message.js';

describe('callSystemMessageBody — copy del evento de llamada en el chat (estilo WhatsApp)', () => {
  it('missed audio / video', () => {
    expect(callSystemMessageBody({ status: 'MissedCall', kind: 'Audio' })).toBe('Missed call');
    expect(callSystemMessageBody({ status: 'MissedCall', kind: 'Video' })).toBe('Missed video call');
  });

  it('ended with duration', () => {
    expect(callSystemMessageBody({ status: 'Ended', kind: 'Audio', durationSeconds: 154 })).toBe('Call · 2:34');
    expect(callSystemMessageBody({ status: 'Ended', kind: 'Video', durationSeconds: 65 })).toBe('Video call · 1:05');
  });

  it('ended with zero/null duration falls back to the plain label', () => {
    expect(callSystemMessageBody({ status: 'Ended', kind: 'Audio', durationSeconds: 0 })).toBe('Call');
    expect(callSystemMessageBody({ status: 'Ended', kind: 'Video', durationSeconds: null })).toBe('Video call');
  });

  it('formatCallDuration pads seconds', () => {
    expect(formatCallDuration(9)).toBe('0:09');
    expect(formatCallDuration(600)).toBe('10:00');
    expect(formatCallDuration(-5)).toBe('0:00');
  });
});
