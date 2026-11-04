import { Conversation } from '../../domain/conversations/conversation.js';
import { ConversationKind } from '../../domain/conversations/conversation-kind.js';
import { isStaffActor } from '../../domain/shared/permissions.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../ports/customer-assignment-projection-repository.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import { seesAllCustomers } from './customer-visibility.js';
import { listUserConversations } from './user-conversations.js';

/** Lo que hace falta para decidir si la oficina lee un chat de cliente cerrado por el retiro. */
export interface ClosedClientChatAccessDeps {
  readonly userPermissions?: UserPermissionsProjectionRepository | undefined;
  readonly customerAssignments?: Pick<CustomerAssignmentProjectionRepository, 'isAssigned'> | undefined;
  readonly customerPortalAccounts?: Pick<CustomerPortalAccountRepository, 'findActiveByUserId'> | undefined;
}

/**
 * D-4: el chat Direct de un cliente con un empleado retirado queda cerrado y su historial es procedencia. Lo
 * lee (solo lectura) el staff de la oficina que ve a ese cliente: quien ve a todos (`customers.view_all`,
 * PlatformAdmin) o quien lo tiene asignado (p.ej. el sucesor). Nadie más, y nunca se escribe.
 */
export async function canOfficeReadClosedClientChat(
  conversation: Conversation,
  requester: { readonly userId: string; readonly actorType?: string | undefined },
  deps: ClosedClientChatAccessDeps,
): Promise<boolean> {
  if (!requester.actorType || !isStaffActor(requester.actorType) || !deps.userPermissions) return false;
  if (!conversation.isClosedClientChat()) return false;
  if (await seesAllCustomers({ userId: requester.userId, actorType: requester.actorType }, deps.userPermissions)) return true;
  const client = conversation.closedChatClient();
  if (!client || !deps.customerAssignments) return false;
  const customerId = client.customerId ?? (await deps.customerPortalAccounts?.findActiveByUserId(client.userId))?.customerId ?? null;
  return customerId !== null && (await deps.customerAssignments.isAssigned(conversation.tenantId, customerId, requester.userId));
}

/** Un chat del cliente cerrado por el retiro de su staff (lo que la ficha del cliente ofrece abrir). */
export interface ClosedClientChatItem {
  readonly conversationId: string;
  readonly formerStaffUserId: string;
  readonly formerStaffDisplayName: string;
  readonly closedAtUtc: string | null;
  readonly lastMessageAtUtc: string | null;
}

/** Chats cerrados por un retiro de este cliente (vía su cuenta de portal activa), el más reciente primero. */
export async function listClosedClientChats(
  input: { tenantId: string; customerId: string },
  deps: {
    customerPortalAccounts: Pick<CustomerPortalAccountRepository, 'findActiveByCustomerId'>;
    conversations: Pick<ConversationRepository, 'listForUser'>;
  },
): Promise<ClosedClientChatItem[]> {
  const portal = await deps.customerPortalAccounts.findActiveByCustomerId(input.customerId);
  if (!portal || portal.tenantId !== input.tenantId) return [];
  const chats = await listUserConversations(deps.conversations, {
    tenantId: input.tenantId,
    userId: portal.userId,
    kinds: [ConversationKind.Direct],
    includeArchived: true,
  });
  return chats
    .filter((c) => Conversation.rehydrate(c).isClosedClientChat())
    .flatMap((c) => {
      const former = c.participants.find((p) => p.isRemoved && isStaffActor(p.actorType));
      if (!former) return [];
      return [
        {
          conversationId: c.id,
          formerStaffUserId: former.userId,
          formerStaffDisplayName: former.displayName,
          closedAtUtc: former.removedAtUtc ? former.removedAtUtc.toISOString() : null,
          lastMessageAtUtc: c.lastMessageAtUtc ? c.lastMessageAtUtc.toISOString() : null,
        },
      ];
    })
    .sort((a, b) => (b.closedAtUtc ?? '').localeCompare(a.closedAtUtc ?? ''));
}
