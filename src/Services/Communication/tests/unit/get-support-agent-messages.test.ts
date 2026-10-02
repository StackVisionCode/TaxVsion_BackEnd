import { randomUUID } from 'node:crypto';
import { describe, expect, it, vi } from 'vitest';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { MessageRepository } from '../../src/application/ports/message-repository.js';
import type { SupportTicketRepository } from '../../src/application/ports/support-ticket-repository.js';
import type { UserDirectoryRepository } from '../../src/application/ports/user-directory-repository.js';
import { getSupportAgentMessages } from '../../src/application/use-cases/get-support-agent-messages.js';
import { MessageKind } from '../../src/domain/conversations/message-kind.js';
import type { MessageSnapshot } from '../../src/domain/conversations/message.js';
import { SupportTicket, type SupportTicketSnapshot } from '../../src/domain/support/support-ticket.js';

function id(): string {
  return randomUUID();
}

function supportTicket(overrides: Partial<SupportTicketSnapshot> = {}): SupportTicket {
  const now = new Date('2026-10-02T00:00:00.000Z');
  return SupportTicket.rehydrate({
    id: id(),
    tenantId: id(),
    agentTenantId: id(),
    openedByUserId: id(),
    assignedAgentId: id(),
    conversationId: id(),
    subject: 'Unable to upload W-2 document',
    category: 'Technical',
    priority: 'High',
    status: 'Claimed',
    openedAtUtc: now,
    claimedAtUtc: now,
    resolvedAtUtc: null,
    closedAtUtc: null,
    updatedAtUtc: now,
    ...overrides,
  });
}

function message(overrides: Partial<MessageSnapshot> = {}): MessageSnapshot {
  const now = new Date('2026-10-02T00:01:00.000Z');
  return {
    id: id(),
    conversationId: id(),
    tenantId: id(),
    senderId: id(),
    senderDisplayName: id(),
    kind: MessageKind.Text,
    body: 'Ayudame por favor tengo problemas.',
    attachmentFileId: null,
    replyToMessageId: null,
    forwardedFromMessageId: null,
    isEdited: false,
    isDeleted: false,
    isPinned: false,
    pinnedAtUtc: null,
    pinnedByUserId: null,
    deletedAtUtc: null,
    createdAtUtc: now,
    editedAtUtc: null,
    audioDurationMs: null,
    audioWaveform: null,
    ...overrides,
  };
}

describe('getSupportAgentMessages', () => {
  it('hydrates customer message names from UserDirectory and normalizes the support placeholder', async () => {
    const tenantId = id();
    const agentTenantId = id();
    const openedByUserId = id();
    const assignedAgentId = id();
    const conversationId = id();
    const ticket = supportTicket({ tenantId, agentTenantId, openedByUserId, assignedAgentId, conversationId });

    const supportTickets = {
      findById: vi.fn().mockResolvedValue(ticket),
    } as unknown as SupportTicketRepository;
    const conversations = {
      findById: vi.fn().mockResolvedValue({ isParticipant: (userId: string) => userId === agentTenantId }),
      listMessages: vi.fn().mockResolvedValue([
        message({ tenantId, conversationId, senderId: openedByUserId, senderDisplayName: openedByUserId }),
        message({
          tenantId,
          conversationId,
          senderId: agentTenantId,
          senderDisplayName: agentTenantId,
          body: 'Hola',
        }),
      ]),
    } as unknown as ConversationRepository;
    const messages = {
      receiptsForOwnMessages: vi.fn().mockResolvedValue(new Map()),
    } as unknown as MessageRepository;
    const userDirectory = {
      findByUserId: vi.fn(),
      findByUserIds: vi.fn().mockResolvedValue([
        {
          userId: openedByUserId,
          tenantId,
          displayName: 'Manuel Mena',
          email: 'manuel@example.com',
          isActive: true,
          actorType: 'TenantAdmin',
          updatedAtUtc: new Date(),
        },
      ]),
      upsert: vi.fn(),
      markInactive: vi.fn(),
      markActive: vi.fn(),
      searchByDisplayNameOrEmail: vi.fn(),
    } satisfies UserDirectoryRepository;

    const result = await getSupportAgentMessages(
      {
        ticketId: ticket.id,
        agent: {
          userId: assignedAgentId,
          tenantId: agentTenantId,
          hasAgentPermission: true,
          isPlatformAdmin: false,
        },
        take: 50,
      },
      { supportTickets, conversations, messages, userDirectory },
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value.items.map((item) => item.senderDisplayName)).toEqual(['Manuel Mena', 'Support Team']);
    expect(userDirectory.findByUserIds).toHaveBeenCalledWith([openedByUserId]);
  });
});
