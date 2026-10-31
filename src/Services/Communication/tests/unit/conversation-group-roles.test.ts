import { randomUUID as u } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { Conversation } from '../../src/domain/conversations/conversation.js';

/** F8b (D-22, D-23, T-21): roles, sucesión, modo anuncio y datos del grupo. */
describe('Conversation — reglas de grupo', () => {
  const tenantId = u();
  const owner = u();
  const admin = u();
  const staff = u();
  const client = u();
  const t0 = new Date('2026-10-31T10:00:00Z');
  const later = (min: number) => new Date(t0.getTime() + min * 60_000);

  function group(): Conversation {
    const r = Conversation.startGroup({
      tenantId,
      groupId: u(),
      title: 'Tax team',
      creator: { userId: owner, displayName: 'Owner', actorType: 'TenantEmployee' },
      members: [
        { userId: admin, displayName: 'Admin', actorType: 'TenantEmployee' },
        { userId: client, displayName: 'Client', actorType: 'CustomerPortal', customerId: 'cust-1' },
      ],
      now: t0,
    });
    if (!r.isSuccess) throw new Error(r.error.code);
    const g = r.value;
    g.addParticipant({ actorUserId: owner, newMember: { userId: staff, displayName: 'Staff', actorType: 'TenantEmployee' }, now: later(1) });
    expect(g.setMemberRole({ actorUserId: owner, targetUserId: admin, role: 'Admin' }).isSuccess).toBe(true);
    return g;
  }

  const code = (r: { isSuccess: boolean; error?: { code: string } }) => (r.isSuccess ? 'ok' : (r.error?.code ?? 'unknown'));

  it('un Member no administra; un Admin sí agrega y quita miembros', () => {
    const g = group();
    expect(code(g.addParticipant({ actorUserId: staff, newMember: { userId: u(), displayName: 'X', actorType: 'TenantEmployee' } }))).toBe('Chat.Group.AdminOnly');
    expect(code(g.removeParticipant({ actorUserId: admin, targetUserId: client }))).toBe('ok');
  });

  it('un Admin no saca al Owner ni a otro Admin; el Tenant Admin sí administra sin ser miembro', () => {
    const g = group();
    expect(code(g.removeParticipant({ actorUserId: admin, targetUserId: owner }))).toBe('Chat.Group.OwnerOnly');
    const tenantAdmin = u();
    expect(code(g.removeParticipant({ actorUserId: tenantAdmin, targetUserId: admin, actorIsTenantAdmin: true }))).toBe('ok');
  });

  it('solo el Owner nombra Admins y un cliente nunca administra', () => {
    const g = group();
    expect(code(g.setMemberRole({ actorUserId: admin, targetUserId: staff, role: 'Admin' }))).toBe('Chat.Group.OwnerOnly');
    expect(code(g.setMemberRole({ actorUserId: owner, targetUserId: client, role: 'Admin' }))).toBe('Chat.Member.ClientCannotBeAdmin');
    expect(code(g.transferOwnership({ actorUserId: owner, targetUserId: client }))).toBe('Chat.Member.ClientCannotBeAdmin');
  });

  it('transferir la propiedad deja al Owner anterior como Admin', () => {
    const g = group();
    const r = g.transferOwnership({ actorUserId: owner, targetUserId: staff });
    expect(r.isSuccess && r.value.previousOwnerUserId).toBe(owner);
    expect(g.roleOf(staff)).toBe('Owner');
    expect(g.roleOf(owner)).toBe('Admin');
  });

  it('sale el Owner → hereda el Admin más antiguo; sin Admins → el staff más antiguo', () => {
    const g = group();
    const r = g.removeParticipant({ actorUserId: owner, targetUserId: owner });
    expect(r.isSuccess && r.value).toEqual({ reason: 'Left', newOwnerUserId: admin, archived: false });

    const g2 = group();
    g2.setMemberRole({ actorUserId: owner, targetUserId: admin, role: 'Member' });
    const r2 = g2.removeParticipant({ actorUserId: owner, targetUserId: owner });
    expect(r2.isSuccess && r2.value.newOwnerUserId).toBe(admin); // admin entró antes que staff
  });

  it('sin staff el grupo se archiva (solo lectura)', () => {
    const g = group();
    g.removeForDeactivation(staff);
    g.removeForDeactivation(admin);
    const last = g.removeForDeactivation(owner);
    expect(last).toEqual({ newOwnerUserId: null, archived: true });
    expect(g.isArchived).toBe(true);
    expect(code(g.sendText({ senderId: client, body: 'hi' }))).toBe('Chat.Conversation.Archived');
  });

  it('modo anuncio: el Member no escribe, el Admin sí', () => {
    const g = group();
    expect(code(g.setAdminsOnlyWrite({ actorUserId: staff, enabled: true }))).toBe('Chat.Group.AdminOnly');
    expect(g.setAdminsOnlyWrite({ actorUserId: admin, enabled: true }).isSuccess).toBe(true);
    expect(code(g.sendText({ senderId: client, body: 'hi' }))).toBe('Chat.Conversation.AdminsOnly');
    expect(code(g.ensureCanWrite(staff))).toBe('Chat.Conversation.AdminsOnly');
    expect(code(g.sendText({ senderId: admin, body: 'notice' }))).toBe('ok');
  });

  it('datos del grupo: nombre y descripción con tope; vacío no vale', () => {
    const g = group();
    expect(code(g.updateGroupInfo({ actorUserId: admin, title: '  ' }))).toBe('Chat.Conversation.MissingTitle');
    expect(code(g.updateGroupInfo({ actorUserId: admin, description: 'x'.repeat(501) }))).toBe('Chat.Group.DescriptionTooLong');
    const r = g.updateGroupInfo({ actorUserId: admin, title: 'Renamed', description: 'Quarterly', avatarFileId: 'f1' });
    expect(r.isSuccess && r.value.titleChanged).toBe(true);
    expect([g.description, g.avatarFileId]).toEqual(['Quarterly', 'f1']);
  });

  it('reingreso reactiva la fila; el cliente ve desde su nuevo ingreso (D-21) y el staff todo', () => {
    const g = group();
    g.removeParticipant({ actorUserId: owner, targetUserId: client, now: later(5) });
    g.addParticipant({ actorUserId: owner, newMember: { userId: client, displayName: 'Client', actorType: 'CustomerPortal' }, now: later(10) });
    expect(g.getParticipantSnapshots().filter((p) => p.userId === client)).toHaveLength(1);
    expect(g.historyVisibleFrom(client)).toEqual(later(10));
    expect(g.historyVisibleFrom(staff)).toBeNull();
    expect(g.getParticipantSnapshots().find((p) => p.userId === client)?.customerId).toBe('cust-1');
  });

  it('silencio propio', () => {
    const g = group();
    expect(g.setMuted(staff, true).isSuccess).toBe(true);
    expect(g.getParticipantSnapshots().find((p) => p.userId === staff)?.isMuted).toBe(true);
    expect(code(g.setMuted(u(), true))).toBe('Chat.Conversation.NotParticipant');
  });
});
