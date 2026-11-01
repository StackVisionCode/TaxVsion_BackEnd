import { randomUUID as u } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { Conversation, type ConversationSnapshot } from '../../src/domain/conversations/conversation.js';
import type { Message } from '../../src/domain/conversations/message.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { ConversationAuditEntry, ConversationAuditRepository } from '../../src/application/ports/conversation-audit-repository.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { UserDirectoryEntrySnapshot } from '../../src/application/ports/user-directory-repository.js';
import type { TenantCommunicationSettingsSnapshot } from '../../src/application/ports/tenant-settings-provider.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type { Notification } from '../../src/domain/notifications/notification.js';
import { admitGroupMembers } from '../../src/application/use-cases/group-membership-policy.js';
import { startGroupConversation } from '../../src/application/use-cases/start-group-conversation.js';
import { addGroupParticipant } from '../../src/application/use-cases/add-group-participant.js';
import { removeGroupParticipant } from '../../src/application/use-cases/remove-group-participant.js';
import {
  removeUserFromGroups,
  setGroupAdminsOnly,
  updateGroupInfo,
} from '../../src/application/use-cases/group-administration.js';
import { getMessages } from '../../src/application/use-cases/get-messages.js';
import { listConversationAudit } from '../../src/application/use-cases/list-conversation-audit.js';
import { deleteMessage } from '../../src/application/use-cases/delete-message.js';
import { ChatSocketEvents } from '../../src/contracts/socket/chat-socket-events.js';
import { createFakeProjectionRepository, fakeSnapshot } from '../helpers/fake-user-permissions-projection-repository.js';
import { FakeEffectiveSettingsResolver } from '../helpers/fake-effective-settings.js';

/** F8b: política de membresía (T-18), efectos de administración (System, auditoría, socket), T-19 y D-21. */
const tenantId = u();
const otherTenant = u();
const owner = u();
const admin = u();
const staff = u();
const client = u();
const client2 = u();
const outsider = u();
const guest = u();
const tenantAdmin = u();

function entry(userId: string, displayName: string, actorType: string, over: Partial<UserDirectoryEntrySnapshot> = {}): UserDirectoryEntrySnapshot {
  return { userId, tenantId, displayName, email: `${displayName}@x.test`, isActive: true, actorType, avatarFileId: null, updatedAtUtc: new Date(0), ...over };
}

const directory = new Map<string, UserDirectoryEntrySnapshot>([
  [owner, entry(owner, 'Ana', 'TenantEmployee')],
  [admin, entry(admin, 'Beto', 'TenantEmployee')],
  [staff, entry(staff, 'Caro', 'TenantEmployee')],
  [client, entry(client, 'Diego', 'CustomerPortal')],
  [client2, entry(client2, 'Eva', 'CustomerPortal')],
  [outsider, entry(outsider, 'Fede', 'TenantEmployee', { tenantId: otherTenant })],
  [guest, entry(guest, 'Gus', 'Guest')],
  [tenantAdmin, entry(tenantAdmin, 'Hilda', 'TenantAdmin')],
]);
const portal = new Map<string, string>([[client, 'cust-1'], [client2, 'cust-2']]);

function settings(over: Partial<TenantCommunicationSettingsSnapshot> = {}): TenantCommunicationSettingsSnapshot {
  return { tenantId, chatEnabled: true, employeeToEmployeeChatEnabled: true, restrictCustomerChatToAssignedPreparer: false, screenshotsEnabled: true, ...over };
}

class MemoryConversations implements ConversationRepository {
  readonly rows = new Map<string, ConversationSnapshot>();
  readonly systemTexts: string[] = [];
  lastListArgs: unknown = null;
  async save(c: Conversation): Promise<void> {
    for (const m of c.drainPendingMessages() as Message[]) if (m.toSnapshot().kind === 'System') this.systemTexts.push(m.toSnapshot().body ?? '');
    this.rows.set(c.id, c.toSnapshot());
  }
  async findById(_t: string, id: string): Promise<Conversation | null> {
    const s = this.rows.get(id);
    return s ? Conversation.rehydrate(s) : null;
  }
  async findByUniquenessKey(): Promise<Conversation | null> {
    return null;
  }
  async listForUser(input: { userId: string }) {
    return [...this.rows.values()].filter((s) => s.participants.some((p) => p.userId === input.userId && !p.isRemoved));
  }
  async countForUser() {
    return 0;
  }
  async countUnreadByKind() {
    return { Direct: 0, Group: 0, Support: 0, Meeting: 0 };
  }
  async listMessages(args: unknown) {
    this.lastListArgs = args;
    return [];
  }
  async countUnreadForUser() {
    return 0;
  }
}

