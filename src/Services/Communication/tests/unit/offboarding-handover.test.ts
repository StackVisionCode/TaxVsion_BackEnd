import { randomUUID as u } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { Conversation, type ConversationSnapshot } from '../../src/domain/conversations/conversation.js';
import { computeDirectUniquenessKey } from '../../src/domain/conversations/uniqueness-key.js';
import type { Message } from '../../src/domain/conversations/message.js';
import { Meeting } from '../../src/domain/meetings/meeting.js';
import { MeetingInvitation } from '../../src/domain/meetings/meeting-invitation.js';
import { SupportTicket, type SupportTicketSnapshot } from '../../src/domain/support/support-ticket.js';
import { RecordingSession } from '../../src/domain/recording/recording-session.js';
import { RecordingCaptureMode, RecordingScope } from '../../src/domain/recording/recording-session-state.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { ConversationAuditEntry, ConversationAuditRepository } from '../../src/application/ports/conversation-audit-repository.js';
import type { SupportTicketRepository } from '../../src/application/ports/support-ticket-repository.js';
import type { UserDirectoryEntrySnapshot } from '../../src/application/ports/user-directory-repository.js';
import type { CustomerPortalAccountSnapshot } from '../../src/application/ports/customer-portal-account-repository.js';
import type { MeetingRepository } from '../../src/application/ports/meeting-repository.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import {
  failRequestedClientRecordings,
  handOverDirectChats,
  handOverSupportTickets,
  resolveOfficeAdmin,
  type OffboardingContext,
} from '../../src/application/use-cases/offboarding-handover.js';
import { getOffboardingImpact } from '../../src/application/use-cases/offboarding-impact.js';
import { removeUserFromGroups } from '../../src/application/use-cases/group-administration.js';
import { listClosedClientChats } from '../../src/application/use-cases/closed-client-chat-access.js';
import { getMessages } from '../../src/application/use-cases/get-messages.js';
import { createFakeProjectionRepository, fakeSnapshot } from '../helpers/fake-user-permissions-projection-repository.js';
import { InMemoryRecordingSessionRepository } from '../helpers/fake-recording-session-repository.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';
import { InMemoryMeetingRepository, RecordingEmitter, RecordingPublisher } from '../helpers/fake-meeting-runtime.js';

/** Retiro de un empleado en Communication: chats Direct (D-4), soporte (D-5), grabaciones, pre-flight y lectura de la oficina. */
const tenantId = u();
const platformTenant = u();
const leaver = u();
const successor = u();
const admin = u();
const colleague = u();
const client = u();
const client2 = u();
const outsider = u();

function entry(userId: string, displayName: string, actorType: string, over: Partial<UserDirectoryEntrySnapshot> = {}): UserDirectoryEntrySnapshot {
  return { userId, tenantId, displayName, email: `${userId}@x.test`, isActive: true, actorType, avatarFileId: null, updatedAtUtc: new Date(0), ...over };
}

const directory = new Map<string, UserDirectoryEntrySnapshot>([
  [leaver, entry(leaver, 'Lena', 'TenantEmployee', { isActive: false })],
  [successor, entry(successor, 'Sam', 'TenantEmployee')],
  [admin, entry(admin, 'Ada', 'TenantAdmin')],
  [colleague, entry(colleague, 'Cleo', 'TenantEmployee')],
  [client, entry(client, 'Diego', 'CustomerPortal')],
  [client2, entry(client2, 'Eva', 'CustomerPortal')],
  [outsider, entry(outsider, 'Otto', 'TenantEmployee')],
]);
const portals = new Map<string, CustomerPortalAccountSnapshot>([
  [client, { customerId: 'cust-1', tenantId, userId: client, isActive: true }],
  [client2, { customerId: 'cust-2', tenantId, userId: client2, isActive: true }],
]);

