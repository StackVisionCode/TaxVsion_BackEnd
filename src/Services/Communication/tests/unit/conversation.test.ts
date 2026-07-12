import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { computeDirectUniquenessKey } from '../../src/domain/conversations/uniqueness-key.js';

function u(): string {
  return randomUUID();
}

describe('Conversation.startDirect', () => {
  it('creates a Direct conversation with both participants', () => {
    const tenantId = u();
    const initiatorId = u();
    const recipientId = u();
    const result = Conversation.startDirect({
      tenantId,
      initiator: { userId: initiatorId, displayName: 'Ana', actorType: 'TenantEmployee' },
      recipient: { userId: recipientId, displayName: 'Bob', actorType: 'CustomerPortal' },
    });
    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;

    const snapshot = result.value.toSnapshot();
    expect(snapshot.kind).toBe('Direct');
    expect(snapshot.participants).toHaveLength(2);
    expect(snapshot.uniquenessKey).toBe(computeDirectUniquenessKey(initiatorId, recipientId));
  });

  it('rejects self-chat', () => {
    const userId = u();
    const result = Conversation.startDirect({
      tenantId: u(),
      initiator: { userId, displayName: 'Ana', actorType: 'TenantEmployee' },
      recipient: { userId, displayName: 'Ana', actorType: 'TenantEmployee' },
    });
    expect(result.isSuccess).toBe(false);
    if (result.isSuccess) return;
    expect(result.error.code).toBe('Chat.Conversation.SelfChat');
  });

  it('produces the same uniquenessKey regardless of who initiated', () => {
    const a = u();
    const b = u();
    const first = Conversation.startDirect({
      tenantId: u(),
      initiator: { userId: a, displayName: 'A', actorType: 'TenantEmployee' },
      recipient: { userId: b, displayName: 'B', actorType: 'CustomerPortal' },
    });
    const second = Conversation.startDirect({
      tenantId: u(),
      initiator: { userId: b, displayName: 'B', actorType: 'CustomerPortal' },
      recipient: { userId: a, displayName: 'A', actorType: 'TenantEmployee' },
    });
    if (!first.isSuccess || !second.isSuccess) throw new Error('unexpected');
    expect(first.value.toSnapshot().uniquenessKey).toBe(second.value.toSnapshot().uniquenessKey);
  });

  it('marks the recipient as primary preparer when requested (closes Only Owner FE gap)', () => {
    const initiator = u();
    const recipient = u();
    const result = Conversation.startDirect({
      tenantId: u(),
      initiator: { userId: initiator, displayName: 'Client', actorType: 'CustomerPortal' },
      recipient: {
        userId: recipient,
        displayName: 'Preparer',
        actorType: 'TenantEmployee',
        isPrimaryPreparer: true,
      },
    });
    if (!result.isSuccess) throw new Error('unexpected');
    const preparer = result.value.getParticipantSnapshots().find((p) => p.userId === recipient);
    expect(preparer?.isPrimaryPreparer).toBe(true);
  });
});

