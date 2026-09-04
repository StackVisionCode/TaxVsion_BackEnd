import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import { SupportTicket } from '../../src/domain/support/support-ticket.js';
import { sendSupportAgentMessage } from '../../src/application/use-cases/send-support-agent-message.js';
import type { SendSupportAgentMessageDeps } from '../../src/application/use-cases/send-support-agent-message.js';

function u(): string {
  return randomUUID();
}

/** Desempaqueta un Result de éxito o lanza (para armar fixtures en el test). */
function must<T>(r: { isSuccess: boolean; value?: T; error?: { message: string } }): T {
  if (!r.isSuccess) throw new Error(`fixture Result.fail: ${r.error?.message}`);
  return r.value as T;
}

/** Construye una conversación Support + su ticket, con el placeholder cuyo userId = tenant Platform. */
function makeTicketAndConversation(input: {
  customerTenant: string;
  platformTenant: string;
  customerUser: string;
}) {
  const conversation = must<Conversation>(
    Conversation.startSupport({
      tenantId: input.customerTenant,
      ticketId: u(),
      agent: { userId: input.platformTenant, displayName: 'Support Team', actorType: 'PlatformAdmin' },
      customer: { userId: input.customerUser, displayName: 'Cliente', actorType: 'CustomerPortal' },
    }),
  );
  const ticket = must<SupportTicket>(
    SupportTicket.open({
      tenantId: input.customerTenant,
      agentTenantId: input.platformTenant,
      openedByUserId: input.customerUser,
      conversationId: conversation.id,
      subject: 'Necesito ayuda',
    }),
  );
  return { conversation, ticket };
}

/** Deps mínimas: sendSupportAgentMessage → sendMessage(text) solo toca conversations/idempotency/settings/publisher. */
function makeDeps(input: {
  customerTenant: string;
  conversation: Conversation;
  ticket: SupportTicket | null;
}): SendSupportAgentMessageDeps {
  return {
    conversations: {
      async findById(tenantId: string, id: string) {
        return tenantId === input.customerTenant && id === input.conversation.id ? input.conversation : null;
      },
      async save() {},
      async findByUniquenessKey() {
        return null;
      },
      async listForUser() {
        return [];
      },
      async countForUser() {
        return 0;
      },
      async listMessages() {
        return [];
      },
      async countUnreadForUser() {
        return 0;
      },
    } as unknown as SendSupportAgentMessageDeps['conversations'],
    idempotency: {
      async tryReserve() {
        return { status: 'reserved', token: 'tok' };
      },
      async commit() {},
      async release() {},
    } as unknown as SendSupportAgentMessageDeps['idempotency'],
    publisher: { async enqueue() {} } as unknown as SendSupportAgentMessageDeps['publisher'],
    settings: {
      async get(tenantId: string) {
        return {
          tenantId,
          chatEnabled: true,
          employeeToEmployeeChatEnabled: true,
          restrictCustomerChatToAssignedPreparer: false,
          screenshotsEnabled: true,
          internalGroupsEnabled: true,
          messageRetentionDays: 365,
        };
      },
    } as unknown as SendSupportAgentMessageDeps['settings'],
    attachmentTracking: { async register() {} } as unknown as SendSupportAgentMessageDeps['attachmentTracking'],
    supportTickets: {
      async findById() {
        return input.ticket;
      },
      async save() {},
      async listForCustomer() {
        return [];
      },
      async countForCustomer() {
        return 0;
      },
      async listForAgentTenant() {
        return [];
      },
      async countForAgentTenant() {
        return 0;
      },
    } as unknown as SendSupportAgentMessageDeps['supportTickets'],
  };
}

describe('sendSupportAgentMessage', () => {
  const customerTenant = u();
  const platformTenant = u();
  const customerUser = u();
  const agentUser = u();

  it('falla Support.NotFound si el ticket no existe', async () => {
    const { conversation } = makeTicketAndConversation({ customerTenant, platformTenant, customerUser });
    const deps = makeDeps({ customerTenant, conversation, ticket: null });
    const r = await sendSupportAgentMessage(
      { correlationId: 'c', clientKey: u(), ticketId: u(), agent: { userId: agentUser, tenantId: platformTenant, isPlatformAdmin: false }, body: 'Hola' },
      deps,
    );
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Support.NotFound');
  });

  it('falla Auth.Forbidden si el agente NO reclamó (ticket Open) y no es PlatformAdmin', async () => {
    const { conversation, ticket } = makeTicketAndConversation({ customerTenant, platformTenant, customerUser });
    const deps = makeDeps({ customerTenant, conversation, ticket });
    const r = await sendSupportAgentMessage(
      { correlationId: 'c', clientKey: u(), ticketId: ticket.id, agent: { userId: agentUser, tenantId: platformTenant, isPlatformAdmin: false }, body: 'Hola' },
      deps,
    );
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Auth.Forbidden');
  });

  it('falla Support.Terminal si el ticket está cerrado', async () => {
    const { conversation, ticket } = makeTicketAndConversation({ customerTenant, platformTenant, customerUser });
    must<void>(ticket.claim({ agentUserId: agentUser }));
    must<void>(ticket.close({ byUserId: agentUser, isPlatformAdmin: false }));
    const deps = makeDeps({ customerTenant, conversation, ticket });
    const r = await sendSupportAgentMessage(
      { correlationId: 'c', clientKey: u(), ticketId: ticket.id, agent: { userId: agentUser, tenantId: platformTenant, isPlatformAdmin: false }, body: 'Hola' },
      deps,
    );
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('Support.Terminal');
  });

  it('el agente asignado envía como el placeholder "Support Team" (senderId = tenant Platform)', async () => {
    const { conversation, ticket } = makeTicketAndConversation({ customerTenant, platformTenant, customerUser });
    must<void>(ticket.claim({ agentUserId: agentUser }));
    const deps = makeDeps({ customerTenant, conversation, ticket });
    const r = await sendSupportAgentMessage(
      { correlationId: 'c', clientKey: u(), ticketId: ticket.id, agent: { userId: agentUser, tenantId: platformTenant, isPlatformAdmin: false }, body: 'Hola, soy soporte' },
      deps,
    );
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) {
      expect(r.value.message.senderId).toBe(platformTenant); // placeholder
      expect(r.value.message.senderDisplayName).toBe('Support Team');
      expect(r.value.message.body).toBe('Hola, soy soporte');
      expect(r.value.customerTenantId).toBe(customerTenant);
      expect(r.value.conversationId).toBe(conversation.id);
      expect(r.value.recipientUserIds).toContain(customerUser); // el cliente, para el delivered
    }
  });

  it('PlatformAdmin puede enviar aunque no sea el agente asignado', async () => {
    const { conversation, ticket } = makeTicketAndConversation({ customerTenant, platformTenant, customerUser });
    must<void>(ticket.claim({ agentUserId: u() })); // asignado a OTRO agente
    const deps = makeDeps({ customerTenant, conversation, ticket });
    const r = await sendSupportAgentMessage(
      { correlationId: 'c', clientKey: u(), ticketId: ticket.id, agent: { userId: u(), tenantId: platformTenant, isPlatformAdmin: true }, body: 'Intervención admin' },
      deps,
    );
    expect(r.isSuccess).toBe(true);
  });
});