class MemoryConversations implements ConversationRepository {
  readonly rows = new Map<string, ConversationSnapshot>();
  readonly systemTexts = new Map<string, string[]>();
  async save(c: Conversation): Promise<void> {
    for (const m of c.drainPendingMessages() as Message[]) {
      if (m.toSnapshot().kind !== 'System') continue;
      this.systemTexts.set(c.id, [...(this.systemTexts.get(c.id) ?? []), m.toSnapshot().body ?? '']);
    }
    this.rows.set(c.id, c.toSnapshot());
  }
  async findById(_t: string, id: string): Promise<Conversation | null> {
    const s = this.rows.get(id);
    return s ? Conversation.rehydrate(s) : null;
  }
  async findByUniquenessKey(_t: string, key: string): Promise<Conversation | null> {
    const s = [...this.rows.values()].find((r) => r.uniquenessKey === key);
    return s ? Conversation.rehydrate(s) : null;
  }
  async listForUser(input: { userId: string; take: number; skip: number; includeArchived?: boolean; kinds?: readonly string[] }) {
    return [...this.rows.values()]
      .filter((s) => s.participants.some((p) => p.userId === input.userId && !p.isRemoved))
      .filter((s) => input.includeArchived || !s.isArchived)
      .filter((s) => !input.kinds || input.kinds.includes(s.kind))
      .slice(input.skip, input.skip + input.take);
  }
  async countForUser() {
    return 0;
  }
  async countUnreadByKind() {
    return { Direct: 0, Group: 0, Support: 0, Meeting: 0 };
  }
  async listMessages() {
    return [];
  }
  async countUnreadForUser() {
    return 0;
  }
  get(id: string): Conversation {
    return Conversation.rehydrate(this.rows.get(id)!);
  }
}

class MemoryAudit implements ConversationAuditRepository {
  readonly entries: ConversationAuditEntry[] = [];
  async append(entries: readonly ConversationAuditEntry[]) {
    this.entries.push(...entries);
  }
  async list() {
    return [];
  }
}

class MemoryTickets implements SupportTicketRepository {
  readonly rows = new Map<string, SupportTicket>();
  async save(t: SupportTicket): Promise<void> {
    this.rows.set(t.id, SupportTicket.rehydrate(t.toSnapshot()));
  }
  async findById(id: string): Promise<SupportTicket | null> {
    const t = this.rows.get(id);
    return t ? SupportTicket.rehydrate(t.toSnapshot()) : null;
  }
  async listForCustomer(input: { tenantId: string; openedByUserId: string; take: number; skip: number }): Promise<SupportTicketSnapshot[]> {
    return this.snapshots()
      .filter((s) => s.tenantId === input.tenantId && s.openedByUserId === input.openedByUserId && s.status !== 'Closed')
      .slice(input.skip, input.skip + input.take);
  }
  async countForCustomer() {
    return 0;
  }
  async listForAgentTenant(input: { agentTenantId: string; assignedAgentId?: string | null; take: number; skip: number }): Promise<SupportTicketSnapshot[]> {
    return this.snapshots()
      .filter((s) => s.agentTenantId === input.agentTenantId && (!input.assignedAgentId || s.assignedAgentId === input.assignedAgentId) && s.status !== 'Closed')
      .slice(input.skip, input.skip + input.take);
  }
  async countForAgentTenant() {
    return 0;
  }
  private snapshots(): SupportTicketSnapshot[] {
    return [...this.rows.values()].map((t) => t.toSnapshot());
  }
}

function harness(opts: { adminHasViewAll?: boolean; chatEnabled?: boolean } = {}) {
  const conversations = new MemoryConversations();
  const conversationAudit = new MemoryAudit();
  const emitter = new RecordingEmitter();
  const publisher = new RecordingPublisher();
  const userPermissions = createFakeProjectionRepository([
    fakeSnapshot({ userId: admin, tenantId, actorType: 'TenantAdmin', permissions: opts.adminHasViewAll === false ? [] : ['customers.view_all'] }),
    fakeSnapshot({ userId: successor, tenantId, permissions: [] }),
    fakeSnapshot({ userId: outsider, tenantId, permissions: [] }),
  ]);
  const deps = {
    conversations,
    conversationAudit,
    emitter: { ...emitter, emitToConversation: () => undefined, emitToTenant: () => undefined, emitToTenantStaff: () => undefined, emitToTenantMembers: () => undefined, emitBroadcast: () => undefined, emitToUser: emitter.emitToUser.bind(emitter), emitToCall: emitter.emitToCall.bind(emitter), emitToMeeting: emitter.emitToMeeting.bind(emitter) },
    publisher,
    customerPortalAccounts: { findActiveByUserId: async (id: string) => portals.get(id) ?? null },
    settings: { get: async () => ({ tenantId, chatEnabled: opts.chatEnabled ?? true, employeeToEmployeeChatEnabled: true, restrictCustomerChatToAssignedPreparer: true, screenshotsEnabled: true }) },
    userDirectory: { findByUserId: async (id: string) => directory.get(id) ?? null },
    userPermissions,
  };
  return { ...deps, emitterLog: emitter, deps };
}

