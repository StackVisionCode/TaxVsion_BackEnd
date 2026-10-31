import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import type { Message } from '../../src/domain/conversations/message.js';
import { resolveEffectiveSettings } from '../../src/domain/settings/effective-communication-settings.js';
import { PlatformCommunicationSettings } from '../../src/domain/settings/platform-communication-settings.js';
import { TenantCommunicationSettings } from '../../src/domain/settings/tenant-communication-settings.js';
import { editMessage, type EditMessageDeps } from '../../src/application/use-cases/edit-message.js';
import { listMessageEdits } from '../../src/application/use-cases/list-message-edits.js';
import type { MessageEditRecord, MessageRepository } from '../../src/application/ports/message-repository.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { IdempotencyReservation, IdempotencyStore } from '../../src/application/ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';

const u = () => randomUUID();
const POLICY = { windowMinutes: 15, maxEdits: 5 };
const T0 = new Date('2026-10-31T15:00:00Z');
const minutes = (n: number) => new Date(T0.getTime() + n * 60_000);

function direct(sender = u()) {
  const started = Conversation.startDirect({
    tenantId: 't1',
    initiator: { userId: sender, displayName: 'Ana', actorType: 'TenantEmployee' },
    recipient: { userId: u(), displayName: 'Bob', actorType: 'CustomerPortal' },
  });
  if (!started.isSuccess) throw new Error('conversation');
  return { conversation: started.value, sender };
}

function textMessage(conversation: Conversation, sender: string, body = 'original'): Message {
  const sent = conversation.sendText({ senderId: sender, body, now: T0 });
  if (!sent.isSuccess) throw new Error('message');
  return sent.value;
}

describe('Message.editText — ventana de edición (F8)', () => {
  it('dentro de la ventana edita, cuenta y devuelve el texto anterior para el historial', () => {
    const { conversation, sender } = direct();
    const message = textMessage(conversation, sender);

    const first = message.editText('first fix', sender, POLICY, minutes(5));
    const second = message.editText('second fix', sender, POLICY, minutes(14));

    expect(first.isSuccess && first.value).toMatchObject({ previousBody: 'original', revision: 1 });
    expect(second.isSuccess && second.value).toMatchObject({ previousBody: 'first fix', revision: 2 });
    expect(message.toSnapshot()).toMatchObject({ body: 'second fix', isEdited: true, editCount: 2 });
  });

  it('pasada la ventana, con la ventana en 0, al llegar al tope o sin cambios, rechaza con su código', () => {
    const { conversation, sender } = direct();
    const late = textMessage(conversation, sender);
    const disabled = textMessage(conversation, sender);
    const capped = textMessage(conversation, sender);
    const same = textMessage(conversation, sender);

    const code = (r: { isSuccess: boolean; error?: { code: string } }) => (r.isSuccess ? 'ok' : r.error?.code);
    expect(code(late.editText('x', sender, POLICY, minutes(15.1)))).toBe('Chat.Message.EditWindowExpired');
    expect(code(disabled.editText('x', sender, { windowMinutes: 0, maxEdits: 5 }, minutes(1)))).toBe('Chat.Message.EditDisabled');
    expect(code(capped.editText('a', sender, { windowMinutes: 15, maxEdits: 1 }, minutes(1)))).toBe('ok');
    expect(code(capped.editText('b', sender, { windowMinutes: 15, maxEdits: 1 }, minutes(2)))).toBe('Chat.Message.EditLimitReached');
    expect(code(same.editText('original', sender, POLICY, minutes(1)))).toBe('Chat.Message.EditUnchanged');
  });

  it('editableUntil = envío + ventana; null si la ventana está apagada', () => {
    const { conversation, sender } = direct();
    const message = textMessage(conversation, sender);
    expect(message.editableUntil(POLICY)?.toISOString()).toBe(minutes(15).toISOString());
    expect(message.editableUntil({ windowMinutes: 0, maxEdits: 5 })).toBeNull();
  });
});