class MemoryAudit implements ConversationAuditRepository {
  readonly entries: ConversationAuditEntry[] = [];
  async append(entries: readonly ConversationAuditEntry[]) {
    this.entries.push(...entries);
  }
  async list() {
    return [...this.entries].reverse().map((entry, index) => ({ ...entry, id: `audit-${index}` }));
  }
}

function recordingEmitter() {
  const events: { event: string; payload: unknown; to: string }[] = [];
  const left: string[] = [];
  const emitter = {
    emitToConversation: (i: { conversationId: string; event: string; envelope: { payload: unknown } }) => events.push({ event: i.event, payload: i.envelope.payload, to: i.conversationId }),
    emitToUser: (i: { userId: string; event: string; envelope: { payload: unknown } }) => events.push({ event: i.event, payload: i.envelope.payload, to: i.userId }),
    emitToCall: () => undefined,
    emitToMeeting: () => undefined,
    emitToTenant: () => undefined,
    emitToTenantStaff: () => undefined,
    removeUserFromConversation: (i: { userId: string }) => left.push(i.userId),
  } as unknown as RealtimeEmitter;
  return { emitter, events, left };
}

function harness(over: { settings?: Partial<TenantCommunicationSettingsSnapshot>; assigned?: boolean; restrict?: boolean } = {}) {
  const conversations = new MemoryConversations();
  const conversationAudit = new MemoryAudit();
  const { emitter, events, left } = recordingEmitter();
  const created: Notification[] = [];
  const notifications = {
    createIfMissing: async (n: Notification) => (created.push(n), true),
    countUnread: async () => created.length,
  } as unknown as NotificationRepository;
  const deps = {
    conversations,
    conversationAudit,
    emitter,
    notifications,
    idempotency: { tryReserve: async () => ({ status: 'fresh' as const, token: u() }), commit: async () => undefined, release: async () => undefined },
    publisher: { enqueue: async () => undefined },
    settings: { get: async () => settings(over.settings) },
    effectiveSettings: new FakeEffectiveSettingsResolver(),
    userDirectory: { findByUserId: async (id: string) => directory.get(id) ?? null },
    customerPortalAccounts: {
      findActiveByUserId: async (id: string) => (portal.has(id) ? { customerId: portal.get(id) ?? '', tenantId, userId: id, isActive: true } : null),
    },
    customerAssignments: { isAssigned: async () => over.assigned ?? false },
    userPermissions: createFakeProjectionRepository([fakeSnapshot({ userId: owner })]),
    assignmentVisibilityEnabled: over.restrict ?? false,
  };
  return { deps, conversations, conversationAudit, events, left, created };
}

function row(h: ReturnType<typeof harness>, id: string): ConversationSnapshot {
  const snapshot = h.conversations.rows.get(id);
  if (!snapshot) throw new Error('not saved');
  return snapshot;
}

const actor = (userId: string, displayName: string, actorType = 'TenantEmployee') => ({ userId, displayName, actorType });
const code = (r: { isSuccess: boolean; error?: { code: string } }) => (r.isSuccess ? 'ok' : (r.error?.code ?? 'unknown'));

async function groupWithAdmin(h: ReturnType<typeof harness>, members = [admin, staff, client]) {
  const created = await startGroupConversation(
    { tenantId, correlationId: 'c', clientKey: u(), creator: actor(owner, 'Ana'), title: 'Tax team', memberUserIds: members },
    h.deps,
  );
  if (!created.isSuccess) throw new Error(created.error.code);
  const conv = await h.conversations.findById(tenantId, created.value.conversationId);
  if (!conv) throw new Error('not saved');
  conv.setMemberRole({ actorUserId: owner, targetUserId: admin, role: 'Admin' });
  await h.conversations.save(conv);
  return created.value.conversationId;
}

