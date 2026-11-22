import type { Socket } from 'socket.io';
import type { ConversationFocusStore } from '../../../application/ports/conversation-focus-store.js';
import { ChatSocketEvents, FocusSetPayloadSchema } from '../../../contracts/socket/chat-socket-events.js';

/** El cliente reemite cada 60 s; sin reemitir, el foco caduca solo y vuelve el push. */
export const FOCUS_LEASE_SECONDS = 90;
/** Varias señales seguidas se juntan: se guarda siempre la última (un "oculto" nunca se pierde). */
const COALESCE_MS = 250;

type Ack = (response: { ok: true } | { ok: false; code: string }) => void;

export interface ChatFocusDeps {
  readonly focus: ConversationFocusStore;
  readonly tenantId: string;
  readonly userId: string;
  readonly sessionId: string;
  /** El socket ya está unido a la sala de la conversación (es participante). */
  readonly isJoined: (conversationId: string) => boolean;
  readonly logger: { warn: (obj: object, msg: string) => void };
}

export function attachChatFocus(socket: Pick<Socket, 'on'>, deps: ChatFocusDeps): { dispose: () => Promise<void> } {
  let pending: { visible: boolean; conversationId: string | null } | null = null;
  let timer: ReturnType<typeof setTimeout> | null = null;

  const flush = (): void => {
    timer = null;
    const value = pending;
    pending = null;
    if (!value) return;
    deps.focus
      .set({ tenantId: deps.tenantId, userId: deps.userId, sessionId: deps.sessionId, ...value, leaseSeconds: FOCUS_LEASE_SECONDS })
      .catch((err) => deps.logger.warn({ err }, 'chat focus write failed'));
  };

  socket.on(ChatSocketEvents.FocusSet, (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as Ack) : undefined;
    const parsed = FocusSetPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload' });
      return;
    }
    // El usuario sale del socket autenticado; una conversación ajena se guarda como null.
    const { visible, conversationId } = parsed.data;
    pending = { visible, conversationId: conversationId && deps.isJoined(conversationId) ? conversationId : null };
    timer ??= setTimeout(flush, COALESCE_MS);
    ack?.({ ok: true });
  });

  return {
    dispose: async () => {
      if (timer) clearTimeout(timer);
      timer = null;
      pending = null;
      await deps.focus
        .clear({ tenantId: deps.tenantId, userId: deps.userId, sessionId: deps.sessionId })
        .catch((err) => deps.logger.warn({ err }, 'chat focus clear failed'));
    },
  };
}