describe('Configuración de la ventana por tipo de chat', () => {
  const resolve = (tenantPatch: Partial<ReturnType<TenantCommunicationSettings['toSnapshot']>> = {}, platformPatch = {}) =>
    resolveEffectiveSettings({
      platform: { ...PlatformCommunicationSettings.defaults().toSnapshot(), ...platformPatch },
      limits: null,
      constraints: null,
      tenant: { ...TenantCommunicationSettings.defaults('t1').toSnapshot(), ...tenantPatch },
    });

  it('defaults: 15 min en Direct, Group y Meeting; 5 en Support; 5 ediciones', () => {
    expect(resolve().chat.edit).toEqual({ windowMinutes: { Direct: 15, Group: 15, Support: 5, Meeting: 15 }, maxEdits: 5 });
  });

  it('la oficina puede acortar (o apagar) pero nunca superar el techo de la plataforma', () => {
    const edit = resolve({ chatEditWindowMinutesGroup: 5, chatEditWindowMinutesSupport: 0, chatEditWindowMinutesDirect: 60 }).chat.edit;
    expect(edit.windowMinutes).toMatchObject({ Group: 5, Support: 0, Direct: 15 });

    const ceilings = { ...resolveCeilingsFor() };
    const rejected = TenantCommunicationSettings.validateAgainstCeilings({ chatEditWindowMinutesDirect: 60 }, ceilings);
    expect(!rejected.isSuccess && rejected.error.code).toBe('Settings.AboveCeiling');
  });

  function resolveCeilingsFor() {
    return resolve().ceilings;
  }
});

class FakeIdempotency implements IdempotencyStore {
  async tryReserve<T>(): Promise<IdempotencyReservation<T>> {
    return { status: 'fresh', token: u() };
  }
  async commit(): Promise<void> {}
  async release(): Promise<void> {}
}

function editHarness(conversation: Conversation, message: Message, platformPatch = {}) {
  const recorded: MessageEditRecord[] = [];
  const deps: EditMessageDeps = {
    messages: {
      findById: async () => message,
      recordEdit: async (_t: string, _m: Message, edit: MessageEditRecord) => void recorded.push(edit),
    } as unknown as MessageRepository,
    conversations: { findById: async () => conversation } as unknown as ConversationRepository,
    effectiveSettings: new FakeEffectiveSettingsResolver(platformPatch),
    idempotency: new FakeIdempotency(),
    publisher: { enqueue: async () => undefined } as unknown as IntegrationEventPublisher,
  };
  const edit = (sender: string, body: string) =>
    editMessage({ tenantId: 't1', correlationId: 'c', clientKey: u(), messageId: message.id, senderUserId: sender, body }, deps);
  return { edit, recorded };
}