describe('Conversation.startGroup / addParticipant / removeParticipant', () => {
  const tenantId = u();
  const owner = u();
  const memberA = u();
  const memberB = u();
  const outsider = u();

  function newGroup(): Conversation {
    const r = Conversation.startGroup({
      tenantId,
      groupId: u(),
      title: 'Equipo de auditoria',
      creator: { userId: owner, displayName: 'Owner', actorType: 'TenantEmployee' },
      members: [{ userId: memberA, displayName: 'A', actorType: 'TenantEmployee' }],
    });
    if (!r.isSuccess) throw new Error();
    return r.value;
  }

  it('rejects group with no members', () => {
    const result = Conversation.startGroup({
      tenantId,
      groupId: u(),
      title: 'Solo',
      creator: { userId: owner, displayName: 'Owner', actorType: 'TenantEmployee' },
      members: [],
    });
    expect(result.isSuccess).toBe(false);
    if (result.isSuccess) return;
    expect(result.error.code).toBe('Chat.Conversation.NoMembers');
  });

  it('rejects empty title', () => {
    const result = Conversation.startGroup({
      tenantId,
      groupId: u(),
      title: '   ',
      creator: { userId: owner, displayName: 'Owner', actorType: 'TenantEmployee' },
      members: [{ userId: memberA, displayName: 'A', actorType: 'TenantEmployee' }],
    });
    expect(result.isSuccess).toBe(false);
    if (result.isSuccess) return;
    expect(result.error.code).toBe('Chat.Conversation.MissingTitle');
  });

  it('addParticipant: active participant can add a new member', () => {
    const group = newGroup();
    const result = group.addParticipant({
      actorUserId: owner,
      newMember: { userId: memberB, displayName: 'B', actorType: 'TenantEmployee' },
    });
    expect(result.isSuccess).toBe(true);
    expect(group.getParticipantSnapshots().some((p) => p.userId === memberB && !p.isRemoved)).toBe(true);
  });

  it('addParticipant: rejects a non-participant actor', () => {
    const group = newGroup();
    const result = group.addParticipant({
      actorUserId: outsider,
      newMember: { userId: memberB, displayName: 'B', actorType: 'TenantEmployee' },
    });
    expect(result.isSuccess).toBe(false);
    if (result.isSuccess) return;
    expect(result.error.code).toBe('Chat.Conversation.NotParticipant');
  });

  it('addParticipant: rejects duplicate active participant', () => {
    const group = newGroup();
    const result = group.addParticipant({
      actorUserId: owner,
      newMember: { userId: memberA, displayName: 'A', actorType: 'TenantEmployee' },
    });
    expect(result.isSuccess).toBe(false);
    if (result.isSuccess) return;
    expect(result.error.code).toBe('Chat.Conversation.AlreadyParticipant');
  });

  it('addParticipant: rejects on a Direct conversation', () => {
    const direct = Conversation.startDirect({
      tenantId,
      initiator: { userId: owner, displayName: 'Owner', actorType: 'TenantEmployee' },
      recipient: { userId: memberA, displayName: 'A', actorType: 'TenantEmployee' },
    });
    if (!direct.isSuccess) throw new Error();
    const result = direct.value.addParticipant({
      actorUserId: owner,
      newMember: { userId: memberB, displayName: 'B', actorType: 'TenantEmployee' },
    });
    expect(result.isSuccess).toBe(false);
    if (result.isSuccess) return;
    expect(result.error.code).toBe('Chat.Conversation.NotMultiParty');
  });

  it('removeParticipant: self-removal reports reason Left', () => {
    const group = newGroup();
    const result = group.removeParticipant({ actorUserId: memberA, targetUserId: memberA });
    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value.reason).toBe('Left');
    expect(group.isParticipant(memberA)).toBe(false);
  });

  it('removeParticipant: removing another participant reports reason Kicked', () => {
    const group = newGroup();
    const result = group.removeParticipant({ actorUserId: owner, targetUserId: memberA });
    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value.reason).toBe('Kicked');
    expect(group.isParticipant(memberA)).toBe(false);
  });

  it('removeParticipant: rejects removing a non-participant target', () => {
    const group = newGroup();
    const result = group.removeParticipant({ actorUserId: owner, targetUserId: outsider });
    expect(result.isSuccess).toBe(false);
    if (result.isSuccess) return;
    expect(result.error.code).toBe('Chat.Conversation.NotParticipant');
  });
});

describe('Conversation.startMeetingChat / Meeting self-join (Fase 8)', () => {
  const tenantId = u();
  const meetingId = u();
  const host = u();
  const attendee = u();
  const outsider = u();

  function newMeetingChat(): Conversation {
    const r = Conversation.startMeetingChat({
      tenantId,
      meetingId,
      meetingTitle: 'Weekly sync',
      creator: { userId: host, displayName: 'Host', actorType: 'TenantEmployee' },
    });
    if (!r.isSuccess) throw new Error();
    return r.value;
  }

  it('creates a Meeting conversation with only the creator as participant', () => {
    const chat = newMeetingChat();
    const snapshot = chat.toSnapshot();
    expect(snapshot.kind).toBe('Meeting');
    expect(snapshot.title).toBe('Weekly sync');
    expect(snapshot.uniquenessKey).toBe(`meeting:${meetingId}`);
    expect(snapshot.participants).toHaveLength(1);
  });

  it('addParticipant: self-join works even though the joiner is not active yet', () => {
    const chat = newMeetingChat();
    const result = chat.addParticipant({
      actorUserId: attendee,
      newMember: { userId: attendee, displayName: 'Attendee', actorType: 'TenantEmployee' },
    });
    expect(result.isSuccess).toBe(true);
    expect(chat.isParticipant(attendee)).toBe(true);
  });

  it('addParticipant: a non-participant cannot add someone ELSE (self-join bypass does not extend to inviting others)', () => {
    const chat = newMeetingChat();
    const result = chat.addParticipant({
      actorUserId: outsider,
      newMember: { userId: attendee, displayName: 'Attendee', actorType: 'TenantEmployee' },
    });
    expect(result.isSuccess).toBe(false);
    if (result.isSuccess) return;
    expect(result.error.code).toBe('Chat.Conversation.NotParticipant');
  });

  it('sendText works once self-joined', () => {
    const chat = newMeetingChat();
    chat.addParticipant({
      actorUserId: attendee,
      newMember: { userId: attendee, displayName: 'Attendee', actorType: 'TenantEmployee' },
    });
    const msg = chat.sendText({ senderId: attendee, body: 'hola a todos' });
    expect(msg.isSuccess).toBe(true);
  });

  it('removeParticipant: leaving reports reason Left and revokes send access', () => {
    const chat = newMeetingChat();
    chat.addParticipant({
      actorUserId: attendee,
      newMember: { userId: attendee, displayName: 'Attendee', actorType: 'TenantEmployee' },
    });
    const left = chat.removeParticipant({ actorUserId: attendee, targetUserId: attendee });
    expect(left.isSuccess).toBe(true);
    if (!left.isSuccess) return;
    expect(left.value.reason).toBe('Left');
    const msg = chat.sendText({ senderId: attendee, body: 'ya no deberia poder' });
    expect(msg.isSuccess).toBe(false);
  });

  it('rejects starting a meeting chat that collides with an existing key shape', () => {
    const chat = newMeetingChat();
    expect(() => Conversation.rehydrate({ ...chat.toSnapshot(), kind: 'Group' })).toThrow();
  });
});