describe('admitGroupMembers (T-18)', () => {
  const run = (candidates: string[], over: Parameters<typeof harness>[0] = {}, actorType = 'TenantEmployee', existingClientCount = 0) => {
    const h = harness(over);
    return admitGroupMembers(
      { tenantId, actor: { userId: owner, actorType }, candidates: candidates.map((userId) => ({ userId })), existingClientCount, settings: settings(over.settings) },
      h.deps,
    );
  };

  it('rechaza otra oficina, invitados y desconocidos', async () => {
    expect(code(await run([outsider]))).toBe('Chat.Member.NotInTenant');
    expect(code(await run([guest]))).toBe('Chat.Member.NotAllowed');
    expect(code(await run([u()]))).toBe('Directory.Unresolved');
  });

  it('cliente: toma su CustomerId; sin portal activo no entra', async () => {
    const ok = await run([client]);
    expect(ok.isSuccess && ok.value[0]?.customerId).toBe('cust-1');
    const noPortal = u();
    directory.set(noPortal, entry(noPortal, 'Ivan', 'CustomerPortal'));
    expect(code(await run([noPortal]))).toBe('Chat.Member.NoPortalAccount');
  });

  it('D-20: un solo cliente salvo que la oficina lo permita', async () => {
    expect(code(await run([client, client2]))).toBe('Chat.Member.SingleClientOnly');
    expect(code(await run([client2], {}, 'TenantEmployee', 1))).toBe('Chat.Member.SingleClientOnly');
    expect(code(await run([client, client2], { settings: { groupsAllowMultipleClients: true } }))).toBe('ok');
  });

  it('ABAC: con visibilidad por asignación solo agrega clientes asignados', async () => {
    expect(code(await run([client], { restrict: true, assigned: false }))).toBe('Chat.Member.NotAssigned');
    expect(code(await run([client], { restrict: true, assigned: true }))).toBe('ok');
  });

  it('grupo solo de empleados respeta el interruptor empleado↔empleado; el Tenant Admin no', async () => {
    const off = { settings: { employeeToEmployeeChatEnabled: false } };
    expect(code(await run([staff], off))).toBe('Chat.EmployeeToEmployeeDisabled');
    expect(code(await run([staff, client], off))).toBe('ok');
    expect(code(await run([staff], off, 'TenantAdmin'))).toBe('ok');
  });
});

