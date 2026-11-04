import { ConversationKind } from '../../domain/conversations/conversation-kind.js';
import { isTerminalSupport } from '../../domain/support/support-enums.js';
import type { SupportTicketSnapshot } from '../../domain/support/support-ticket.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { SupportTicketRepository } from '../ports/support-ticket-repository.js';
import { listUserConversations } from './user-conversations.js';

/**
 * Pre-flight del retiro (punto 3.2): lo que Communication va a mover si se retira a este empleado. Aditivo:
 * `activeMeetings` conserva su significado original (reuniones activas que hospeda).
 */
export interface OffboardingImpact {
  /** Reuniones Scheduled/Live que hospeda (se ceden al sucesor o se cancelan/terminan). */
  readonly activeMeetings: number;
  /** Chats Direct con clientes donde es el preparador principal (se cierran y siguen con el sucesor). */
  readonly clientChats: number;
  /** Grupos activos de los que es Owner (la propiedad pasa por sucesión). */
  readonly groupsOwned: number;
  /** Tickets de soporte no terminales que abrió o atiende como agente. */
  readonly openSupportTickets: number;
  /** Reuniones Scheduled de otros donde figura como participante o invitado (sale de ellas). */
  readonly scheduledMeetingsAsParticipant: number;
}

const SUPPORT_PAGE = 100;

export async function getOffboardingImpact(
  input: { tenantId: string; userId: string },
  deps: {
    meetings: Pick<MeetingRepository, 'countActiveHostedBy' | 'countScheduledWithParticipant'>;
    conversations: Pick<ConversationRepository, 'listForUser'>;
    supportTickets: Pick<SupportTicketRepository, 'listForCustomer' | 'listForAgentTenant'>;
  },
): Promise<OffboardingImpact> {
  const [activeMeetings, scheduledMeetingsAsParticipant, direct, groups, opened, assigned] = await Promise.all([
    deps.meetings.countActiveHostedBy(input.tenantId, input.userId),
    deps.meetings.countScheduledWithParticipant(input.tenantId, input.userId),
    listUserConversations(deps.conversations, { ...input, kinds: [ConversationKind.Direct], includeArchived: false }),
    listUserConversations(deps.conversations, { ...input, kinds: [ConversationKind.Group], includeArchived: false }),
    countOpenTickets((skip) => deps.supportTickets.listForCustomer({ tenantId: input.tenantId, openedByUserId: input.userId, take: SUPPORT_PAGE, skip })),
    countOpenTickets((skip) =>
      deps.supportTickets.listForAgentTenant({ agentTenantId: input.tenantId, assignedAgentId: input.userId, take: SUPPORT_PAGE, skip }),
    ),
  ]);

  const clientChats = direct.filter((c) => {
    const me = c.participants.find((p) => p.userId === input.userId && !p.isRemoved);
    return !!me?.isPrimaryPreparer && c.participants.some((p) => !p.isRemoved && p.actorType === 'CustomerPortal');
  }).length;
  const groupsOwned = groups.filter((c) => c.participants.some((p) => p.userId === input.userId && !p.isRemoved && p.role === 'Owner')).length;

  return { activeMeetings, clientChats, groupsOwned, openSupportTickets: opened + assigned, scheduledMeetingsAsParticipant };
}

async function countOpenTickets(page: (skip: number) => Promise<readonly SupportTicketSnapshot[]>): Promise<number> {
  let count = 0;
  for (let skip = 0; ; skip += SUPPORT_PAGE) {
    const rows = await page(skip);
    count += rows.filter((t) => !isTerminalSupport(t.status)).length;
    if (rows.length < SUPPORT_PAGE) return count;
  }
}
