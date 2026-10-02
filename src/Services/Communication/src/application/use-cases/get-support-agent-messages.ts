import { Result, makeError } from '../../domain/shared/result.js';
import type { MessageDto } from '../../contracts/socket/chat-socket-events.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type {
  UserDirectoryEntrySnapshot,
  UserDirectoryRepository,
} from '../ports/user-directory-repository.js';
import { getMessages, type GetMessagesResult } from './get-messages.js';

/**
 * Historial del chat de un ticket para el agente. La conversacion vive en el tenant
 * del cliente y el agente real no es participante, asi que se lee como el
 * placeholder "Support Team" y luego se hidratan nombres desde UserDirectory.
 */
export interface GetSupportAgentMessagesCommand {
  readonly ticketId: string;
  readonly agent: {
    userId: string;
    tenantId: string;
    hasAgentPermission: boolean;
    isPlatformAdmin: boolean;
  };
  readonly take: number;
  readonly beforeUtc?: string | undefined;
}

export interface GetSupportAgentMessagesDeps {
  readonly supportTickets: SupportTicketRepository;
  readonly conversations: ConversationRepository;
  readonly messages: MessageRepository;
  readonly userDirectory: UserDirectoryRepository;
}

export async function getSupportAgentMessages(
  cmd: GetSupportAgentMessagesCommand,
  deps: GetSupportAgentMessagesDeps,
): Promise<Result<GetMessagesResult>> {
  const ticket = await deps.supportTickets.findById(cmd.ticketId);
  if (!ticket) {
    return Result.fail(makeError('Support.NotFound', 'Support ticket not found.'));
  }

  const canAccess = ticket.canBeAccessedBy({
    actorUserId: cmd.agent.userId,
    actorTenantId: cmd.agent.tenantId,
    actorHasAgentPermission: cmd.agent.hasAgentPermission,
    isPlatformAdmin: cmd.agent.isPlatformAdmin,
  });
  if (!canAccess) {
    return Result.fail(makeError('Auth.Forbidden', 'Not allowed to read this support ticket.'));
  }

  const snap = ticket.toSnapshot();
  const result = await getMessages(
    {
      tenantId: snap.tenantId,
      conversationId: snap.conversationId,
      requesterUserId: snap.agentTenantId,
      take: cmd.take,
      ...(cmd.beforeUtc !== undefined ? { beforeUtc: cmd.beforeUtc } : {}),
    },
    deps,
  );
  if (!result.isSuccess) {
    return result;
  }

  const items = await hydrateSupportMessageSenders(result.value.items, snap, deps.userDirectory);
  return Result.ok({ ...result.value, items });
}

async function hydrateSupportMessageSenders(
  messages: readonly MessageDto[],
  ticket: {
    readonly agentTenantId: string;
    readonly openedByUserId: string;
  },
  userDirectory: UserDirectoryRepository,
): Promise<readonly MessageDto[]> {
  const userIds = [
    ...new Set(
      messages
        .filter((message) => message.kind !== 'System' && message.senderId !== ticket.agentTenantId)
        .map((message) => message.senderId),
    ),
  ];
  const directory = await readUsers(userIds, userDirectory);

  return messages.map((message) => {
    if (message.senderId === ticket.agentTenantId) {
      return message.senderDisplayName === 'Support Team'
        ? message
        : { ...message, senderDisplayName: 'Support Team' };
    }

    const projected = directory.get(message.senderId);
    const displayName = projected?.displayName?.trim() || projected?.email?.trim();
    if (displayName) {
      return { ...message, senderDisplayName: displayName };
    }

    if (isGuidLike(message.senderDisplayName)) {
      return { ...message, senderDisplayName: 'Requester' };
    }

    return message;
  });
}

async function readUsers(
  userIds: readonly string[],
  userDirectory: UserDirectoryRepository,
): Promise<ReadonlyMap<string, UserDirectoryEntrySnapshot>> {
  if (userIds.length === 0) {
    return new Map();
  }

  const entries = userDirectory.findByUserIds
    ? await userDirectory.findByUserIds(userIds)
    : await Promise.all(userIds.map((userId) => userDirectory.findByUserId(userId))).then((items) =>
        items.filter((item): item is UserDirectoryEntrySnapshot => item !== null),
      );

  return new Map(entries.map((entry) => [entry.userId, entry]));
}

function isGuidLike(value: string | null | undefined): boolean {
  return (
    typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value.trim())
  );
}