function ctx(over: Partial<OffboardingContext> = {}): OffboardingContext {
  return {
    tenantId,
    leaverUserId: leaver,
    leaverDisplayName: 'Lena',
    successor: { userId: successor, displayName: 'Sam', actorType: 'TenantEmployee' },
    offboardedByUserId: admin,
    correlationId: u(),
    ...over,
  };
}

async function directChat(h: ReturnType<typeof harness>, staffId: string, otherId: string, otherType: string, primary: boolean): Promise<string> {
  const started = Conversation.startDirect({
    tenantId,
    initiator: { userId: otherId, displayName: directory.get(otherId)!.displayName, actorType: otherType },
    recipient: { userId: staffId, displayName: directory.get(staffId)!.displayName, actorType: 'TenantEmployee', isPrimaryPreparer: primary },
  });
  if (!started.isSuccess) throw new Error('start failed');
  started.value.sendText({ senderId: otherId, body: 'Hello' });
  await h.conversations.save(started.value);
  return started.value.id;
}

describe('D-4 — chats Direct del retirado', () => {
  it('closes the client chat (read-only, not primary, system notice) and continues with the successor as primary', async () => {
    const h = harness();
    const oldId = await directChat(h, leaver, client, 'CustomerPortal', true);

    const report = await handOverDirectChats(ctx(), h.deps);

    expect(report).toEqual({ clientChatsClosed: 1, staffChatsLeft: 0, clientChatsOpened: 1 });
    const old = h.conversations.get(oldId);
    expect(old.isArchived).toBe(true);
    const leaverRow = old.getParticipantSnapshots().find((p) => p.userId === leaver)!;
    expect(leaverRow.isRemoved).toBe(true);
    expect(leaverRow.isPrimaryPreparer).toBe(false);
    expect(old.isClosedClientChat()).toBe(true);
    expect(h.conversations.systemTexts.get(oldId)?.[0]).toContain('Your preparer changed');
    expect(old.ensureCanWrite(client).isSuccess).toBe(false);

    const next = await h.conversations.findByUniquenessKey(tenantId, computeDirectUniquenessKey(successor, client));
    expect(next).not.toBeNull();
    expect(next!.isArchived).toBe(false);
    expect(next!.getParticipantSnapshots().find((p) => p.userId === successor)?.isPrimaryPreparer).toBe(true);
    expect(h.conversations.systemTexts.get(next!.id)?.[0]).toContain('Sam is now your preparer');
    expect(h.conversationAudit.entries.map((e) => e.action).sort()).toEqual(['ClosedByOffboarding', 'PrimaryPreparerAssigned']);
    expect(h.publisher.types()).toContain('communication.chat.conversation_started.v1');
  });

  it('reuses an existing chat between the client and the successor and marks it primary', async () => {
    const h = harness();
    await directChat(h, leaver, client, 'CustomerPortal', true);
    const existing = await directChat(h, successor, client, 'CustomerPortal', false);

    await handOverDirectChats(ctx(), h.deps);

    expect(h.conversations.get(existing).getParticipantSnapshots().find((p) => p.userId === successor)?.isPrimaryPreparer).toBe(true);
    expect([...h.conversations.rows.values()].filter((r) => r.kind === 'Direct')).toHaveLength(2);
  });

  it('without a successor the client continues with the office admin (not marked primary)', async () => {
    const h = harness();
    await directChat(h, leaver, client, 'CustomerPortal', true);

    await handOverDirectChats(ctx({ successor: null }), h.deps);

    const withAdmin = await h.conversations.findByUniquenessKey(tenantId, computeDirectUniquenessKey(admin, client));
    expect(withAdmin).not.toBeNull();
    expect(withAdmin!.getParticipantSnapshots().every((p) => !p.isPrimaryPreparer)).toBe(true);
    expect(h.conversations.systemTexts.get(withAdmin!.id)?.[0]).toContain('keep messaging the office');
  });

  it('a client chat where the leaver was not the primary is only closed', async () => {
    const h = harness();
    await directChat(h, leaver, client2, 'CustomerPortal', false);

    const report = await handOverDirectChats(ctx(), h.deps);

    expect(report).toEqual({ clientChatsClosed: 1, staffChatsLeft: 0, clientChatsOpened: 0 });
  });

  it('does not open a new chat when the office has chat disabled', async () => {
    const h = harness({ chatEnabled: false });
    await directChat(h, leaver, client, 'CustomerPortal', true);

    const report = await handOverDirectChats(ctx(), h.deps);

    expect(report.clientChatsOpened).toBe(0);
  });

  it('staff DM: the leaver is removed with a system message and the chat stays open', async () => {
    const h = harness();
    const dm = await directChat(h, leaver, colleague, 'TenantEmployee', false);

    const report = await handOverDirectChats(ctx(), h.deps);

    expect(report.staffChatsLeft).toBe(1);
    const saved = h.conversations.get(dm);
    expect(saved.isArchived).toBe(false);
    expect(saved.isParticipant(leaver)).toBe(false);
    expect(h.conversations.systemTexts.get(dm)).toEqual(['Lena is no longer with the office']);
  });

  it('staff DM: queda en solo lectura para el colega (sigue en su lista) y se avisa en vivo con quien salió', async () => {
    const h = harness();
    const live: Array<{ event: string; payload: unknown }> = [];
    const deps = {
      ...h.deps,
      emitter: { ...h.deps.emitter, emitToConversation: (e: { event: string; envelope: { payload: unknown } }) => live.push({ event: e.event, payload: e.envelope.payload }) },
    };
    const dm = await directChat(h, leaver, colleague, 'TenantEmployee', false);

    await handOverDirectChats(ctx(), deps);

    const saved = h.conversations.get(dm);
    expect(saved.closedReason).toBe('ParticipantLeft');
    expect(saved.departedParticipant()?.userId).toBe(leaver);
    const send = saved.sendText({ senderId: colleague, body: 'Still there?' });
    expect(send.isSuccess ? null : send.error.code).toBe('Chat.Conversation.ParticipantLeft');
    expect((await h.conversations.listForUser({ userId: colleague, take: 10, skip: 0 })).map((c) => c.id)).toEqual([dm]);

    const system = live.find((e) => e.event === 'chat.message.new')?.payload as { systemKind?: string };
    expect(system.systemKind).toBe('Membership');
    expect(live.find((e) => e.event === 'chat.conversation.updated')?.payload).toMatchObject({
      conversationId: dm,
      isClosedForWriting: true,
      closedReason: 'ParticipantLeft',
      departedParticipant: { userId: leaver, actorType: 'TenantEmployee' },
    });
  });

  it('is idempotent: a second run changes nothing', async () => {
    const h = harness();
    await directChat(h, leaver, client, 'CustomerPortal', true);
    await handOverDirectChats(ctx(), h.deps);
    const messagesBefore = [...h.conversations.systemTexts.values()].flat().length;

    const report = await handOverDirectChats(ctx(), h.deps);

    expect(report).toEqual({ clientChatsClosed: 0, staffChatsLeft: 0, clientChatsOpened: 0 });
    expect([...h.conversations.systemTexts.values()].flat()).toHaveLength(messagesBefore);
  });
});

