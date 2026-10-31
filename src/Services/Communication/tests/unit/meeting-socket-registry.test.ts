import { describe, expect, it } from 'vitest';
import { MeetingSocketRegistry } from '../../src/api/socket/handlers/meeting-socket-registry.js';

describe('MeetingSocketRegistry', () => {
  it('devuelve el ultimo socket registrado para un participante', () => {
    const registry = new MeetingSocketRegistry();
    registry.register('t1', 'm1', 'u1', 'sock-a');
    registry.register('t1', 'm1', 'u1', 'sock-b'); // segunda pestaña
    expect(registry.find('t1', 'm1', 'u1')).toBe('sock-b');
  });

  it('no borra el socket vigente cuando se va una pestaña vieja', () => {
    const registry = new MeetingSocketRegistry();
    registry.register('t1', 'm1', 'u1', 'sock-a');
    registry.register('t1', 'm1', 'u1', 'sock-b');
    registry.unregister('t1', 'm1', 'u1', 'sock-a');
    expect(registry.find('t1', 'm1', 'u1')).toBe('sock-b');
    registry.unregister('t1', 'm1', 'u1', 'sock-b');
    expect(registry.find('t1', 'm1', 'u1')).toBeUndefined();
  });

  it('aisla por tenant y meeting', () => {
    const registry = new MeetingSocketRegistry();
    registry.register('t1', 'm1', 'u1', 'sock-a');
    expect(registry.find('t2', 'm1', 'u1')).toBeUndefined();
    expect(registry.find('t1', 'm2', 'u1')).toBeUndefined();
  });
});