describe('administración de grupos (efectos)', () => {
  it('crear: System "created the group", auditoría y CustomerId del cliente', async () => {
    const h = harness();
    const id = await groupWithAdmin(h);
    expect(h.conversations.systemTexts).toContain('Ana created the group');
    expect(h.conversationAudit.entries.map((e) => e.action)).toEqual(['GroupCreated']);
    expect(row(h, id).participants.find((p) => p.userId === client)?.customerId).toBe('cust-1');
  });

  it('agregar: un Member no puede; el Admin sí, con System, auditoría y aviso en la campana', async () => {
    const h = harness({ settings: { groupsAllowMultipleClients: true } });
    const id = await groupWithAdmin(h);
    const denied = await addGroupParticipant({ tenantId, correlationId: 'c', clientKey: u(), conversationId: id, actor: actor(staff, 'Caro'), newMemberUserId: client2 }, h.deps);
    expect(code(denied)).toBe('Chat.Group.AdminOnly');

    const ok = await addGroupParticipant({ tenantId, correlationId: 'c', clientKey: u(), conversationId: id, actor: actor(admin, 'Beto'), newMemberUserId: client2 }, h.deps);
    expect(code(ok)).toBe('ok');
    expect(h.conversations.systemTexts).toContain('Beto added Eva');
    expect(h.conversationAudit.entries.at(-1)).toMatchObject({ action: 'MemberAdded', actorUserId: admin, targetUserId: client2 });
    expect(h.created).toHaveLength(1);
    expect(h.events.some((e) => e.event === ChatSocketEvents.MessageNew)).toBe(true);
  });

  // E2E F8: la bitácora decía "A former member" porque el front solo conocía a los miembros actuales.
  it('bitácora: trae el nombre del actor y del afectado aunque ya no esté en el grupo', async () => {
    const h = harness();
    const id = await groupWithAdmin(h);
    await removeGroupParticipant({ tenantId, correlationId: 'c', clientKey: u(), conversationId: id, actor: actor(owner, 'Ana'), targetUserId: staff }, h.deps);

    const audit = await listConversationAudit({ tenantId, conversationId: id, requesterUserId: owner, canAdministerAnyGroup: false, take: 50 }, h.deps);

    expect(audit.isSuccess && audit.value.items.find((i) => i.action === 'MemberRemoved')).toMatchObject({ actorDisplayName: 'Ana', targetDisplayName: 'Caro' });
  });

  it('sale el Owner: hereda el Admin, en vivo y en la bitácora', async () => {
    const h = harness();
    const id = await groupWithAdmin(h);
    const r = await removeGroupParticipant({ tenantId, correlationId: 'c', clientKey: u(), conversationId: id, actor: actor(owner, 'Ana'), targetUserId: owner }, h.deps);
    expect(r.isSuccess && r.value).toMatchObject({ reason: 'Left', newOwnerUserId: admin, archived: false });
    expect(h.conversations.systemTexts.at(-1)).toBe('Ana left. Beto is now the group owner');
    expect(h.events.find((e) => e.event === ChatSocketEvents.ConversationParticipantRoleChanged)?.payload).toMatchObject({ userId: admin, role: 'Owner', changedByUserId: null });
    expect(h.conversationAudit.entries.slice(-2).map((e) => e.action)).toEqual(['MemberLeft', 'OwnershipTransferred']);
  });

  it('modo anuncio y datos del grupo avisan con chat.conversation.updated; imagen ajena se rechaza', async () => {
    const h = harness();
    const id = await groupWithAdmin(h);
    expect(code(await setGroupAdminsOnly({ tenantId, conversationId: id, actor: actor(admin, 'Beto'), enabled: true }, h.deps))).toBe('ok');
    expect(h.events.find((e) => e.event === ChatSocketEvents.ConversationUpdated)?.payload).toMatchObject({ adminsOnlyWrite: true });

    const missing = await updateGroupInfo(
      { tenantId, conversationId: id, actor: actor(admin, 'Beto'), avatarFileId: u() },
      { ...h.deps, cloudStorageMetadata: { getMetadata: async () => null } },
    );
    expect(code(missing)).toBe('Chat.Group.AvatarNotFound');
  });

  it('superpoder: el Tenant Admin renombra sin ser miembro; un empleado ajeno no', async () => {
    const h = harness();
    const id = await groupWithAdmin(h);
    expect(code(await updateGroupInfo({ tenantId, conversationId: id, actor: actor(outsider, 'Fede'), title: 'X' }, h.deps))).toBe('Chat.Conversation.NotParticipant');
    const r = await updateGroupInfo({ tenantId, conversationId: id, actor: actor(tenantAdmin, 'Hilda', 'TenantAdmin'), title: 'Renamed' }, h.deps);
    expect(r.isSuccess && r.value.title).toBe('Renamed');
    expect(h.conversations.systemTexts.at(-1)).toBe('Hilda renamed the group to "Renamed"');
  });

  it('T-19: la baja saca de los grupos, hereda el siguiente y archiva si no queda staff', async () => {
    const h = harness();
    const id = await groupWithAdmin(h, [admin, client]);
    expect(await removeUserFromGroups({ tenantId, userId: owner, reason: 'Offboarded' }, h.deps)).toBe(1);
    expect(h.left).toEqual([owner]);
    expect(row(h, id).participants.find((p) => p.userId === admin)?.role).toBe('Owner');

    await removeUserFromGroups({ tenantId, userId: admin, reason: 'Deactivated' }, h.deps);
    expect(row(h, id).isArchived).toBe(true);
    expect(h.events.filter((e) => e.event === ChatSocketEvents.ConversationUpdated).at(-1)?.payload).toMatchObject({ isArchived: true });
    expect(h.conversationAudit.entries.at(-1)).toMatchObject({ action: 'Archived', actorUserId: null });
  });

  it('D-21: el cliente lee desde su ingreso; el staff sin piso', async () => {
    const h = harness();
    const id = await groupWithAdmin(h);
    const joined = row(h, id).participants.find((p) => p.userId === client)?.joinedAtUtc;
    await getMessages({ tenantId, conversationId: id, requesterUserId: client, take: 20 }, { conversations: h.conversations, messages: { receiptsForOwnMessages: async () => new Map() } as never });
    expect(h.conversations.lastListArgs).toMatchObject({ notBeforeUtc: joined });
    await getMessages({ tenantId, conversationId: id, requesterUserId: staff, take: 20 }, { conversations: h.conversations, messages: { receiptsForOwnMessages: async () => new Map() } as never });
    expect(h.conversations.lastListArgs).toMatchObject({ notBeforeUtc: null });
  });

  it('el Admin del grupo borra mensajes ajenos sin el permiso global de moderación', async () => {
    const h = harness();
    const id = await groupWithAdmin(h);
    const conv = await h.conversations.findById(tenantId, id);
    if (!conv) throw new Error('not saved');
    const sent = conv.sendText({ senderId: client, body: 'spam' });
    if (!sent.isSuccess) throw new Error(sent.error.code);
    const message = sent.value;
    let updated = 0;
    const messages = { findById: async () => message, update: async () => void updated++ } as never;
    const base = { tenantId, correlationId: 'c', messageId: message.id, actorCanModerate: false };
    expect(code(await deleteMessage({ ...base, clientKey: u(), actorUserId: staff }, { ...h.deps, messages }))).not.toBe('ok');
    expect(code(await deleteMessage({ ...base, clientKey: u(), actorUserId: admin }, { ...h.deps, messages }))).toBe('ok');
    expect(updated).toBe(1);
  });
});