describe('D-4 — la oficina lee el chat cerrado (procedencia)', () => {
  it('staff that sees all clients reads it; other staff do not; the client still reads it', async () => {
    const h = harness();
    const oldId = await directChat(h, leaver, client, 'CustomerPortal', true);
    await handOverDirectChats(ctx(), h.deps);
    const readDeps = { conversations: h.conversations, messages: { receiptsForOwnMessages: async () => new Map() } as never, userPermissions: h.userPermissions };

    const byAdmin = await getMessages({ tenantId, conversationId: oldId, requesterUserId: admin, requesterActorType: 'TenantAdmin', take: 20 }, readDeps);
    const byOutsider = await getMessages({ tenantId, conversationId: oldId, requesterUserId: outsider, requesterActorType: 'TenantEmployee', take: 20 }, readDeps);
    const byClient = await getMessages({ tenantId, conversationId: oldId, requesterUserId: client, requesterActorType: 'CustomerPortal', take: 20 }, readDeps);

    expect(byAdmin.isSuccess).toBe(true);
    expect(byOutsider.isSuccess).toBe(false);
    expect(byClient.isSuccess).toBe(true);
  });

  it('the assigned staff (e.g. the successor) reads it too', async () => {
    const h = harness();
    const oldId = await directChat(h, leaver, client, 'CustomerPortal', true);
    await handOverDirectChats(ctx(), h.deps);

    const result = await getMessages(
      { tenantId, conversationId: oldId, requesterUserId: successor, requesterActorType: 'TenantEmployee', take: 20 },
      {
        conversations: h.conversations,
        messages: { receiptsForOwnMessages: async () => new Map() } as never,
        userPermissions: h.userPermissions,
        customerPortalAccounts: h.customerPortalAccounts,
        customerAssignments: { isAssigned: async (_t: string, customerId: string, userId: string) => customerId === 'cust-1' && userId === successor },
      },
    );

    expect(result.isSuccess).toBe(true);
  });

  it('lists the closed chats of a client for its profile', async () => {
    const h = harness();
    const oldId = await directChat(h, leaver, client, 'CustomerPortal', true);
    await handOverDirectChats(ctx(), h.deps);

    const items = await listClosedClientChats(
      { tenantId, customerId: 'cust-1' },
      { conversations: h.conversations, customerPortalAccounts: { findActiveByCustomerId: async (id: string) => [...portals.values()].find((p) => p.customerId === id) ?? null } },
    );

    expect(items).toHaveLength(1);
    expect(items[0]).toMatchObject({ conversationId: oldId, formerStaffUserId: leaver, formerStaffDisplayName: 'Lena' });
  });
});

