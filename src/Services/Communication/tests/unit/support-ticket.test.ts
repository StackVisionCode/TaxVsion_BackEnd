import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { SupportTicket } from '../../src/domain/support/support-ticket.js';

function u(): string {
  return randomUUID();
}

function opened(overrides: { platform?: string; opener?: string; customer?: string } = {}) {
  const customerTenant = overrides.customer ?? u();
  const platform = overrides.platform ?? u();
  const opener = overrides.opener ?? u();
  const r = SupportTicket.open({
    tenantId: customerTenant,
    agentTenantId: platform,
    openedByUserId: opener,
    conversationId: u(),
    subject: 'Cannot access reports',
    category: 'Technical',
  });
  if (!r.isSuccess) throw new Error();
  return { ticket: r.value, customerTenant, platform, opener };
}

describe('SupportTicket.open', () => {
  it('creates an Open ticket cross-tenant', () => {
    const { ticket, customerTenant, platform, opener } = opened();
    const snap = ticket.toSnapshot();
    expect(snap.status).toBe('Open');
    expect(snap.tenantId).toBe(customerTenant);
    expect(snap.agentTenantId).toBe(platform);
    expect(snap.openedByUserId).toBe(opener);
    expect(snap.assignedAgentId).toBeNull();
  });

  it('rejects same tenant on both sides', () => {
    const tid = u();
    const r = SupportTicket.open({
      tenantId: tid,
      agentTenantId: tid,
      openedByUserId: u(),
      conversationId: u(),
      subject: 'x',
    });
    expect(r.isSuccess).toBe(false);
    if (r.isSuccess) return;
    expect(r.error.code).toBe('Support.SameTenant');
  });

  it('rejects empty subject', () => {
    const r = SupportTicket.open({
      tenantId: u(),
      agentTenantId: u(),
      openedByUserId: u(),
      conversationId: u(),
      subject: '   ',
    });
    expect(r.isSuccess).toBe(false);
    if (r.isSuccess) return;
    expect(r.error.code).toBe('Support.MissingSubject');
  });
});

describe('SupportTicket.claim', () => {
  it('assigns agent and moves to Claimed', () => {
    const { ticket } = opened();
    const agent = u();
    const r = ticket.claim({ agentUserId: agent });
    expect(r.isSuccess).toBe(true);
    expect(ticket.status).toBe('Claimed');
    expect(ticket.assignedAgentId).toBe(agent);
  });

  it('cannot claim twice', () => {
    const { ticket } = opened();
    ticket.claim({ agentUserId: u() });
    const r = ticket.claim({ agentUserId: u() });
    expect(r.isSuccess).toBe(false);
  });
});

describe('SupportTicket.canBeAccessedBy', () => {
  it('opener always can access', () => {
    const { ticket, customerTenant, opener } = opened();
    expect(
      ticket.canBeAccessedBy({
        actorUserId: opener,
        actorTenantId: customerTenant,
        actorHasAgentPermission: false,
        isPlatformAdmin: false,
      }),
    ).toBe(true);
  });

  it('assigned agent from platform tenant can access', () => {
    const { ticket, platform } = opened();
    const agent = u();
    ticket.claim({ agentUserId: agent });
    expect(
      ticket.canBeAccessedBy({
        actorUserId: agent,
        actorTenantId: platform,
        actorHasAgentPermission: true,
        isPlatformAdmin: false,
      }),
    ).toBe(true);
  });

  it('unassigned agent from platform can access Open (to claim)', () => {
    const { ticket, platform } = opened();
    expect(
      ticket.canBeAccessedBy({
        actorUserId: u(),
        actorTenantId: platform,
        actorHasAgentPermission: true,
        isPlatformAdmin: false,
      }),
    ).toBe(true);
  });

  it('random user is denied', () => {
    const { ticket } = opened();
    expect(
      ticket.canBeAccessedBy({
        actorUserId: u(),
        actorTenantId: u(),
        actorHasAgentPermission: false,
        isPlatformAdmin: false,
      }),
    ).toBe(false);
  });

  it('PlatformAdmin bypasses everything', () => {
    const { ticket } = opened();
    expect(
      ticket.canBeAccessedBy({
        actorUserId: u(),
        actorTenantId: u(),
        actorHasAgentPermission: false,
        isPlatformAdmin: true,
      }),
    ).toBe(true);
  });
});

describe('SupportTicket.resolve / close', () => {
  it('agent can resolve if assigned', () => {
    const { ticket } = opened();
    const agent = u();
    ticket.claim({ agentUserId: agent });
    const r = ticket.resolve({ byUserId: agent, isAgent: true });
    expect(r.isSuccess).toBe(true);
    expect(ticket.status).toBe('Resolved');
  });

  it('opener can resolve as customer', () => {
    const { ticket, opener } = opened();
    ticket.claim({ agentUserId: u() });
    const r = ticket.resolve({ byUserId: opener, isAgent: false });
    expect(r.isSuccess).toBe(true);
  });

  it('agent cannot resolve if not assigned', () => {
    const { ticket } = opened();
    ticket.claim({ agentUserId: u() });
    const r = ticket.resolve({ byUserId: u(), isAgent: true });
    expect(r.isSuccess).toBe(false);
  });

  it('PlatformAdmin can close even without opener/agent role', () => {
    const { ticket } = opened();
    const r = ticket.close({ byUserId: u(), isPlatformAdmin: true });
    expect(r.isSuccess).toBe(true);
    expect(ticket.status).toBe('Closed');
  });

  it('random user cannot close', () => {
    const { ticket } = opened();
    const r = ticket.close({ byUserId: u(), isPlatformAdmin: false });
    expect(r.isSuccess).toBe(false);
  });
});
