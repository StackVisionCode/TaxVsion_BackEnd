import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { isMemberOfConversation } from '../../src/api/socket/handlers/chat-handlers.js';

/**
 * A1 — los indicadores `chat.typing.*` y `chat.recording.*` se emitían a CUALQUIER conversationId del
 * tenant: bastaba con conocer el id para hacer aparecer "Fulano está escribiendo…" en una conversación
 * ajena, con su nombre visible. Ahora solo se anuncia en una conversación de la que ya se es parte, y
 * la parte se mide por la room a la que el socket se unió al conectar — la misma que usa la entrega.
 */
describe('membresía de conversación para los indicadores', () => {
  const tenant = 'tenant-1';
  const mine = 'conv-mine';
  const others = 'conv-others';
  const rooms = new Set([`t:${tenant}:c:${mine}`, `t:${tenant}:u:someone`]);

  it('deja anunciar en la propia conversación', () => {
    expect(isMemberOfConversation(rooms, tenant, mine)).toBe(true);
  });

  it('no deja anunciar en la conversación de otros', () => {
    expect(isMemberOfConversation(rooms, tenant, others)).toBe(false);
  });

  it('no cruza tenants aunque el id de conversación coincida', () => {
    expect(isMemberOfConversation(rooms, 'tenant-2', mine)).toBe(false);
  });

  it('un socket sin rooms no anuncia en ningún lado', () => {
    expect(isMemberOfConversation(new Set(), tenant, mine)).toBe(false);
  });
});

describe('los cuatro indicadores aplican el chequeo', () => {
  const source = readFileSync(
    fileURLToPath(new URL('../../src/api/socket/handlers/chat-handlers.ts', import.meta.url)),
    'utf8',
  );

  // Cada handler debe filtrar ANTES de emitir. Si mañana se agrega otro indicador y se olvida,
  // esta prueba no lo ve — pero sí ve que se quite alguno de los que ya estan cubiertos.
  const handlers = ['TypingStart', 'TypingStop', 'RecordingStart', 'RecordingStop'];

  it.each(handlers)('%s no emite sin ser parte de la conversación', (handler) => {
    const start = source.indexOf(`socket.on(ChatSocketEvents.${handler},`);
    expect(start).toBeGreaterThan(-1);
    // Hasta el siguiente handler (o el final): basta para ver si filtra antes de emitir.
    const next = source.indexOf('socket.on(ChatSocketEvents.', start + 1);
    const body = source.slice(start, next === -1 ? source.length : next);
    expect(body).toContain('isInConversation(');
  });
});
