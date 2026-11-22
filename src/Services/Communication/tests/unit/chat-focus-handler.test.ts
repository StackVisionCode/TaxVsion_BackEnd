import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { EventEmitter } from 'node:events';
import { attachChatFocus } from '../../src/api/socket/handlers/chat-focus.js';
import type { ConversationFocusStore } from '../../src/application/ports/conversation-focus-store.js';

type Ack = (value: unknown) => void;
const JOINED = '22222222-2222-4222-8222-222222222222';

function setup(joined: readonly string[] = [JOINED]) {
  const socket = new EventEmitter();
  const writes: Array<{ visible: boolean; conversationId: string | null }> = [];
  const cleared: string[] = [];
  const focus: ConversationFocusStore = {
    set: async (input) => void writes.push({ visible: input.visible, conversationId: input.conversationId }),
    clear: async (input) => void cleared.push(input.sessionId),
    snapshot: async () => ({ active: [], viewing: [] }),
  };
  const handle = attachChatFocus(socket as never, {
    focus,
    tenantId: 't-1',
    userId: 'u-1',
    sessionId: 's-1',
    isJoined: (id) => joined.includes(id),
    logger: { warn: () => undefined },
  });
  const emit = (payload: unknown) =>
    new Promise<unknown>((resolve) => socket.emit('chat.focus.set', payload, resolve as Ack));
  return { emit, writes, cleared, handle };
}

describe('chat.focus.set', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('stores the focused conversation the user takes part in', async () => {
    const { emit, writes } = setup();

    expect(await emit({ visible: true, conversationId: JOINED })).toEqual({ ok: true });
    await vi.runAllTimersAsync();

    expect(writes).toEqual([{ visible: true, conversationId: JOINED }]);
  });

  it('a conversation the user is not in is stored as null', async () => {
    const { emit, writes } = setup();

    await emit({ visible: true, conversationId: '11111111-1111-4111-8111-111111111111' });
    await vi.runAllTimersAsync();

    expect(writes).toEqual([{ visible: true, conversationId: null }]);
  });

  it('a bad payload is refused', async () => {
    const { emit, writes } = setup();

    expect(await emit({ visible: 'yes' })).toEqual({ ok: false, code: 'Chat.BadPayload' });
    await vi.runAllTimersAsync();

    expect(writes).toEqual([]);
  });

  it('a throttled hidden signal still wins', async () => {
    const { emit, writes } = setup();

    await emit({ visible: true, conversationId: JOINED });
    await emit({ visible: true, conversationId: null });
    await emit({ visible: false, conversationId: null });
    await vi.runAllTimersAsync();

    expect(writes.at(-1)).toEqual({ visible: false, conversationId: null });
    expect(writes.length).toBeLessThanOrEqual(2);
  });

  it('disposing clears the session focus', async () => {
    const { emit, handle, cleared } = setup();
    await emit({ visible: true, conversationId: JOINED });

    await handle.dispose();

    expect(cleared).toEqual(['s-1']);
  });
});
