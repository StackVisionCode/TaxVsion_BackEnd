import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';

function acceptedCall(kind: 'Audio' | 'Video' = 'Video') {
  const caller = { userId: randomUUID(), displayName: 'Caller' };
  const callee = { userId: randomUUID(), displayName: 'Callee' };
  const created = Call.initiate({ tenantId: randomUUID(), kind, caller, callee });
  if (!created.isSuccess) throw new Error('initiate');
  const call = created.value;
  if (!call.accept({ byUserId: callee.userId, calleeDisplayName: callee.displayName }).isSuccess) throw new Error('accept');
  return { call, caller, callee };
}

/**
 * Nadie pasaba la llamada de Accepted a Active, así que compartir pantalla, pasar de audio a video y
 * adjuntar una grabación con la llamada en curso fallaban. La pasa a Active el primer reporte de
 * calidad de una conexión real: solo sale con WebRTC conectado.
 */
describe('Call · paso a Active con la primera calidad de conexión', () => {
  it('una calidad real pasa la llamada a Active y fija el inicio de la conversación', () => {
    const { call, caller } = acceptedCall();
    const at = new Date('2026-11-13T23:00:05Z');

    expect(call.reportConnectionQuality({ byUserId: caller.userId, quality: 'Good', now: at }).isSuccess).toBe(true);

    expect(call.toSnapshot().status).toBe('Active');
    expect(call.toSnapshot().startedAtUtc).toEqual(at);
  });

  it('Unknown o Disconnected no cuentan como conectada', () => {
    const { call, caller } = acceptedCall();

    call.reportConnectionQuality({ byUserId: caller.userId, quality: 'Unknown' });
    call.reportConnectionQuality({ byUserId: caller.userId, quality: 'Disconnected' });

    expect(call.toSnapshot().status).toBe('Accepted');
    expect(call.toSnapshot().startedAtUtc).toBeNull();
  });

  it('los reportes siguientes no mueven el inicio', () => {
    const { call, caller, callee } = acceptedCall();
    const first = new Date('2026-11-13T23:00:05Z');
    call.reportConnectionQuality({ byUserId: caller.userId, quality: 'Excellent', now: first });

    call.reportConnectionQuality({ byUserId: callee.userId, quality: 'Poor', now: new Date('2026-11-13T23:01:00Z') });

    expect(call.toSnapshot().status).toBe('Active');
    expect(call.toSnapshot().startedAtUtc).toEqual(first);
  });

  it('ya en Active se puede compartir pantalla y dejar de compartir', () => {
    const { call, caller } = acceptedCall();
    call.reportConnectionQuality({ byUserId: caller.userId, quality: 'Good' });

    expect(call.startScreenShare({ actorUserId: caller.userId }).isSuccess).toBe(true);
    expect(call.stopScreenShare({ actorUserId: caller.userId }).isSuccess).toBe(true);
  });

  it('ya en Active una llamada de audio puede pasar a video', () => {
    const { call, caller } = acceptedCall('Audio');
    call.reportConnectionQuality({ byUserId: caller.userId, quality: 'Fair' });

    expect(call.upgradeToVideo({ actorUserId: caller.userId }).isSuccess).toBe(true);
    expect(call.toSnapshot().kind).toBe('Video');
  });

  it('ya en Active se adjunta una grabación con la llamada en curso', () => {
    const { call, caller } = acceptedCall();
    call.reportConnectionQuality({ byUserId: caller.userId, quality: 'Good' });

    expect(call.attachRecording(randomUUID()).isSuccess).toBe(true);
  });

  it('el reporte de alguien que no participa no cambia nada', () => {
    const { call } = acceptedCall();

    expect(call.reportConnectionQuality({ byUserId: randomUUID(), quality: 'Good' }).isSuccess).toBe(false);
    expect(call.toSnapshot().status).toBe('Accepted');
  });
});