describe('Conversation.sendText', () => {
  const tenantId = u();
  const initiator = u();
  const recipient = u();

  function newConv(): Conversation {
    const r = Conversation.startDirect({
      tenantId,
      initiator: { userId: initiator, displayName: 'Ana', actorType: 'TenantEmployee' },
      recipient: { userId: recipient, displayName: 'Bob', actorType: 'CustomerPortal' },
    });
    if (!r.isSuccess) throw new Error();
    return r.value;
  }

  it('appends a Text message and updates lastMessageAt', () => {
    const conv = newConv();
    const msg = conv.sendText({ senderId: initiator, body: 'hola' });
    expect(msg.isSuccess).toBe(true);
    if (!msg.isSuccess) return;
    expect(msg.value.toSnapshot().body).toBe('hola');
    expect(conv.drainPendingMessages()).toHaveLength(1);
    expect(conv.toSnapshot().lastMessageAtUtc).not.toBeNull();
  });

  it('rejects sender that is not a participant', () => {
    const conv = newConv();
    const msg = conv.sendText({ senderId: u(), body: 'trespass' });
    expect(msg.isSuccess).toBe(false);
    if (msg.isSuccess) return;
    expect(msg.error.code).toBe('Chat.Conversation.NotParticipant');
  });

  it('rejects empty body', () => {
    const conv = newConv();
    const msg = conv.sendText({ senderId: initiator, body: '   ' });
    expect(msg.isSuccess).toBe(false);
    if (msg.isSuccess) return;
    expect(msg.error.code).toBe('Chat.Message.EmptyBody');
  });

  it('rejects >4000 chars body', () => {
    const conv = newConv();
    const msg = conv.sendText({ senderId: initiator, body: 'x'.repeat(4001) });
    expect(msg.isSuccess).toBe(false);
    if (msg.isSuccess) return;
    expect(msg.error.code).toBe('Chat.Message.TooLong');
  });
});

describe('Message.editText / softDelete', () => {
  const tenantId = u();
  const initiator = u();
  const recipient = u();

  function firstMessage() {
    const r = Conversation.startDirect({
      tenantId,
      initiator: { userId: initiator, displayName: 'Ana', actorType: 'TenantEmployee' },
      recipient: { userId: recipient, displayName: 'Bob', actorType: 'CustomerPortal' },
    });
    if (!r.isSuccess) throw new Error();
    const msg = r.value.sendText({ senderId: initiator, body: 'original' });
    if (!msg.isSuccess) throw new Error();
    return msg.value;
  }

  it('only sender can edit', () => {
    const msg = firstMessage();
    const other = msg.editText('new', u());
    expect(other.isSuccess).toBe(false);
    if (other.isSuccess) return;
    expect(other.error.code).toBe('Chat.Message.EditForbidden');
  });

  it('editing marks IsEdited=true and updates body', () => {
    const msg = firstMessage();
    const edited = msg.editText('updated', initiator);
    expect(edited.isSuccess).toBe(true);
    const snap = msg.toSnapshot();
    expect(snap.body).toBe('updated');
    expect(snap.isEdited).toBe(true);
    expect(snap.editedAtUtc).not.toBeNull();
  });

  it('non-sender cannot delete unless moderator', () => {
    const msg = firstMessage();
    const attempt = msg.softDelete(u(), false);
    expect(attempt.isSuccess).toBe(false);
    if (attempt.isSuccess) return;
    expect(attempt.error.code).toBe('Chat.Message.DeleteForbidden');
  });

  it('moderator can delete other users messages', () => {
    const msg = firstMessage();
    const attempt = msg.softDelete(u(), true);
    expect(attempt.isSuccess).toBe(true);
    expect(msg.toSnapshot().isDeleted).toBe(true);
  });

  it('sender can always delete', () => {
    const msg = firstMessage();
    const attempt = msg.softDelete(initiator, false);
    expect(attempt.isSuccess).toBe(true);
  });
});