describe('D-5 — tickets de soporte', () => {
  async function ticketOpenedBy(h: ReturnType<typeof harness>, tickets: MemoryTickets, openerId: string): Promise<SupportTicket> {
    const ticketId = u();
    const chat = Conversation.startSupport({
      tenantId,
      ticketId,
      agent: { userId: platformTenant, displayName: 'Support Team', actorType: 'PlatformAdmin' },
      customer: { userId: openerId, displayName: directory.get(openerId)!.displayName, actorType: 'TenantEmployee' },
    });
    if (!chat.isSuccess) throw new Error('chat failed');
    await h.conversations.save(chat.value);
    const opened = SupportTicket.open({ tenantId, agentTenantId: platformTenant, openedByUserId: openerId, conversationId: chat.value.id, subject: 'Billing issue' });
    if (!opened.isSuccess) throw new Error('open failed');
    await tickets.save(opened.value);
    return opened.value;
  }

  it('open tickets of the leaver move to the successor (owner + chat participant); closed ones stay', async () => {
    const h = harness();
    const tickets = new MemoryTickets();
    const open = await ticketOpenedBy(h, tickets, leaver);
    const done = await ticketOpenedBy(h, tickets, leaver);
    const closing = (await tickets.findById(done.id))!;
    closing.close({ byUserId: leaver, isPlatformAdmin: false });
    await tickets.save(closing);

    const report = await handOverSupportTickets(ctx(), { ...h.deps, supportTickets: tickets });

    expect(report).toEqual({ transferred: 1, released: 0 });
    expect((await tickets.findById(open.id))!.openedByUserId).toBe(successor);
    expect((await tickets.findById(done.id))!.openedByUserId).toBe(leaver);
    const chat = h.conversations.get(open.conversationId);
    expect(chat.isParticipant(successor)).toBe(true);
    expect(chat.isParticipant(leaver)).toBe(false);
    expect(h.conversationAudit.entries.at(-1)?.action).toBe('SupportTicketTransferred');
  });

  it('without a successor the tickets go to the office admin', async () => {
    const h = harness();
    const tickets = new MemoryTickets();
    const open = await ticketOpenedBy(h, tickets, leaver);

    await handOverSupportTickets(ctx({ successor: null }), { ...h.deps, supportTickets: tickets });

    expect((await tickets.findById(open.id))!.openedByUserId).toBe(admin);
  });

  it('tickets claimed by a platform agent who leaves go back to the queue', async () => {
    const h = harness();
    const tickets = new MemoryTickets();
    const agent = u();
    const claimedTicket = await ticketOpenedBy(h, tickets, colleague);
    const loaded = (await tickets.findById(claimedTicket.id))!;
    loaded.claim({ agentUserId: agent });
    await tickets.save(loaded);

    const report = await handOverSupportTickets(
      { ...ctx(), tenantId: platformTenant, leaverUserId: agent, successor: null, offboardedByUserId: null },
      { ...h.deps, supportTickets: tickets },
    );

    expect(report.released).toBe(1);
    const released = (await tickets.findById(claimedTicket.id))!.toSnapshot();
    expect(released.status).toBe('Open');
    expect(released.assignedAgentId).toBeNull();
    expect(h.conversationAudit.entries.at(-1)?.action).toBe('SupportTicketReleased');
  });
});

