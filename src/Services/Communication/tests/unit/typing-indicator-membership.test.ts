import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  isMemberOfConversation,
  resolveJoinedConversationTenant,
} from '../../src/api/socket/handlers/chat-handlers.js';

describe('conversation membership for live indicators', () => {
  const tenant = 'tenant-1';
  const mine = 'conv-mine';
  const others = 'conv-others';
  const rooms = new Set([`t:${tenant}:c:${mine}`, `t:${tenant}:u:someone`]);

  it('allows announcing in an owned conversation', () => {
    expect(isMemberOfConversation(rooms, tenant, mine)).toBe(true);
  });

  it('does not announce in another conversation', () => {
    expect(isMemberOfConversation(rooms, tenant, others)).toBe(false);
  });

  it('does not cross tenants in the strict membership check', () => {
    expect(isMemberOfConversation(rooms, 'tenant-2', mine)).toBe(false);
  });

  it('does not announce when the socket has no rooms', () => {
    expect(isMemberOfConversation(new Set(), tenant, mine)).toBe(false);
  });

  it('resolves a support cross-tenant room that was already authorized by join', () => {
    const platformTenant = 'platform-tenant';
    const customerTenant = 'customer-tenant';
    const supportRooms = new Set([`t:${customerTenant}:c:${mine}`, `t:${platformTenant}:u:agent`]);

    expect(resolveJoinedConversationTenant(supportRooms, platformTenant, mine)).toBe(customerTenant);
  });

  it('prefers the actor tenant when the joined conversation is local', () => {
    expect(resolveJoinedConversationTenant(rooms, tenant, mine)).toBe(tenant);
  });

  it('does not resolve conversations missing from the socket rooms', () => {
    expect(resolveJoinedConversationTenant(rooms, tenant, others)).toBeNull();
  });
});

describe('the four live indicators apply the membership guard', () => {
  const source = readFileSync(
    fileURLToPath(new URL('../../src/api/socket/handlers/chat-handlers.ts', import.meta.url)),
    'utf8',
  );

  const handlers = ['TypingStart', 'TypingStop', 'RecordingStart', 'RecordingStop'];

  it.each(handlers)('%s does not emit before resolving a joined room', (handler) => {
    const start = source.indexOf(`socket.on(ChatSocketEvents.${handler},`);
    expect(start).toBeGreaterThan(-1);
    const next = source.indexOf('socket.on(ChatSocketEvents.', start + 1);
    const body = source.slice(start, next === -1 ? source.length : next);
    expect(body).toContain('joinedConversationTenant(');
  });
});