describe('editMessage — política por tipo de chat (caso de uso)', () => {
  it('edita un mensaje recién enviado, guarda la versión anterior y devuelve el contador', async () => {
    const { conversation, sender } = direct();
    const sent = conversation.sendText({ senderId: sender, body: 'Your W-2 is ready' });
    if (!sent.isSuccess) throw new Error();
    const { edit, recorded } = editHarness(conversation, sent.value);

    const result = await edit(sender, 'Your W-2 and 1099 are ready');

    expect(result.isSuccess && result.value.edited).toMatchObject({ body: 'Your W-2 and 1099 are ready', editCount: 1 });
    expect(recorded).toEqual([expect.objectContaining({ revision: 1, previousBody: 'Your W-2 is ready', editedByUserId: sender })]);
  });

  it('usa la ventana del tipo de chat: un Support de hace 6 minutos ya no se edita (5 min), un Direct sí (15)', async () => {
    const agent = u();
    const support = Conversation.startSupport({
      tenantId: 't1',
      ticketId: u(),
      agent: { userId: agent, displayName: 'Agent', actorType: 'PlatformSupport' },
      customer: { userId: u(), displayName: 'Client', actorType: 'CustomerPortal' },
    });
    if (!support.isSuccess) throw new Error();
    const sixMinutesAgo = new Date(Date.now() - 6 * 60_000);
    const old = support.value.sendText({ senderId: agent, body: 'old', now: sixMinutesAgo });
    if (!old.isSuccess) throw new Error();
    const supportResult = await editHarness(support.value, old.value).edit(agent, 'new');
    expect(!supportResult.isSuccess && supportResult.error.code).toBe('Chat.Message.EditWindowExpired');

    const { conversation, sender } = direct();
    const directOld = conversation.sendText({ senderId: sender, body: 'old', now: sixMinutesAgo });
    if (!directOld.isSuccess) throw new Error();
    expect((await editHarness(conversation, directOld.value).edit(sender, 'new')).isSuccess).toBe(true);
  });

  it('quien ya no puede escribir (conversación archivada) no edita aunque esté en la ventana', async () => {
    const { conversation, sender } = direct();
    const sent = conversation.sendText({ senderId: sender, body: 'hi' });
    if (!sent.isSuccess) throw new Error();
    const archived = Conversation.rehydrate({ ...conversation.toSnapshot(), isArchived: true });

    const result = await editHarness(archived, sent.value).edit(sender, 'hello');

    expect(!result.isSuccess && result.error.code).toBe('Chat.Conversation.Archived');
  });

  it('la plataforma puede apagar la edición en un tipo de chat (ventana 0)', async () => {
    const { conversation, sender } = direct();
    const sent = conversation.sendText({ senderId: sender, body: 'hi' });
    if (!sent.isSuccess) throw new Error();

    const result = await editHarness(conversation, sent.value, { chatEditWindowMinutesDirect: 0 }).edit(sender, 'hello');

    expect(!result.isSuccess && result.error.code).toBe('Chat.Message.EditDisabled');
  });
});

describe('listMessageEdits — "Edited" muestra las versiones', () => {
  function setup(isDeleted = false) {
    const { conversation, sender } = direct();
    const message = textMessage(conversation, sender, 'v1');
    message.editText('v2', sender, POLICY, minutes(1));
    if (isDeleted) message.softDelete(sender, false);
    const edits: MessageEditRecord[] = [{ revision: 1, previousBody: 'v1', editedByUserId: sender, editedAtUtc: minutes(1) }];
    const deps = {
      conversations: { findById: async () => conversation } as unknown as ConversationRepository,
      messages: { findById: async () => message, listEdits: async () => edits } as unknown as MessageRepository,
    };
    const participant = conversation.getParticipantSnapshots()[1]?.userId ?? '';
    return { deps, message, conversation, participant };
  }

  it('el otro participante ve el texto actual y el original', async () => {
    const { deps, message, conversation, participant } = setup();
    const result = await listMessageEdits({ tenantId: 't1', conversationId: conversation.id, messageId: message.id, requesterUserId: participant }, deps);
    expect(result.isSuccess && result.value).toMatchObject({ currentBody: 'v2', versions: [{ revision: 1, body: 'v1' }] });
  });

  it('quien no participa recibe NotParticipant y un mensaje borrado no expone su historial', async () => {
    const outsider = setup();
    const notParticipant = await listMessageEdits(
      { tenantId: 't1', conversationId: outsider.conversation.id, messageId: outsider.message.id, requesterUserId: u() },
      outsider.deps,
    );
    expect(!notParticipant.isSuccess && notParticipant.error.code).toBe('Chat.Conversation.NotParticipant');

    const deleted = setup(true);
    const gone = await listMessageEdits(
      { tenantId: 't1', conversationId: deleted.conversation.id, messageId: deleted.message.id, requesterUserId: deleted.participant },
      deleted.deps,
    );
    expect(!gone.isSuccess && gone.error.code).toBe('Chat.Message.NotFound');
  });
});