describe('grupos (D-7: solo el retiro)', () => {
  it('removeUserFromGroups also walks archived groups', async () => {
    const h = harness();
    const group = Conversation.startGroup({
      tenantId,
      groupId: u(),
      title: 'Old team',
      creator: { userId: colleague, displayName: 'Cleo', actorType: 'TenantEmployee' },
      members: [{ userId: leaver, displayName: 'Lena', actorType: 'TenantEmployee' }],
    });
    if (!group.isSuccess) throw new Error('group failed');
    group.value.archive();
    await h.conversations.save(group.value);

    const removed = await removeUserFromGroups({ tenantId, userId: leaver, reason: 'Offboarded' }, h.deps);

    expect(removed).toBe(1);
    expect(h.conversations.get(group.value.id).isParticipant(leaver)).toBe(false);
  });
});

describe('admin de la oficina', () => {
  it('prefers who offboarded when it is an active TenantAdmin that sees all clients', async () => {
    const h = harness();
    expect((await resolveOfficeAdmin(h.deps, { tenantId, preferredUserId: admin, excludeUserId: leaver }))?.userId).toBe(admin);
  });

  it('returns null when no TenantAdmin sees all clients', async () => {
    const h = harness({ adminHasViewAll: false });
    expect(await resolveOfficeAdmin(h.deps, { tenantId, preferredUserId: admin, excludeUserId: leaver })).toBeNull();
  });
});

describe('grabaciones con captura en su navegador', () => {
  it('fails by system the client-capture recordings the leaver requested and are still capturing', async () => {
    const meetings = new InMemoryMeetingRepository();
    const scheduled = Meeting.schedule({ tenantId, title: 'Review', host: { userId: leaver, displayName: 'Lena' } });
    if (!scheduled.isSuccess) throw new Error('schedule failed');
    await meetings.save(scheduled.value);
    const recordingSessions = new InMemoryRecordingSessionRepository();
    const requested = RecordingSession.request({ tenantId, scope: RecordingScope.Meeting, scopeId: scheduled.value.id, requestedByUserId: leaver });
    if (!requested.isSuccess) throw new Error('request failed');
    const session = requested.value;
    session.start({ policyAllows: true, captureMode: RecordingCaptureMode.Client, maxDurationSeconds: null });
    await recordingSessions.save(session);
    const publisher = new RecordingPublisher();

    const failed = await failRequestedClientRecordings(ctx(), {
      meetings: meetings.asRepository() as MeetingRepository,
      calls: {} as CallRepository,
      recordingSessions,
      publisher,
      emitter: new RecordingEmitter(),
      effectiveSettings: new FakeEffectiveSettingsResolver(),
    });

    expect(failed).toBe(1);
    expect((await recordingSessions.findById(tenantId, session.id))?.toSnapshot().state).toBe('Failed');
    expect((await recordingSessions.findById(tenantId, session.id))?.toSnapshot().failureReason).toBe('RequesterOffboarded');
    expect(publisher.types()).toContain('communication.meeting.recording_failed.v1');
  });
});

describe('pre-flight — conteos aditivos', () => {
  it('counts client chats where it is primary, owned groups, open tickets and meetings of others', async () => {
    const h = harness();
    await directChat(h, leaver, client, 'CustomerPortal', true);
    await directChat(h, leaver, client2, 'CustomerPortal', false);
    const group = Conversation.startGroup({
      tenantId,
      groupId: u(),
      title: 'Team',
      creator: { userId: leaver, displayName: 'Lena', actorType: 'TenantEmployee' },
      members: [{ userId: colleague, displayName: 'Cleo', actorType: 'TenantEmployee' }],
    });
    if (!group.isSuccess) throw new Error('group failed');
    await h.conversations.save(group.value);
    const tickets = new MemoryTickets();
    const ticket = SupportTicket.open({ tenantId, agentTenantId: platformTenant, openedByUserId: leaver, conversationId: u(), subject: 'Help' });
    if (!ticket.isSuccess) throw new Error('ticket failed');
    await tickets.save(ticket.value);

    const impact = await getOffboardingImpact(
      { tenantId, userId: leaver },
      {
        meetings: { countActiveHostedBy: async () => 2, countScheduledWithParticipant: async () => 3 },
        conversations: h.conversations,
        supportTickets: tickets,
      },
    );

    expect(impact).toEqual({ activeMeetings: 2, clientChats: 1, groupsOwned: 1, openSupportTickets: 1, scheduledMeetingsAsParticipant: 3 });
  });
});

describe('dominio — métodos por sistema', () => {
  it('Meeting.removeParticipantBySystem only on scheduled meetings, never the host, idempotent', () => {
    const host = u();
    const scheduled = Meeting.schedule({ tenantId, title: 'Plan', host: { userId: host, displayName: 'Host' } });
    if (!scheduled.isSuccess) throw new Error('schedule failed');
    const meeting = scheduled.value;
    meeting.reassignHostBySystem({ newHostUserId: successor, newHostDisplayName: 'Sam' });

    expect(meeting.removeParticipantBySystem({ userId: successor }).isSuccess).toBe(false);
    const first = meeting.removeParticipantBySystem({ userId: host });
    const second = meeting.removeParticipantBySystem({ userId: host });

    expect(first.isSuccess && first.value.changed).toBe(true);
    expect(second.isSuccess && second.value.changed).toBe(false);
    expect(meeting.notifiableUserIds()).toEqual([successor]);
  });

  it('MeetingInvitation.revokeBySystem only revokes open invitations', () => {
    const issued = MeetingInvitation.issue({ meetingId: u(), tenantId, inviteeKind: 'Employee', inviteeUserId: leaver, ttlSeconds: 60, now: new Date() });
    if (!issued.isSuccess) throw new Error('issue failed');
    expect(issued.value.invitation.revokeBySystem(new Date())).toBe(true);
    expect(issued.value.invitation.revokeBySystem(new Date())).toBe(false);
  });

  it('SupportTicket by-system transitions refuse terminal tickets', () => {
    const opened = SupportTicket.open({ tenantId, agentTenantId: platformTenant, openedByUserId: leaver, conversationId: u(), subject: 'X' });
    if (!opened.isSuccess) throw new Error('open failed');
    const ticket = opened.value;
    expect(ticket.releaseAgentBySystem().isSuccess).toBe(false);
    ticket.close({ byUserId: leaver, isPlatformAdmin: false });
    expect(ticket.transferOwnershipBySystem({ newOpenerUserId: successor }).isSuccess).toBe(false);
  });
});
